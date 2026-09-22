/**
 * Ask HisabDo — Web/Mobile API Integration
 * HisabDo AI, Day 15 — Task owner: Laiba (integrate the API, build the
 * question input flow, display the AI answer + relevant verified values,
 * handle loading/response/missing-data/unsupported states, test the
 * complete question-to-answer flow)
 *
 * ---------------------------------------------------------------------
 * BACKEND STATUS WHEN THIS WAS WRITTEN:
 * ---------------------------------------------------------------------
 * Same pattern as Days 12-14: the repo had two real, independent files —
 * Omesha's classifier (QueryClassificationService.cs) and Taha's
 * orchestration service (AskHisabDoAIService.cs) — that had never been
 * connected to each other, let alone to an HTTP endpoint. Taha's own
 * IAskHisabDoDataService interface had NO implementation anywhere. This
 * client integrates against `AskHisabDoController.cs` (included alongside
 * this file), which wires the classifier through an adapter, adds a demo
 * data-service implementation that computes real answers over seeded data
 * using the same MVP formulas as the Day 10-14 specs, and exposes it all
 * at one endpoint. Built and run with `dotnet run`, tested with curl for
 * every category in the spec's own sample-question table — every shape
 * below is a REAL, executed response (see resp_*.json), not a guess.
 *
 * Real endpoint:
 *   POST /api/ai/ask-hisabdo/{userId}   body: { "question": "..." }
 *
 * Real response shape (verified):
 *   {
 *     category, confidence, classificationReason, requiredData: string[],
 *     answer, verifiedValues: [{ label, value }],
 *     limitations: string[], isVerified, isUnsupported, needsClarification,
 *     clarificationPrompts: string[]
 *   }
 *
 * TWO CONFIRMED CLASSIFIER ISSUES (found by running the spec's own sample
 * questions from Section 9 — see task report for full detail):
 * 1. "Where is most of my money going?" (the spec's own example) comes
 *    back Unsupported, not Spending/Category — the keyword list only has
 *    the exact phrase "where is my money going" (no "most"), and
 *    substring matching doesn't catch the natural variant.
 * 2. "Tell me something about my finances." (the spec's own Ambiguous
 *    example) comes back Unsupported, not Ambiguous — the classifier's
 *    Ambiguous path only fires when two categories TIE with a NONZERO
 *    score; a question matching zero keywords in every category always
 *    falls through to Unsupported first, regardless of how vague it is.
 * This client does not try to work around either issue client-side
 * (Golden Rule: the classifier's output is what it is) — it renders
 * whatever the backend returns, including an Unsupported response for
 * these two cases, and flags both to the team.
 */

const API_BASE_URL = "http://localhost:5080";
const AUTH_HEADER = ""; // demo handler reads X-Demo-User instead; wire real auth later

export class AskHisabDoAPIError extends Error {
  constructor(statusCode, detail) {
    super(`[${statusCode}] ${detail}`);
    this.statusCode = statusCode;
    this.detail = detail;
  }
}

// ---------------------------------------------------------------------
// 1. Call the real endpoint
// ---------------------------------------------------------------------
export async function askHisabDo(userId, question, { baseUrl = API_BASE_URL, authHeader = AUTH_HEADER, demoUserHeader } = {}) {
  const url = `${baseUrl}/api/ai/ask-hisabdo/${encodeURIComponent(userId)}`;
  const headers = { "Content-Type": "application/json" };
  if (authHeader) headers.Authorization = authHeader;
  if (demoUserHeader) headers["X-Demo-User"] = demoUserHeader; // demo backend only

  let response;
  try {
    response = await fetch(url, { method: "POST", headers, body: JSON.stringify({ question }) });
  } catch (err) {
    throw new AskHisabDoAPIError(0, `Network/connection error: ${err.message}`);
  }

  if (response.ok) return response.json();

  let detail;
  try {
    const body = await response.json();
    detail = body.error || JSON.stringify(body);
  } catch {
    detail = await response.text();
  }
  throw new AskHisabDoAPIError(response.status, detail);
}

// ---------------------------------------------------------------------
// 2. Error -> UI state mapping
// ---------------------------------------------------------------------
export function handleApiError(error) {
  if (error.statusCode === 400) {
    return { state: "invalid_input", title: "Check your question", message: error.detail || "That request wasn't valid." };
  }
  if (error.statusCode === 401) {
    return { state: "unauthorized", title: "Can't reach Ask HisabDo", message: "This request wasn't authorized to reach the server." };
  }
  if (error.statusCode === 0) {
    return { state: "offline", title: "You're offline", message: "Check your connection and try again." };
  }
  return { state: "error", title: "Something went wrong", message: "Please try again in a moment." };
}

// ---------------------------------------------------------------------
// 3. Build the display structure the Ask HisabDo chat UI needs
// ---------------------------------------------------------------------
export function buildAnswerDisplay(apiResponse) {
  return {
    category: apiResponse.category,
    confidencePercent: Math.round(apiResponse.confidence * 100),
    answer: apiResponse.answer,
    verifiedValues: apiResponse.verifiedValues || [],
    hasVerifiedValues: (apiResponse.verifiedValues || []).length > 0,
    limitations: apiResponse.limitations || [],
    isVerified: apiResponse.isVerified,
    isUnsupported: apiResponse.isUnsupported,
    needsClarification: apiResponse.needsClarification,
    clarificationPrompts: apiResponse.clarificationPrompts || [],
    isMissingData: !apiResponse.isVerified && !apiResponse.isUnsupported && !apiResponse.needsClarification,
  };
}

// ---------------------------------------------------------------------
// 4. Single entry point for the UI to call
// ---------------------------------------------------------------------
export async function askHisabDoForUI(userId, question, opts = {}) {
  try {
    const response = await askHisabDo(userId, question, opts);
    return { ok: true, data: buildAnswerDisplay(response) };
  } catch (err) {
    if (err instanceof AskHisabDoAPIError) return { ok: false, errorState: handleApiError(err) };
    throw err;
  }
}
