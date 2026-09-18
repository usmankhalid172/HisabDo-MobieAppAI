/**
 * Spending Pattern Intelligence — Mobile/Web Integration Layer (v2)
 * HisabDo AI, Day 11 — Task owner: Laiba (API + Mobile/Web Integration)
 *
 * REBUILT to point at the REAL backend that actually exists in the repo:
 *   src/financial_assistant/spending_pattern_intelligence.py
 *   src/financial_assistant/spending_pattern_router.py
 *   (branch: feature/spending-pattern-intelligence — not yet merged to main)
 *
 * This is NOT the same contract as the Day 11 spec PDF / C# service in
 * Ai-task-11/. That C# file is a design document; it was never implemented.
 * The real, running endpoint is:
 *
 *   GET /api/v1/spending-pattern-intelligence/{user_id}?start_date=YYYY-MM-DD&end_date=YYYY-MM-DD
 *   Header required: X-Internal-Token: <INTERNAL_SERVICE_TOKEN>
 *
 * Real response shape:
 *   {
 *     user_id, analysis_period: {start_date, end_date},
 *     income_total, total_expenses,
 *     category_breakdown: { "Groceries": 5000, ... },
 *     category_percentages: { "Groceries": 45.2, ... },
 *     period_summary: { transaction_count, income_transaction_count,
 *                        expense_transaction_count, has_transactions, months },
 *     monthly_comparison: [ { month: "2026-08", income, expenses, transaction_count }, ... ],
 *     verified: true, source: "verified_hisabdo_transaction_ledger"
 *   }
 *
 * WHAT THE REAL BACKEND DOES vs DOES NOT GIVE YOU (verified by reading every
 * file on that branch — nothing below is guessed):
 *
 *   ✅ Total spending           -> total_expenses (real)
 *   ✅ Category amounts + %     -> category_breakdown / category_percentages (real)
 *   ✅ Monthly spending trend   -> monthly_comparison (real — and it's actually BETTER
 *                                  than the spec: one call with a wide date range
 *                                  returns every month in range, no N-calls workaround needed)
 *   ⚠️ Top categories (ranked)  -> NOT pre-ranked by the API; this layer sorts
 *                                  category_breakdown client-side. Still real numbers,
 *                                  just re-ordered, not invented.
 *   ⚠️ Largest increasing/      -> NOT computed by the API at all. This layer calls the
 *      decreasing category         endpoint TWICE (current period + the immediately
 *                                  preceding period of equal length) and diffs the two
 *                                  real category_breakdown objects client-side.
 *   ❌ Recurring spending       -> CANNOT be derived from this API. The endpoint only
 *                                  returns category TOTALS, never individual transactions
 *                                  (no description, no per-transaction date/amount to spot
 *                                  a repeating pattern). This layer does NOT fabricate this —
 *                                  it reports it as unavailable and states exactly what the
 *                                  backend would need to add (a transaction-level endpoint,
 *                                  or the raw list) to make this real. See getRecurringStatus().
 *   ❌ AI Spending Insight      -> No insight field or LLM step exists anywhere in the real
 *                                  backend. Same rule-based-fallback approach as before,
 *                                  built ONLY from the verified numbers above — never
 *                                  invents a figure — and clearly marked as a fallback.
 */

const API_BASE_URL = "http://localhost:8000"; // replace with real HisabDo backend host
const INTERNAL_TOKEN = ""; // set from a secure config/env value on the real client — never hardcode in shipped code

export class SpendingIntelligenceAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the real API for one period
// ---------------------------------------------------------------------
export async function fetchSpendingPatternIntelligence(
  userId,
  startDate,
  endDate,
  { baseUrl = API_BASE_URL, token = INTERNAL_TOKEN } = {}
) {
  const url =
    `${baseUrl}/api/v1/spending-pattern-intelligence/${encodeURIComponent(userId)}` +
    `?start_date=${startDate}&end_date=${endDate}`;

  let response;
  try {
    response = await fetch(url, { headers: { "X-Internal-Token": token } });
  } catch (err) {
    throw new SpendingIntelligenceAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.detail || body.message || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new SpendingIntelligenceAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state mapping (matches the real router's actual error codes:
//    ValueError -> 400, FileNotFoundError -> 404, bad/missing token -> 401)
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 400) {
    return {
      state: "invalid_input",
      title: "Check the request",
      message: "The user ID or date range isn't valid. (Dates must be YYYY-MM-DD or full ISO 8601.)",
    };
  }
  if (error.statusCode === 401) {
    return {
      state: "unauthorized",
      title: "Can't load this data",
      message: "This request wasn't authorized to reach the server.",
    };
  }
  if (error.statusCode === 404) {
    return {
      state: "no_data",
      title: "No spending data found",
      message: "We couldn't find the transaction dataset or this user's records.",
    };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "Please try again in a moment." };
}

// ---------------------------------------------------------------------
// 3. Helpers
// ---------------------------------------------------------------------
function toDateStr(d) {
  return d.toISOString().slice(0, 10);
}

/** Returns the immediately preceding period, same length in days as [start, end]. */
function previousPeriodOf(startDate, endDate) {
  const start = new Date(startDate);
  const end = new Date(endDate);
  const lengthMs = end.getTime() - start.getTime();
  const prevEnd = new Date(start.getTime() - 24 * 60 * 60 * 1000); // day before current start
  const prevStart = new Date(prevEnd.getTime() - lengthMs);
  return { from: toDateStr(prevStart), to: toDateStr(prevEnd) };
}

/** Ranks category_breakdown (real amounts) + category_percentages (real %) together, descending. */
function rankTopCategories(categoryBreakdown = {}, categoryPercentages = {}, limit = 5) {
  return Object.entries(categoryBreakdown)
    .map(([category, amount]) => ({
      category,
      amount,
      percentage: categoryPercentages[category] ?? null,
    }))
    .sort((a, b) => b.amount - a.amount)
    .slice(0, limit);
}

/** Diffs two real category_breakdown objects to find the biggest movers, plus new/stopped categories. */
function computeCategoryMovers(currentBreakdown = {}, previousBreakdown = {}) {
  const allCategories = new Set([...Object.keys(currentBreakdown), ...Object.keys(previousBreakdown)]);
  const deltas = [];
  const newCategories = [];
  const stoppedCategories = [];

  for (const category of allCategories) {
    const current = currentBreakdown[category] ?? 0;
    const previous = previousBreakdown[category] ?? 0;
    const changeAmount = current - previous;

    if (previous === 0 && current > 0) newCategories.push(category);
    if (previous > 0 && current === 0) stoppedCategories.push(category);

    deltas.push({
      category,
      changeAmount,
      changePercent: previous > 0 ? Math.round((changeAmount / previous) * 10000) / 100 : null,
    });
  }

  deltas.sort((a, b) => b.changeAmount - a.changeAmount);
  const largestIncreasingCategory = deltas.length && deltas[0].changeAmount > 0 ? deltas[0] : null;
  const largestDecreasingCategory =
    deltas.length && deltas[deltas.length - 1].changeAmount < 0 ? deltas[deltas.length - 1] : null;

  return { largestIncreasingCategory, largestDecreasingCategory, newCategories, stoppedCategories };
}

/**
 * Recurring spending CANNOT be computed from this API — it only returns
 * category totals, never individual transactions. Returns an honest status
 * object instead of fabricating data. See file header for what the backend
 * would need to add to make this real.
 */
function getRecurringStatus() {
  return {
    available: false,
    reason:
      "The current API only returns category totals, not individual transactions, " +
      "so a repeating expense (same amount/category recurring monthly) can't be detected. " +
      "Needs either a transaction-level endpoint or the raw transaction list included in the response.",
  };
}

/** Rule-based fallback insight — built only from verified numbers computed above, never invented. */
function buildFallbackInsight({ totalExpenses, growthPercent, topCategories, movers }) {
  const lines = [];

  if (growthPercent === null || growthPercent === undefined) {
    lines.push("This is the first period with comparable data, so there's no trend yet.");
  } else if (growthPercent > 10) {
    lines.push(`Spending increased ${growthPercent}% compared with the previous period.`);
  } else if (growthPercent < -10) {
    lines.push(`Spending decreased ${Math.abs(growthPercent)}% compared with the previous period.`);
  } else {
    lines.push("Spending is roughly stable compared with the previous period.");
  }

  if (topCategories.length) {
    const top = topCategories[0];
    lines.push(
      top.percentage != null
        ? `${top.category} is the largest category at ${top.percentage}% of total spending.`
        : `${top.category} is the largest category.`
    );
  }
  if (movers.largestIncreasingCategory) {
    lines.push(
      `${movers.largestIncreasingCategory.category} increased the most, by Rs ${Math.round(
        movers.largestIncreasingCategory.changeAmount
      ).toLocaleString()}.`
    );
  }
  if (movers.largestDecreasingCategory) {
    lines.push(
      `${movers.largestDecreasingCategory.category} decreased the most, by Rs ${Math.round(
        Math.abs(movers.largestDecreasingCategory.changeAmount)
      ).toLocaleString()}.`
    );
  }
  if (movers.newCategories.length) {
    lines.push(`New spending category this period: ${movers.newCategories.join(", ")}.`);
  }
  return lines.join(" ");
}

// ---------------------------------------------------------------------
// 4. Build the mobile/web display structure (Section 13) from REAL data
// ---------------------------------------------------------------------
export function buildDisplayPayload(currentResponse, previousResponse) {
  const totalExpenses = currentResponse.total_expenses ?? 0;
  const previousTotalExpenses = previousResponse?.total_expenses ?? 0;
  const hasSpending = (currentResponse.period_summary?.has_transactions ?? totalExpenses > 0) && totalExpenses > 0;

  const growthPercent =
    previousResponse && previousTotalExpenses > 0
      ? Math.round(((totalExpenses - previousTotalExpenses) / previousTotalExpenses) * 10000) / 100
      : previousResponse && previousTotalExpenses === 0 && totalExpenses > 0
      ? null // avoid divide-by-zero; genuinely undefined growth, not zero
      : null;

  const topCategories = rankTopCategories(currentResponse.category_breakdown, currentResponse.category_percentages);
  const movers = previousResponse
    ? computeCategoryMovers(currentResponse.category_breakdown, previousResponse.category_breakdown)
    : { largestIncreasingCategory: null, largestDecreasingCategory: null, newCategories: [], stoppedCategories: [] };
  const recurring = getRecurringStatus();

  const insight = buildFallbackInsight({ totalExpenses, growthPercent, topCategories, movers });

  return {
    period: currentResponse.analysis_period,
    previousPeriod: previousResponse?.analysis_period ?? null,
    totalSpending: {
      amount: totalExpenses,
      display: hasSpending ? `Rs ${Math.round(totalExpenses).toLocaleString()}` : "No spending recorded",
    },
    periodComparison: {
      current: totalExpenses,
      previous: previousTotalExpenses,
      growthPercent,
      trendLabel: growthPercent == null ? "No prior data" : growthPercent > 0 ? "Up" : growthPercent < 0 ? "Down" : "Unchanged",
    },
    topCategories,
    largestIncreasingCategory: movers.largestIncreasingCategory,
    largestDecreasingCategory: movers.largestDecreasingCategory,
    newCategories: movers.newCategories,
    stoppedCategories: movers.stoppedCategories,
    monthlyTrend: currentResponse.monthly_comparison ?? [],
    recurring,
    aiInsight: insight,
    aiInsightIsFallback: true, // always true today — no real aiInsight field exists anywhere
    isEmpty: !hasSpending,
  };
}

// ---------------------------------------------------------------------
// 5. Single entry point for the UI to call
// ---------------------------------------------------------------------
export async function getSpendingIntelligenceForUI(
  userId,
  startDate,
  endDate,
  { baseUrl = API_BASE_URL, token = INTERNAL_TOKEN } = {}
) {
  const prev = previousPeriodOf(startDate, endDate);

  try {
    // Two real calls: current period + immediately preceding period of equal length,
    // so movers/growth are computed from real numbers, not estimated.
    const [current, previous] = await Promise.all([
      fetchSpendingPatternIntelligence(userId, startDate, endDate, { baseUrl, token }),
      fetchSpendingPatternIntelligence(userId, prev.from, prev.to, { baseUrl, token }).catch(() => null),
      // ^ previous period is allowed to fail/return nothing (e.g. brand-new user)
      //   without breaking the current period's display.
    ]);
    return { ok: true, data: buildDisplayPayload(current, previous) };
  } catch (err) {
    if (err instanceof SpendingIntelligenceAPIError) {
      return { ok: false, errorState: handleApiError(err) };
    }
    throw err;
  }
}
