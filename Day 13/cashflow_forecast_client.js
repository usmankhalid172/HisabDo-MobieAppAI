/**
 * Cash Flow Forecast — Mobile/Web Integration Layer
 * HisabDo AI, Day 13 — Task owner: Laiba
 *
 * Spec §15 assigns this exactly (job steps 49–58):
 *   49. Create Cash Flow Forecast section/card   54. Show AI explanation
 *   50. Show forecast period                     55. Clearly label predicted values as forecast
 *   51. Show expected income/expense/saving      56. Create limited-data and no-data states
 *   52. Show expected closing balance            57. Handle API loading/error states
 *   53. Show confidence/data-quality indicator   58. Test mobile/web presentation
 *
 * Sources read:
 *   day-13-forecasting/HisabDo_AI_Day13_Cash_Flow_Forecasting_Detailed_Specification.pdf
 *   explanantion layer/Day13_AI_Forecast_Explanation_Service.cs
 *   Forecasting/*.cs  (branch: feature/calculations — the real implementation)
 *
 * ══════════════════════════════════════════════════════════════════════
 *  THE TWO CONTRACTS DO NOT MATCH. This layer supports both.
 * ══════════════════════════════════════════════════════════════════════
 *
 * (A) SPEC CONTRACT — §9 of the Day 13 specification:
 *     GET /api/ai/cash-flow-forecast/{userId}?months=6&horizonMonths=1
 *     {
 *       userId, historyMonthsUsed, forecastHorizonMonths,
 *       expectedIncome, expectedExpense, expectedSaving,
 *       openingBalance, expectedClosingBalance,
 *       confidence, method, limitations[]
 *     }
 *
 * (B) REAL IMPLEMENTED CONTRACT — Forecasting/ForecastingController.cs:
 *     POST /api/forecasting            <-- POST, not GET. Different path.
 *     body: { userId, from, to, frequency, forecastPeriods }
 *     {
 *       userId, frequency,
 *       historical: [ { period, income, expense, savings, openingBalance, closingBalance } ],
 *       expected:   [ { period, income, expense, savings, openingBalance, closingBalance } ]
 *     }
 *
 * Differences that affect this task directly:
 *
 *   1. NO `confidence` FIELD IN THE REAL API. Spec §8 defines a full
 *      confidence model (High / Medium / Low / Unavailable / Limited) and
 *      §15 step 53 requires the UI to display it. The implemented service
 *      returns nothing of the sort. This layer derives a confidence band
 *      from `historical.length` using the spec's own §8 thresholds, and
 *      marks it `derived` so the UI can label it honestly rather than
 *      presenting a client-side guess as a backend verdict.
 *
 *   2. NO `limitations[]` AND NO `method` FIELD in the real API either.
 *      Rendered only when the payload actually carries them.
 *
 *   3. INSUFFICIENT DATA ARRIVES AS AN HTTP 400, NOT A DATA STATE.
 *      ForecastingService throws ForecastValidationException when fewer
 *      than 3 periods contain data ("At least 3 periods containing
 *      financial data are required."). Spec §5/§8 instead expects a
 *      Low/Limited *forecast result*. A naive client shows that 400 as a
 *      generic "something went wrong" error — which is wrong, and hides a
 *      state the task explicitly asks for (requirement 9). isInsufficientDataError()
 *      below detects it so it routes to the insufficient-data state instead.
 *
 *   4. REAL API RETURNS `historical[]`, WHICH THE SPEC CONTRACT DOES NOT.
 *      This is genuinely better for requirement 7 (historical vs forecast
 *      comparison) — the spec's flat shape gives nothing to compare against.
 *      Under contract (A) the comparison section is hidden rather than faked.
 *
 *   5. NO AI EXPLANATION IS RETURNED BY EITHER ENDPOINT.
 *      ForecastExplanationService (Aarti's, §14) exists as a standalone C#
 *      class producing { summary, expectedIncome, expectedExpense,
 *      expectedSaving, expectedClosingBalance, cashFlowTrend,
 *      actionableObservation, uncertainty } — but neither controller calls
 *      it or includes it in the response. This layer renders that object
 *      whenever the payload carries it, and otherwise builds a clearly-marked
 *      fallback from verified figures only (see buildFallbackInsight).
 *
 * GOLDEN RULE (§1, §7, §14): the forecasting engine is the source of numeric
 * truth. Nothing in this file recalculates, adjusts, or invents a financial
 * value. Derived confidence is a data-quality label, not a financial figure,
 * and it is flagged as derived wherever it is shown.
 */

const API_BASE_URL = "https://api.hisabdo.local"; // replace with the real HisabDo backend host

export class ForecastAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Calling the API — one function per contract
// ---------------------------------------------------------------------

/** Contract (B) — the real implemented endpoint. POST with a JSON body. */
export async function fetchForecast(
  { userId, from, to, frequency = "Monthly", forecastPeriods = 1 },
  { baseUrl = API_BASE_URL, authToken = null } = {}
) {
  const headers = { "Content-Type": "application/json" };
  // Controller is [Authorize] and reads the user from the token claim.
  if (authToken) headers["Authorization"] = `Bearer ${authToken}`;

  let response;
  try {
    response = await fetch(`${baseUrl}/api/forecasting`, {
      method: "POST",
      headers,
      body: JSON.stringify({ userId, from, to, frequency, forecastPeriods }),
    });
  } catch (err) {
    throw new ForecastAPIError(0, `Network/connection error: ${err.message}`);
  }
  return handleResponse(response);
}

/** Contract (A) — the spec's documented endpoint, in case the backend ships this shape. */
export async function fetchForecastSpecContract(
  userId,
  { months = 6, horizonMonths = 1 } = {},
  { baseUrl = API_BASE_URL, authToken = null } = {}
) {
  const headers = {};
  if (authToken) headers["Authorization"] = `Bearer ${authToken}`;

  const url =
    `${baseUrl}/api/ai/cash-flow-forecast/${encodeURIComponent(userId)}` +
    `?months=${months}&horizonMonths=${horizonMonths}`;

  let response;
  try {
    response = await fetch(url, { headers });
  } catch (err) {
    throw new ForecastAPIError(0, `Network/connection error: ${err.message}`);
  }
  return handleResponse(response);
}

async function handleResponse(response) {
  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.error || body.detail || body.message || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new ForecastAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error handling (requirement 10; spec §15 step 57)
// ---------------------------------------------------------------------

/**
 * Difference #3 above: the real service signals "not enough history" as a
 * 400 validation error. That is an insufficient-data STATE, not a failure,
 * and must not be shown as a generic error.
 */
export function isInsufficientDataError(error) {
  if (error.statusCode !== 400) return false;
  const detail = String(error.detail || "").toLowerCase();
  return (
    detail.includes("periods containing financial data") ||
    detail.includes("insufficient") ||
    detail.includes("not enough")
  );
}

export function handleApiError(error) {
  if (isInsufficientDataError(error)) {
    return {
      state: "insufficient_data",
      title: "Not enough history yet",
      message:
        "We need at least 3 months of income and expense history before we can estimate your cash flow. Keep recording transactions and your forecast will appear here.",
    };
  }
  if (error.statusCode === 400) {
    return { state: "invalid_input", title: "Check the dates", message: "That date range isn't valid. Try a different period." };
  }
  if (error.statusCode === 401 || error.statusCode === 403) {
    return { state: "unauthorized", title: "Can't load your forecast", message: "You don't have access to this data. Try signing in again." };
  }
  if (error.statusCode === 404) {
    return { state: "no_data", title: "No records found", message: "We couldn't find any transactions for this account." };
  }
  // 503 ForecastUnavailableException — history or balance data couldn't load.
  if (error.statusCode === 503) {
    return { state: "unavailable", title: "Forecast temporarily unavailable", message: "We couldn't load your financial history right now. Please try again shortly." };
  }
  // 502 ForecastEngineException — the engine itself failed.
  if (error.statusCode === 502) {
    return { state: "engine_error", title: "Couldn't build your forecast", message: "The forecasting service didn't return a usable result. Please try again shortly." };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "We couldn't load your forecast right now. Please try again." };
}

// ---------------------------------------------------------------------
// 3. Confidence — spec §8 thresholds (see difference #1)
// ---------------------------------------------------------------------
const CONFIDENCE_COPY = {
  High: "Based on 6 months of fairly complete history.",
  Medium: "Based on 3–5 months of history.",
  Low: "Based on fewer than 3 months of history — treat this as a rough guide.",
  Limited: "Some historical months are missing or inconsistent.",
  Unavailable: "There isn't enough verified history to produce a forecast.",
};

/** Applies the spec's own §8 bands to a usable-month count. */
export function deriveConfidence(usableMonths) {
  if (!usableMonths || usableMonths <= 0) return "Unavailable";
  if (usableMonths >= 6) return "High";
  if (usableMonths >= 3) return "Medium";
  return "Low";
}

// ---------------------------------------------------------------------
// 4. Fallback insight (see difference #5)
//    Built only from verified figures already in the payload. Adds no
//    number of its own, and makes no guarantee about future results (§8).
// ---------------------------------------------------------------------
function buildFallbackInsight({ expectedIncome, expectedExpense, expectedSaving, expectedClosingBalance, confidence, historyMonthsUsed }) {
  const money = (v) => `Rs ${Math.round(v).toLocaleString()}`;
  const lines = [];

  if (expectedIncome != null && expectedExpense != null) {
    lines.push(
      `Based on your recent pattern, expected income is ${money(expectedIncome)} and expected expenses are ${money(expectedExpense)} for the forecast period.`
    );
  }
  if (expectedSaving != null) {
    lines.push(
      expectedSaving > 0
        ? `That leaves an expected saving of ${money(expectedSaving)} — a positive cash flow outlook.`
        : expectedSaving < 0
        ? `That points to spending exceeding income by ${money(Math.abs(expectedSaving))} — a negative cash flow outlook worth reviewing.`
        : `Income and expenses are expected to roughly balance out.`
    );
  }
  if (expectedClosingBalance != null) {
    lines.push(`Expected closing balance is ${money(expectedClosingBalance)}.`);
  } else {
    lines.push("Closing balance isn't available because no trusted opening balance was provided.");
  }

  if (historyMonthsUsed) {
    lines.push(`This estimate uses ${historyMonthsUsed} month${historyMonthsUsed === 1 ? "" : "s"} of history.`);
  }
  if (confidence && CONFIDENCE_COPY[confidence]) {
    lines.push(CONFIDENCE_COPY[confidence]);
  }
  lines.push("Actual results can differ from this estimate.");

  return lines.join(" ");
}

// ---------------------------------------------------------------------
// 5. Normalise either contract into one display payload
// ---------------------------------------------------------------------

/** True when the payload looks like contract (B) — the real implementation. */
function isRealContract(payload) {
  return Array.isArray(payload.expected) || Array.isArray(payload.historical);
}

export function buildDisplayPayload(apiResponse) {
  return isRealContract(apiResponse)
    ? normaliseRealContract(apiResponse)
    : normaliseSpecContract(apiResponse);
}

function normaliseRealContract(payload) {
  const historical = payload.historical || [];
  const expected = payload.expected || [];

  // Only months that actually carry data count toward confidence — matching
  // the service's own MinimumUsablePeriods check.
  const usableMonths = historical.filter((p) => (p.income ?? 0) !== 0 || (p.expense ?? 0) !== 0).length;

  const first = expected[0] || null;
  const confidence = deriveConfidence(usableMonths);

  const core = {
    expectedIncome: first ? first.income ?? null : null,
    expectedExpense: first ? first.expense ?? null : null,
    expectedSaving: first ? first.savings ?? null : null,
    openingBalance: first ? first.openingBalance ?? null : null,
    expectedClosingBalance: first ? first.closingBalance ?? null : null,
  };

  return {
    contract: "real",
    userId: payload.userId ?? null,
    frequency: payload.frequency ?? "Monthly",
    forecastPeriodLabel: buildPeriodLabel(expected),
    historyMonthsUsed: usableMonths,
    ...core,
    confidence,
    confidenceIsDerived: true, // difference #1 — the API supplied none
    confidenceNote: CONFIDENCE_COPY[confidence] || null,
    method: payload.method ?? null,
    limitations: payload.limitations || [],
    // Requirement 7 — real historical data to compare against.
    historicalPeriods: historical.map(normalisePeriod),
    expectedPeriods: expected.map(normalisePeriod),
    hasComparison: historical.length > 0,
    insight: payload.explanation || payload.aiExplanation || null,
    insightIsFallback: !(payload.explanation || payload.aiExplanation),
    fallbackInsight: buildFallbackInsight({ ...core, confidence, historyMonthsUsed: usableMonths }),
    isForecastAvailable: expected.length > 0,
  };
}

function normaliseSpecContract(payload) {
  const confidence = payload.confidence || deriveConfidence(payload.historyMonthsUsed);

  const core = {
    expectedIncome: payload.expectedIncome ?? null,
    expectedExpense: payload.expectedExpense ?? null,
    expectedSaving: payload.expectedSaving ?? null,
    openingBalance: payload.openingBalance ?? null,
    expectedClosingBalance: payload.expectedClosingBalance ?? null,
  };

  return {
    contract: "spec",
    userId: payload.userId ?? null,
    frequency: "Monthly",
    forecastPeriodLabel:
      payload.forecastHorizonMonths === 1
        ? "Next month"
        : `Next ${payload.forecastHorizonMonths} months`,
    historyMonthsUsed: payload.historyMonthsUsed ?? null,
    ...core,
    confidence,
    confidenceIsDerived: !payload.confidence,
    confidenceNote: CONFIDENCE_COPY[confidence] || null,
    method: payload.method ?? null,
    limitations: payload.limitations || [],
    // Difference #4 — this contract carries no history, so nothing to compare.
    historicalPeriods: [],
    expectedPeriods: [],
    hasComparison: false,
    insight: payload.explanation || payload.aiExplanation || null,
    insightIsFallback: !(payload.explanation || payload.aiExplanation),
    fallbackInsight: buildFallbackInsight({ ...core, confidence, historyMonthsUsed: payload.historyMonthsUsed }),
    isForecastAvailable: payload.expectedIncome != null || payload.expectedExpense != null,
  };
}

function normalisePeriod(p) {
  return {
    period: p.period,
    income: p.income ?? null,
    expense: p.expense ?? null,
    savings: p.savings ?? null,
    openingBalance: p.openingBalance ?? null,
    closingBalance: p.closingBalance ?? null,
  };
}

function buildPeriodLabel(expected) {
  if (!expected.length) return "—";
  if (expected.length === 1) return expected[0].period;
  return `${expected[0].period} – ${expected[expected.length - 1].period}`;
}

// ---------------------------------------------------------------------
// 6. Single entry point for the UI
// ---------------------------------------------------------------------
export async function getCashFlowForecastForUI(request, options = {}) {
  try {
    const raw = await fetchForecast(request, options);
    return { ok: true, data: buildDisplayPayload(raw) };
  } catch (err) {
    if (err instanceof ForecastAPIError) {
      return { ok: false, errorState: handleApiError(err) };
    }
    throw err;
  }
}
