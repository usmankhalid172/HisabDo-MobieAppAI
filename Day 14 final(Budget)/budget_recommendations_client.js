/**
 * Budget Recommendations — Web/Mobile API Integration
 * HisabDo AI, Day 14 — Task owner: Laiba (API/UI integration per spec
 * Section 14: "Laiba: API/UI integration")
 *
 * ---------------------------------------------------------------------
 * BACKEND STATUS WHEN THIS WAS WRITTEN:
 * ---------------------------------------------------------------------
 * Same pattern as Days 12–13: the repo had BudgetRecommendationService.cs
 * (Taha's real MVP methodology) and TahaAIBudgetExplanationService.cs
 * (Taha's real, deterministic explanation layer — not an LLM call) as
 * plain classes with no HTTP controller. `BudgetRecommendationController.cs`
 * (included alongside this file) wires them together. Built and run with
 * `dotnet run`, tested with curl — every shape below is a REAL, executed
 * response (see real_response_*.json), not a guess.
 *
 * Real endpoint:
 *   GET /api/ai/budget-recommendations/{userId}?months=3
 *   GET /api/ai/budget-recommendations/{userId}?start=2026-06-01&end=2026-08-31
 *
 * Real response shape (verified):
 *   {
 *     userId, analysisPeriodStart, analysisPeriodEnd,
 *     categories: [{
 *       category, historicalAverage, existingBudget, recommendedBudget,
 *       budgetVariance, budgetUtilizationPercent, recommendationReason,
 *       dataSufficiency, limitations, isOverspending,
 *       explanation: { summary, recommendedBudget, whyRecommendation,
 *                       overspending, savingsOpportunity, action, limitations }
 *     }],
 *     overspendingCategories: string[],
 *     actionableSuggestions: string[]
 *   }
 *
 * CONFIRMED BACKEND CHARACTERISTIC (found by running it, same pattern as
 * Day 13's Gap #2): `HistoricalAverage` divides a category's total spend
 * by the FULL requested month count, not by how many of those months
 * actually had transactions in that category. Confirmed live: "Shopping"
 * had real spending in only 2 of 3 requested months, so its average (Rs
 * 3,000) is diluted versus what a 2-month-only average would show
 * (Rs 4,500). This client does not correct the number (Golden Rule:
 * never alter verified values) — it displays exactly what the API
 * returns.
 */

const API_BASE_URL = "http://localhost:5070";
const AUTH_HEADER = ""; // demo handler reads X-Demo-User instead; wire real auth later

export class BudgetRecAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the real endpoint
// ---------------------------------------------------------------------
export async function fetchBudgetRecommendations(userId, { months, start, end, baseUrl = API_BASE_URL, authHeader = AUTH_HEADER, demoUserHeader } = {}) {
  const params = start && end ? `start=${start}&end=${end}` : `months=${months ?? 3}`;
  const url = `${baseUrl}/api/ai/budget-recommendations/${encodeURIComponent(userId)}?${params}`;
  const headers = {};
  if (authHeader) headers.Authorization = authHeader;
  if (demoUserHeader) headers["X-Demo-User"] = demoUserHeader; // demo backend only

  let response;
  try {
    response = await fetch(url, { headers });
  } catch (err) {
    throw new BudgetRecAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.error || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new BudgetRecAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state mapping
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 400) {
    return { state: "invalid_input", title: "Check the request", message: error.detail || "The request wasn't valid." };
  }
  if (error.statusCode === 401) {
    return { state: "unauthorized", title: "Can't load budget recommendations", message: "This request wasn't authorized to reach the server." };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "Please try again in a moment." };
}

// ---------------------------------------------------------------------
// 3. Build the display structure the Budget Recommendations screen needs
// ---------------------------------------------------------------------
function formatAmount(amount) {
  return amount == null ? "Not available" : `Rs ${Math.round(amount).toLocaleString()}`;
}

/** One category card, ready for the UI. */
function toCategoryCard(c) {
  return {
    category: c.category,
    historicalAverage: formatAmount(c.historicalAverage),
    existingBudget: c.existingBudget != null ? formatAmount(c.existingBudget) : "No existing budget",
    recommendedBudget: formatAmount(c.recommendedBudget),
    variance: c.budgetVariance != null ? formatAmount(Math.abs(c.budgetVariance)) : null,
    varianceDirection: c.budgetVariance == null ? null : c.budgetVariance >= 0 ? "headroom" : "over",
    utilization: c.budgetUtilizationPercent != null ? `${c.budgetUtilizationPercent}%` : "Not available",
    isOverspending: c.isOverspending,
    isLimitedData: c.dataSufficiency === "Limited",
    reason: c.recommendationReason,
    aiSummary: c.explanation.summary,
    aiWhy: c.explanation.whyRecommendation,
    aiOverspending: c.explanation.overspending,
    aiSavings: c.explanation.savingsOpportunity,
    aiAction: c.explanation.action,
    aiLimitations: c.explanation.limitations,
  };
}

export function buildBudgetRecDisplay(apiResponse) {
  const categories = (apiResponse.categories || []).map(toCategoryCard);
  return {
    period: { start: apiResponse.analysisPeriodStart, end: apiResponse.analysisPeriodEnd },
    hasRecommendations: categories.length > 0,
    categories,
    overspendingCategories: apiResponse.overspendingCategories || [],
    actionableSuggestions: apiResponse.actionableSuggestions || [],
    hasOverspending: (apiResponse.overspendingCategories || []).length > 0,
  };
}

// ---------------------------------------------------------------------
// 4. Single entry point for the UI to call
// ---------------------------------------------------------------------
export async function getBudgetRecommendationsForUI(userId, opts = {}) {
  try {
    const response = await fetchBudgetRecommendations(userId, opts);
    return { ok: true, data: buildBudgetRecDisplay(response) };
  } catch (err) {
    if (err instanceof BudgetRecAPIError) return { ok: false, errorState: handleApiError(err) };
    throw err;
  }
}
