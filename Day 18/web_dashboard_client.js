/**
 * Web AI Dashboard — API Integration
 * HisabDo AI, Day 18 — Task owner: Laiba (create/prepare the web AI
 * dashboard, integrate the Financial Health API and every card, display
 * priority/severity, recommendation detail, Ask HisabDo AI interface,
 * loading/empty/error states, retry, responsive layout, end-to-end
 * testing)
 *
 * ---------------------------------------------------------------------
 * PATTERN THIS FOLLOWS:
 * ---------------------------------------------------------------------
 * This mirrors the teammate's real Day 17 mobile dashboard implementation
 * (Mobile AI Dashboard/Day17_HisabDo_AI_Mobile_Dashboard_Implementation.cs)
 * as closely as possible, per the task's own request: same DTOs, same
 * Loading/Success/Empty/Error state machine, same one-call dashboard
 * endpoint, same Ask HisabDo flow — renamed Mobile -> Web and re-routed
 * /api/mobile/ai -> /api/web/ai. `WebAiDashboardCore.cs` (included
 * alongside this file) is that adaptation.
 *
 * Real endpoints:
 *   GET  /api/web/ai/dashboard   (one authenticated call loads every card)
 *   POST /api/web/ai/ask         body: { "question": "..." }
 *
 * Real response shape (verified, matches DashboardResponse exactly):
 *   {
 *     financialHealth: {...} | null,
 *     spendingPatterns: [...], anomalies: [...],
 *     cashFlowForecast: {...} | null,
 *     budgetRecommendations: [...], recommendations: [...]
 *   }
 *
 * The authenticated UserId is NEVER supplied by this client — per spec
 * Section 10, it comes only from the backend auth context. This client
 * only sends an X-Demo-User header because the reference backend's demo
 * auth handler needs SOME way to pick a demo identity for testing; a real
 * login/session replaces this entirely, and this client never sends any
 * userId in a URL or request body.
 */

const API_BASE_URL = "http://localhost:5100";
const AUTH_HEADER = ""; // wire real auth (e.g. a session cookie or bearer token) here

export class DashboardAPIError extends Error {
  constructor(statusCode, code, message, retryable) {
    super(`[${statusCode}] ${code}: ${message}`);
    this.statusCode = statusCode;
    this.code = code;
    this.detail = message;
    this.retryable = retryable;
  }
}

// ---------------------------------------------------------------------
// 1. Call the real endpoints
// ---------------------------------------------------------------------
async function callApi(path, { method = "GET", body, baseUrl = API_BASE_URL, authHeader = AUTH_HEADER, demoUserHeader } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (authHeader) headers.Authorization = authHeader;
  if (demoUserHeader) headers["X-Demo-User"] = demoUserHeader; // demo backend only

  let response;
  try {
    response = await fetch(`${baseUrl}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  } catch (err) {
    throw new DashboardAPIError(0, "NETWORK_ERROR", err.message, true);
  }

  if (response.ok) return response.json();

  let apiError;
  try {
    apiError = await response.json(); // { code, message, retryable }
  } catch {
    apiError = { code: "UNKNOWN_ERROR", message: "An unexpected error occurred.", retryable: true };
  }
  throw new DashboardAPIError(response.status, apiError.code, apiError.message, apiError.retryable ?? true);
}

export function fetchDashboard(opts) {
  return callApi("/api/web/ai/dashboard", opts);
}

export function askHisabDo(question, opts) {
  return callApi("/api/web/ai/ask", { ...opts, method: "POST", body: { question } });
}

// ---------------------------------------------------------------------
// 2. Loading / Success / Empty / Error state machine — same shape as the
//    mobile reference's MobileScreenState<T> / AiDashboardViewModel.
// ---------------------------------------------------------------------
export const LoadState = { Idle: "Idle", Loading: "Loading", Success: "Success", Empty: "Empty", Error: "Error" };

function isDashboardEmpty(data) {
  return !data || (!data.financialHealth && (data.spendingPatterns || []).length === 0 &&
    (data.anomalies || []).length === 0 && !data.cashFlowForecast &&
    (data.budgetRecommendations || []).length === 0 && (data.recommendations || []).length === 0);
}

export class DashboardViewModel {
  constructor(opts = {}) {
    this.opts = opts;
    this.state = { state: LoadState.Idle, data: null, message: null, canRetry: false };
    this.askState = { state: LoadState.Idle, data: null, message: null, canRetry: false };
  }

  async load() {
    this.state = { state: LoadState.Loading, data: null, message: null, canRetry: false };
    try {
      const data = await fetchDashboard(this.opts);
      this.state = isDashboardEmpty(data)
        ? { state: LoadState.Empty, data: null, message: "No AI financial insights are available yet.", canRetry: true }
        : { state: LoadState.Success, data, message: null, canRetry: false };
    } catch (err) {
      this.state = { state: LoadState.Error, data: null, message: safeMessage(err), canRetry: err.retryable !== false };
    }
    return this.state;
  }

  retry() {
    return this.load();
  }

  async ask(question) {
    if (!question || !question.trim()) {
      this.askState = { state: LoadState.Error, data: null, message: "Please enter a question.", canRetry: false };
      return this.askState;
    }
    this.askState = { state: LoadState.Loading, data: null, message: null, canRetry: false };
    try {
      const answer = await askHisabDo(question.trim(), this.opts);
      this.askState = !answer || !answer.answer
        ? { state: LoadState.Empty, data: answer, message: answer?.limitation || "No answer is available for this question.", canRetry: true }
        : { state: LoadState.Success, data: answer, message: null, canRetry: false };
    } catch (err) {
      this.askState = { state: LoadState.Error, data: null, message: safeMessage(err), canRetry: err.retryable !== false };
    }
    return this.askState;
  }
}

function safeMessage(err) {
  // Never surface err.stack or raw exception text — only the backend's
  // own pre-written, safe message (per spec Section 14).
  if (err instanceof DashboardAPIError) return err.detail || "Something went wrong. Please try again.";
  return "Something went wrong. Please try again.";
}

// ---------------------------------------------------------------------
// 3. Display helpers — never invent a value the backend didn't verify.
// ---------------------------------------------------------------------
export function recommendationCard(rec) {
  return {
    id: rec.recommendationId,
    type: rec.type,
    priority: rec.priority,
    severity: rec.severity, // null means "hide this row", never "Unknown"
    reason: rec.reason,
    action: rec.action,
    verifiedAmount: rec.verifiedAmount,
    verifiedPercentage: rec.verifiedPercentage,
    expectedBenefit: rec.verifiedExpectedBenefit, // null means "hide this row"
    limitation: rec.limitation,
  };
}

export function findRecommendationDetail(dashboardData, recommendationId) {
  const rec = (dashboardData.recommendations || []).find((r) => r.recommendationId === recommendationId);
  return rec ? recommendationCard(rec) : null;
}
