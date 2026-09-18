/**
 * Spending Intelligence — Web/Mobile Integration Layer (v3 — CORRECTED)
 * HisabDo AI, Day 11 — Task owner: Laiba (API + Mobile/Web Integration)
 *
 * ---------------------------------------------------------------------
 * WHY THIS FILE WAS REBUILT (read this before trusting anything below):
 * ---------------------------------------------------------------------
 * Two earlier versions of this integration existed, and both targeted an
 * endpoint that does not exist anywhere in the code actually delivered:
 *   v1 assumed  GET /api/ai/spending-patterns/{userId}
 *   v2 assumed  GET /api/v1/spending-pattern-intelligence/{user_id}
 * Neither endpoint, nor the Python service files they reference, are in
 * the uploaded backend. The REAL backend (BudgetAnalysisService.zip,
 * verified by reading every .cs file in it) exposes two ASP.NET Core
 * endpoints instead:
 *
 *   POST /api/budgets/analysis     body: { userId, from, to }
 *     -> BudgetAnalysisResult: { userId, from, to, status,
 *          monthsWithData, totalIncome, totalExpense,
 *          categories: [{ category, averageMonthlySpend, currentBudget,
 *                          utilizationPercent, recommendedBudget,
 *                          needsAdjustment, adjustmentReason }] }
 *
 *   POST /api/forecasting          body: { userId, from, to, frequency, forecastPeriods }
 *     -> ForecastResult: { userId, frequency,
 *          historical: [{ period, income, expense, savings, openingBalance, closingBalance }],
 *          expected:   [ ...same shape... ] }
 *
 * Both require [Authorize]; the demo auth handler included in the zip
 * (DemoAuthenticationHandler) accepts any request and always authenticates
 * as a fixed demo user (11111111-1111-1111-1111-111111111111) — so
 * `userId` in the request body MUST equal that GUID against the demo
 * backend, or the service returns 400 ("Invalid user ID."). Point
 * `AUTH_HEADER` at the real HisabDo auth token once real auth is wired in.
 *
 * WHAT THE REAL BACKEND GIVES YOU vs. DOES NOT (verified by reading the
 * source, not guessed):
 *
 *   Total spending            -> totalExpense (real, from /api/budgets/analysis)
 *   Category amounts          -> NOT returned directly. The API only returns
 *                                 averageMonthlySpend per category. This layer
 *                                 recovers the real total per category as
 *                                 averageMonthlySpend * monthsBetween(from, to)
 *                                 -- the exact formula BudgetAnalysisService.cs
 *                                 uses in reverse -- so no new number is
 *                                 invented, an existing verified one is
 *                                 un-averaged.
 *   Category percentages      -> derived: categoryTotal / totalExpense * 100
 *   Top categories (ranked)   -> NOT pre-ranked by the API; sorted client-side
 *                                 by the recovered category total.
 *   Monthly spending trend    -> historical[].expense from /api/forecasting
 *                                 (one call, real per-month data, no workaround).
 *   Recurring spending        -> CANNOT be derived from either endpoint. Both
 *                                 only return aggregated totals, never
 *                                 individual transactions (no per-transaction
 *                                 date/description to detect a repeating
 *                                 charge). Reported as unavailable, not
 *                                 fabricated. See getRecurringStatus().
 *   AI Spending Insight       -> No insight/LLM field exists in either
 *                                 response. Rule-based fallback built only
 *                                 from verified fields (adjustmentReason,
 *                                 needsAdjustment, trend direction) — see
 *                                 buildFallbackInsight() — clearly marked
 *                                 as a fallback in the returned payload.
 *   Empty / no-data state     -> status === "insufficient_data" (fewer than
 *                                 3 months of transaction data, or no budgets
 *                                 configured at all — see BudgetAnalysisService.cs).
 *                                 Note this can still have totalIncome/
 *                                 totalExpense > 0; it's not the same as
 *                                 "literally zero transactions".
 */

const API_BASE_URL = "http://localhost:5000"; // default Kestrel dev port for `dotnet run`
const DEMO_USER_ID = "11111111-1111-1111-1111-111111111111"; // DemoAuthenticationHandler.DemoUserId
const AUTH_HEADER = ""; // set to "Bearer <token>" once real auth exists; not required by the demo handler

export class SpendingIntelligenceAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the two real endpoints
// ---------------------------------------------------------------------
async function postJson(path, body, { baseUrl = API_BASE_URL, authHeader = AUTH_HEADER } = {}) {
  let response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(authHeader ? { Authorization: authHeader } : {}),
      },
      body: JSON.stringify(body),
    });
  } catch (err) {
    throw new SpendingIntelligenceAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const errBody = await response.json();
    detail = errBody.error || JSON.stringify(errBody);
  } catch {
    detail = await response.text();
  }
  throw new SpendingIntelligenceAPIError(response.status, detail);
}

export function fetchBudgetAnalysis(userId, from, to, opts) {
  return postJson("/api/budgets/analysis", { userId, from, to }, opts);
}

export function fetchForecast(userId, from, to, { frequency = "Monthly", forecastPeriods = 1 } = {}, opts) {
  return postJson("/api/forecasting", { userId, from, to, frequency, forecastPeriods }, opts);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state mapping (matches the real controllers' actual codes:
//    401 Unauthorized, 400 ForecastValidationException, 503 ForecastUnavailableException,
//    502 ForecastEngineException — forecasting endpoint only)
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 400) {
    return {
      state: "invalid_input",
      title: "Check the request",
      message: "The user ID or date range isn't valid.",
    };
  }
  if (error.statusCode === 401) {
    return {
      state: "unauthorized",
      title: "Can't load this data",
      message: "This request wasn't authorized to reach the server.",
    };
  }
  if (error.statusCode === 503) {
    return {
      state: "unavailable",
      title: "Data temporarily unavailable",
      message: "We couldn't load your financial data just now. Please try again shortly.",
    };
  }
  if (error.statusCode === 502) {
    return {
      state: "forecast_engine_error",
      title: "Trend data unavailable",
      message: "The forecasting engine couldn't produce a result. Category totals are still shown.",
    };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "Please try again in a moment." };
}

// ---------------------------------------------------------------------
// 3. Helpers — category total recovery, ranking, insight, recurring status
// ---------------------------------------------------------------------

/** Exact same formula as BudgetAnalysisService.cs's MonthsBetween(). */
function monthsBetween(fromStr, toStr) {
  const from = new Date(fromStr);
  const to = new Date(toStr);
  return (to.getUTCFullYear() - from.getUTCFullYear()) * 12 + (to.getUTCMonth() - from.getUTCMonth()) + 1;
}

/**
 * Recovers each category's real total for the period from its
 * averageMonthlySpend, ranks them, and computes % of totalExpense.
 * (averageMonthlySpend = categoryTotal / monthsBetween(from,to), per the
 * C# source — this just inverts that, it doesn't invent a new figure.)
 */
function rankTopCategories(analysisResult, limit = 5) {
  const months = monthsBetween(analysisResult.from, analysisResult.to);
  const totalExpense = analysisResult.totalExpense || 0;

  return analysisResult.categories
    .map((c) => {
      const amount = Math.round(c.averageMonthlySpend * months * 100) / 100;
      return {
        category: c.category,
        amount,
        percentage: totalExpense > 0 ? Math.round((amount / totalExpense) * 10000) / 100 : null,
        budgetStatus: c.needsAdjustment ? "Needs Attention" : "On Track",
        adjustmentReason: c.adjustmentReason,
      };
    })
    .sort((a, b) => b.amount - a.amount)
    .slice(0, limit);
}

/**
 * Recurring spending CANNOT be computed from either endpoint — both only
 * return aggregated totals, never individual transactions.
 */
function getRecurringStatus() {
  return {
    available: false,
    reason:
      "Neither /api/budgets/analysis nor /api/forecasting returns individual transactions " +
      "(only category averages and period totals), so a genuinely repeating charge can't be " +
      "detected from this API. Needs a transaction-level endpoint or the raw transaction list.",
  };
}

/** Rule-based fallback insight — built only from verified fields, never invented. */
function buildFallbackInsight({ totalExpense, topCategories, historicalTrend }) {
  const lines = [];

  if (topCategories.length) {
    const top = topCategories[0];
    lines.push(
      top.percentage != null
        ? `${top.category} is the largest spending category at ${top.percentage}% of total spending.`
        : `${top.category} is the largest spending category.`
    );
  }

  const flagged = topCategories.filter((c) => c.budgetStatus === "Needs Attention" && c.adjustmentReason === "Average spending exceeds the budget.");
  if (flagged.length) {
    lines.push(`${flagged.map((c) => c.category).join(", ")} ${flagged.length > 1 ? "are" : "is"} over budget.`);
  }

  if (historicalTrend && historicalTrend.length >= 2) {
    const first = historicalTrend[0].expense;
    const last = historicalTrend[historicalTrend.length - 1].expense;
    if (first > 0) {
      const change = Math.round(((last - first) / first) * 10000) / 100;
      if (change > 10) lines.push(`Monthly spending has trended up ${change}% over this period.`);
      else if (change < -10) lines.push(`Monthly spending has trended down ${Math.abs(change)}% over this period.`);
    }
  }

  if (!lines.length) lines.push(`Total spending for this period is Rs ${Math.round(totalExpense).toLocaleString()}.`);
  return lines.join(" ");
}

// ---------------------------------------------------------------------
// 4. Build the Mobile/Web display structure from the two real responses
// ---------------------------------------------------------------------
export function buildDisplayPayload(analysisResult, forecastResult) {
  const isEmpty = analysisResult.status === "insufficient_data";

  if (isEmpty) {
    return {
      period: { from: analysisResult.from, to: analysisResult.to },
      isEmpty: true,
      emptyReason:
        "Not enough data yet — this view needs at least 3 months of transaction history and at least " +
        "one budget category configured before it can show a breakdown.",
      totalSpending: { amount: analysisResult.totalExpense, display: `Rs ${Math.round(analysisResult.totalExpense).toLocaleString()}` },
    };
  }

  const topCategories = rankTopCategories(analysisResult);
  const recurring = getRecurringStatus();
  const monthlyTrend = forecastResult?.historical?.map((h) => ({ period: h.period, expense: h.expense })) ?? [];
  const insight = buildFallbackInsight({
    totalExpense: analysisResult.totalExpense,
    topCategories,
    historicalTrend: forecastResult?.historical ?? [],
  });

  return {
    period: { from: analysisResult.from, to: analysisResult.to },
    isEmpty: false,
    totalSpending: {
      amount: analysisResult.totalExpense,
      display: `Rs ${Math.round(analysisResult.totalExpense).toLocaleString()}`,
    },
    totalIncome: analysisResult.totalIncome,
    topCategories,
    monthlyTrend,
    trendAvailable: monthlyTrend.length > 0,
    recurring,
    aiInsight: insight,
    aiInsightIsFallback: true, // always true today — no real AI insight field exists on either endpoint
  };
}

// ---------------------------------------------------------------------
// 5. Single entry point for the UI to call
// ---------------------------------------------------------------------
export async function getSpendingIntelligenceForUI(userId, from, to, opts = {}) {
  try {
    const analysisPromise = fetchBudgetAnalysis(userId, from, to, opts);
    // Forecast can legitimately fail (e.g. < 3 usable periods) without
    // breaking the category/total-spending display, so it's allowed to
    // resolve to null instead of rejecting the whole screen.
    const forecastPromise = fetchForecast(userId, from, to, {}, opts).catch(() => null);

    const [analysis, forecastResult] = await Promise.all([analysisPromise, forecastPromise]);
    return { ok: true, data: buildDisplayPayload(analysis, forecastResult) };
  } catch (err) {
    if (err instanceof SpendingIntelligenceAPIError) {
      return { ok: false, errorState: handleApiError(err) };
    }
    throw err;
  }
}

export { DEMO_USER_ID };
