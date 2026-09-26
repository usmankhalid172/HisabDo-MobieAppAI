/**
 * AI Recommendations — Web/Mobile API Integration
 * HisabDo AI, Day 16 — Task owner: Laiba (integrate the API, display
 * recommendations in mobile UI with priority/category/explanation/
 * actionable suggestion/limitations, handle loading/error/empty states,
 * test multiple recommendations across different users, prepare the UI
 * for Day 17/18 integration)
 *
 * ---------------------------------------------------------------------
 * BACKEND STATUS WHEN THIS WAS WRITTEN:
 * ---------------------------------------------------------------------
 * `TahaRecommendationEngine` (RecommendationEngine.cs) is a complete,
 * deterministic engine — used here COMPLETELY UNMODIFIED — but it only
 * takes an already-assembled `VerifiedRecommendationInput`; nothing in
 * the repo builds that input from the real Day 10-14 services (that's
 * Jaffer's task per the spec's team table), and there was no HTTP
 * controller either. `RecommendationController.cs` (included alongside
 * this file) adds the missing HTTP layer plus a demo data aggregator
 * standing in for Jaffer's real integration work. Built and run with
 * `dotnet run`, tested with curl for three distinct users — every shape
 * below is a REAL, executed response (see resp_*.json), not a guess.
 *
 * Real endpoint:
 *   GET /api/ai/recommendations/{userId}
 *
 * Real response shape (verified, matches the spec's own output structure
 * exactly, Section 7):
 *   {
 *     userId,
 *     recommendations: [{
 *       type, priority, severity, category, reason, action,
 *       verifiedAmount, verifiedPercentage
 *     }],
 *     limitations: string[]
 *   }
 */

const API_BASE_URL = "http://localhost:5090";
const AUTH_HEADER = ""; // demo handler reads X-Demo-User instead; wire real auth later

export class RecommendationAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the real endpoint
// ---------------------------------------------------------------------
export async function fetchRecommendations(userId, { baseUrl = API_BASE_URL, authHeader = AUTH_HEADER, demoUserHeader } = {}) {
  const url = `${baseUrl}/api/ai/recommendations/${encodeURIComponent(userId)}`;
  const headers = {};
  if (authHeader) headers.Authorization = authHeader;
  if (demoUserHeader) headers["X-Demo-User"] = demoUserHeader; // demo backend only

  let response;
  try {
    response = await fetch(url, { headers });
  } catch (err) {
    throw new RecommendationAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.error || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new RecommendationAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state mapping
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 401) {
    return { state: "unauthorized", title: "Can't load recommendations", message: "This request wasn't authorized to reach the server." };
  }
  if (error.statusCode === 404) {
    return { state: "not_found", title: "No data on file", message: error.detail || "No verified data is available for this user yet." };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "Please try again in a moment." };
}

// ---------------------------------------------------------------------
// 3. Build the display structure the mobile/web recommendation cards need
// ---------------------------------------------------------------------
const PRIORITY_ORDER = { High: 0, Medium: 1, Low: 2 };

const TYPE_LABELS = {
  SpendingReduction: "Spending Reduction",
  BudgetAdjustment: "Budget Adjustment",
  SavingImprovement: "Saving Improvement",
  CashFlowRisk: "Cash-Flow Risk",
  UnusualSpendingReview: "Unusual Spending Review",
  RecurringExpenseReview: "Recurring Expense Review",
  FinancialHealthImprovement: "Financial Health Improvement",
  CategorySpendingControl: "Category Spending Control",
};

function formatVerifiedFigure(rec) {
  const parts = [];
  if (rec.verifiedAmount != null) parts.push(`Rs ${Math.round(Math.abs(rec.verifiedAmount)).toLocaleString()}${rec.verifiedAmount < 0 ? " (negative)" : ""}`);
  if (rec.verifiedPercentage != null) parts.push(`${rec.verifiedPercentage}%`);
  return parts.length ? parts.join(" · ") : null;
}

/** One recommendation, shaped for a mobile/web card — this shape is the
 *  stable contract to build Day 17/18's UI on top of. */
function toRecommendationCard(rec) {
  return {
    type: rec.type,
    typeLabel: TYPE_LABELS[rec.type] || rec.type,
    priority: rec.priority,
    severity: rec.severity,
    category: rec.category,
    reason: rec.reason,
    action: rec.action,
    verifiedFigure: formatVerifiedFigure(rec),
  };
}

export function buildRecommendationsDisplay(apiResponse) {
  const recommendations = (apiResponse.recommendations || []).map(toRecommendationCard);
  return {
    userId: apiResponse.userId,
    hasRecommendations: recommendations.length > 0,
    recommendations,
    highCount: recommendations.filter((r) => r.priority === "High").length,
    mediumCount: recommendations.filter((r) => r.priority === "Medium").length,
    lowCount: recommendations.filter((r) => r.priority === "Low").length,
    limitations: apiResponse.limitations || [],
    hasLimitations: (apiResponse.limitations || []).length > 0,
    // Distinguishes "genuinely nothing to flag" from "we don't have
    // enough data yet" — both render an empty recommendation list, but
    // they mean very different things to a user (spec Section 8).
    isInsufficientData: recommendations.length === 0 && (apiResponse.limitations || []).length > 0,
  };
}

// ---------------------------------------------------------------------
// 4. Single entry point for the UI to call
// ---------------------------------------------------------------------
export async function getRecommendationsForUI(userId, opts = {}) {
  try {
    const response = await fetchRecommendations(userId, opts);
    return { ok: true, data: buildRecommendationsDisplay(response) };
  } catch (err) {
    if (err instanceof RecommendationAPIError) return { ok: false, errorState: handleApiError(err) };
    throw err;
  }
}
