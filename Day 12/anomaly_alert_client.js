/**
 * Anomaly Alerts — Mobile/Web Integration Layer
 * HisabDo AI, Day 12 — Task owner: Laiba
 *
 * Section 17 of the Day 12 spec assigns this exactly:
 *   "Laiba — Frontend / UI: Alerts UI, severity/reason/details,
 *    API integration, empty/no-anomaly states."
 *
 * Built against the contract defined in:
 *   Anomaly_Detection/HisabDo_AI_Day12_Anomaly_Detection_Specification.pdf  (§11)
 *   Anomaly_Detection/Day12_AnomalyDetection_Validation.cs                  (rules)
 *   Anomaly explanation/Day12_AI_Anomaly_Explanation_Service.cs             (§12 explanation layer)
 *
 * CONTRACT (spec §11):
 *   GET /api/ai/anomalies/{userId}?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD
 *
 *   {
 *     "userId": "verified-user-id",
 *     "analysisPeriod": { "start": "YYYY-MM-DD", "end": "YYYY-MM-DD" },
 *     "baselinePeriods": 3,
 *     "anomalies": [{
 *       "transactionId", "type", "severity", "amount", "category", "date",
 *       "baselineValue", "deviationPercent", "reasonCode", "isDuplicateCandidate"
 *     }],
 *     "periodSpikes": [{
 *       "category", "currentAmount", "historicalAverage", "growthPercent", "severity"
 *     }]
 *   }
 *
 * ── SPEC RULES THIS LAYER MUST HONOUR (not optional styling choices) ──
 *
 *   §2  "An anomaly is not proof of fraud."
 *       -> No fraud/suspicious/theft language anywhere in the UI copy.
 *   §7.1 "Flagged result should use 'unusual' language; it must not claim fraud."
 *   §7.4 "Return a duplicate candidate; never automatically delete or reverse."
 *       -> Duplicates render as review candidates with no delete/reverse action.
 *   §5  "If history is insufficient, return an insufficient-baseline state rather
 *        than inventing a normal pattern."
 *       -> isInsufficientBaseline is surfaced as its own UI state (see below).
 *   §7.2 / §9 "If historical average is zero, do not divide."
 *       -> deviation/growth values are displayed only when the API actually
 *          supplies them; this layer never computes a percentage itself.
 *   §12 "LLM must not invent amounts, dates, categories, causes or fraud claims."
 *       -> Every figure shown comes straight from the API payload.
 *
 * ── ISSUES FOUND WHILE BUILDING (raise with team lead / Jaffer / Aarti) ──
 *
 *   1. TYPE NAMING IS INCONSISTENT ACROSS THE TWO SPEC FILES.
 *      §11's example payload uses PascalCase with no spaces:  "UnusualAmount".
 *      Day12_AI_Anomaly_Explanation_Service.cs switches on spaced strings:
 *      "Unusual Amount", "Sudden Spending Spike", "Unusual Category",
 *      "Duplicate Transaction". If the backend emits one form and the
 *      explanation service expects the other, every `switch` falls through to
 *      its default branch and users get generic text.
 *      -> normalizeType() below accepts BOTH forms so the UI is correct either
 *         way, but the backend contract still needs to pick one.
 *
 *   2. SEVERITY CASING. §11 uses "High"; the explanation service switches on
 *      "High"/"Medium"/"Low" (capitalised). Handled case-insensitively here.
 *
 *   3. NO HUMAN-READABLE `reason` FIELD IN THE API. §11 returns `reasonCode`
 *      (e.g. AMOUNT_ABOVE_BASELINE) — a machine token, not display text. The
 *      task requires showing a "Reason". Two real sources exist:
 *        (a) the AI Explanation layer (§12, Aarti's service) — preferred, and
 *            this client uses it whenever the payload carries it; or
 *        (b) a static reasonCode -> sentence map, below.
 *      The map is deterministic, describes only what the rule itself checked,
 *      and invents no figures. Only these two are used — no fabricated text.
 *
 *   4. `periodSpikes` IS A SEPARATE ARRAY FROM `anomalies`. It is easy to miss
 *      and would silently drop Medium/High spending-spike alerts (§7.2) from
 *      the UI. This layer merges them into one alert list, tagged by origin.
 */

const API_BASE_URL = "https://api.hisabdo.local"; // replace with the real HisabDo backend host

export class AnomalyAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the API (spec §11)
// ---------------------------------------------------------------------
export async function fetchAnomalies(userId, startDate, endDate, { baseUrl = API_BASE_URL, authToken = null } = {}) {
  const url =
    `${baseUrl}/api/ai/anomalies/${encodeURIComponent(userId)}` +
    `?startDate=${encodeURIComponent(startDate)}&endDate=${encodeURIComponent(endDate)}`;

  const headers = {};
  // §14: "Controller must enforce authentication/authorization."
  if (authToken) headers["Authorization"] = `Bearer ${authToken}`;

  let response;
  try {
    response = await fetch(url, { headers });
  } catch (err) {
    throw new AnomalyAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.detail || body.message || body.error || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new AnomalyAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state. No fraud language, no raw errors shown (§2).
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 400) {
    return { state: "invalid_input", title: "Check the dates", message: "That user ID or date range isn't valid. Try a different period." };
  }
  if (error.statusCode === 401 || error.statusCode === 403) {
    // §3/§14: user isolation — never show another user's data on an auth failure.
    return { state: "unauthorized", title: "Can't load alerts", message: "You don't have access to this data. Try signing in again." };
  }
  if (error.statusCode === 404) {
    return { state: "no_data", title: "No records found", message: "We couldn't find transactions for this user and period." };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "We couldn't load your alerts right now. Please try again." };
}

// ---------------------------------------------------------------------
// 3. Type + severity normalisation (see issues #1 and #2 above)
// ---------------------------------------------------------------------
const TYPE_LABELS = {
  unusualamount: "Unusual Amount",
  suddenspendingspike: "Sudden Spending Spike",
  spendingspike: "Sudden Spending Spike",
  unusualcategory: "Unusual Category",
  duplicatetransaction: "Duplicate Transaction",
  newcategory: "New Category",
};

/** Accepts "UnusualAmount", "Unusual Amount", "UNUSUAL_AMOUNT" — all resolve to one label. */
export function normalizeType(rawType) {
  if (!rawType) return "Spending Anomaly";
  const key = String(rawType).toLowerCase().replace(/[\s_-]/g, "");
  return TYPE_LABELS[key] || String(rawType);
}

export function normalizeSeverity(rawSeverity) {
  const s = String(rawSeverity || "").toLowerCase();
  return ["high", "medium", "low"].includes(s) ? s : "unknown";
}

function severityRank(severity) {
  return { high: 0, medium: 1, low: 2, unknown: 3 }[severity] ?? 3;
}

// ---------------------------------------------------------------------
// 4. reasonCode -> display sentence (see issue #3 above).
//    Each sentence states only what its own rule checked. No figures,
//    no causes, no fraud language (§7.1, §12).
// ---------------------------------------------------------------------
const REASON_CODE_TEXT = {
  AMOUNT_ABOVE_BASELINE: "This amount is above the usual range for this category, based on past spending.",
  CATEGORY_ABOVE_BASELINE: "Spending in this category is above its usual level, based on past periods.",
  PERIOD_SPIKE: "Total spending in this category rose compared with the usual level for a comparable period.",
  DUPLICATE_CANDIDATE: "Another transaction with matching details was recorded on the same day.",
  NEW_CATEGORY: "This category has no past spending to compare against yet.",
  INSUFFICIENT_BASELINE: "There isn't enough spending history yet to compare this against.",
};

/**
 * Resolves the "Reason" line, in priority order:
 *   1. The AI Explanation layer's verified text, if the payload carries it (§12)
 *   2. A free-text reason/detectionReason field, if the backend supplies one
 *   3. The static reasonCode map above
 *   4. A neutral fallback that claims nothing specific
 * Never fabricates a reason beyond these.
 */
function resolveReason(anomaly) {
  const explanation = anomaly.explanation || anomaly.aiExplanation;
  if (explanation) {
    // Shape from Day12_AI_Anomaly_Explanation_Service.cs
    const parts = [explanation.whatHappened, explanation.whyFlagged].filter(Boolean);
    if (parts.length) return { text: parts.join(" "), source: "ai_explanation" };
  }
  if (anomaly.reason || anomaly.detectionReason) {
    return { text: anomaly.reason || anomaly.detectionReason, source: "backend" };
  }
  if (anomaly.reasonCode && REASON_CODE_TEXT[anomaly.reasonCode]) {
    return { text: REASON_CODE_TEXT[anomaly.reasonCode], source: "reason_code" };
  }
  return { text: "This transaction differs from the usual spending pattern.", source: "fallback" };
}

// ---------------------------------------------------------------------
// 5. Formatting helpers
// ---------------------------------------------------------------------
function formatAmount(value) {
  if (value == null || isNaN(value)) return null;
  return `Rs ${Math.round(Number(value)).toLocaleString()}`;
}

function formatDate(value) {
  if (!value) return "Date not recorded";
  const d = new Date(value);
  if (isNaN(d.getTime())) return String(value);
  return d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
}

/** §9: "Calculate percentages only when the denominator is greater than zero."
 *  This layer never computes one — it only displays what the API supplied. */
function formatPercent(value) {
  if (value == null || isNaN(value)) return null;
  const n = Number(value);
  return `${n > 0 ? "+" : ""}${n.toFixed(2)}%`;
}

// ---------------------------------------------------------------------
// 6. Build the display payload
// ---------------------------------------------------------------------
export function buildDisplayPayload(apiResponse) {
  const rawAnomalies = apiResponse.anomalies || [];
  const rawSpikes = apiResponse.periodSpikes || [];
  const baselinePeriods = apiResponse.baselinePeriods;

  // §5: fewer than 3 comparable periods = insufficient baseline, surfaced as its
  // own state so the UI never presents thin history as a confident "all clear".
  const isInsufficientBaseline =
    apiResponse.insufficientBaseline === true ||
    (typeof baselinePeriods === "number" && baselinePeriods < 3);

  const anomalyItems = rawAnomalies.map((a) => {
    const severity = normalizeSeverity(a.severity);
    const reason = resolveReason(a);
    const isDuplicate =
      a.isDuplicateCandidate === true || normalizeType(a.type) === "Duplicate Transaction";

    return {
      origin: "anomaly",
      transactionId: a.transactionId ?? null,
      type: normalizeType(a.type),
      rawType: a.type,
      severity,
      amount: a.amount ?? null,
      amountDisplay: formatAmount(a.amount),
      category: a.category || "Uncategorized", // §23.6: missing category -> Uncategorized
      date: a.date ?? null,
      dateDisplay: formatDate(a.date),
      baselineValue: a.baselineValue ?? null,
      baselineDisplay: formatAmount(a.baselineValue),
      deviationPercent: a.deviationPercent ?? null,
      deviationDisplay: formatPercent(a.deviationPercent),
      reasonCode: a.reasonCode ?? null,
      reason: reason.text,
      reasonSource: reason.source,
      isDuplicateCandidate: isDuplicate,
      // §7.4 — a duplicate is a review candidate, never an auto-action.
      suggestedAction: isDuplicate
        ? "Review both transactions and confirm whether both should stay."
        : "Review this transaction and confirm whether the spending was expected.",
    };
  });

  // Issue #4: periodSpikes (§7.2) merged in so spikes aren't silently dropped.
  const spikeItems = rawSpikes.map((s) => {
    const severity = normalizeSeverity(s.severity);
    return {
      origin: "periodSpike",
      transactionId: null,
      type: "Sudden Spending Spike",
      rawType: "PeriodSpike",
      severity,
      amount: s.currentAmount ?? null,
      amountDisplay: formatAmount(s.currentAmount),
      category: s.category || "Uncategorized",
      date: null, // spec's periodSpikes entries carry no date — it's a period total
      dateDisplay: apiResponse.analysisPeriod
        ? `${apiResponse.analysisPeriod.start} – ${apiResponse.analysisPeriod.end}`
        : "This period",
      baselineValue: s.historicalAverage ?? null,
      baselineDisplay: formatAmount(s.historicalAverage),
      deviationPercent: s.growthPercent ?? null,
      deviationDisplay: formatPercent(s.growthPercent),
      reasonCode: "PERIOD_SPIKE",
      reason: REASON_CODE_TEXT.PERIOD_SPIKE,
      reasonSource: "reason_code",
      isDuplicateCandidate: false,
      suggestedAction: "Review spending in this category for the period.",
    };
  });

  const items = [...anomalyItems, ...spikeItems].sort(
    (a, b) => severityRank(a.severity) - severityRank(b.severity) || new Date(b.date || 0) - new Date(a.date || 0)
  );

  const counts = { high: 0, medium: 0, low: 0 };
  for (const item of items) {
    if (counts[item.severity] !== undefined) counts[item.severity] += 1;
  }

  return {
    userId: apiResponse.userId ?? null,
    analysisPeriod: apiResponse.analysisPeriod ?? null,
    baselinePeriods: baselinePeriods ?? null,
    isInsufficientBaseline,
    alerts: items,
    counts,
    total: items.length,
    isEmpty: items.length === 0,
  };
}

// ---------------------------------------------------------------------
// 7. Single entry point for the UI
// ---------------------------------------------------------------------
export async function getAnomalyAlertsForUI(userId, startDate, endDate, options = {}) {
  try {
    const raw = await fetchAnomalies(userId, startDate, endDate, options);
    return { ok: true, data: buildDisplayPayload(raw) };
  } catch (err) {
    if (err instanceof AnomalyAPIError) {
      return { ok: false, errorState: handleApiError(err) };
    }
    throw err;
  }
}
