# Test Cases — Spending Intelligence (Day 11, corrected)

**Scope:** Client-side integration layer (`spending_intelligence_client.js`) and
UI (`spending_intelligence_ui.html`), built against the REAL backend in
`BudgetAnalysisService.zip`: `POST /api/budgets/analysis` + `POST /api/forecasting`.

All rows marked "Verified" were actually run — see `test_client_math.mjs`
(client math, run with `node`) and `reference_port.py` (backend logic,
run with `python3` since the .NET SDK isn't installable in this sandbox —
see the task report for why). Rows marked "By inspection" were confirmed by
reading the C# source directly, not executed, because they require the
live ASP.NET server.

| # | Scenario | Expected behavior (per C# source) | Status |
|---|----------|--------------------------------------|--------|
| 1 | Normal financial data | Correct total spending, correctly-ranked categories, correct % of total, 5-month trend | **Verified** — `test_client_math.mjs` Test 1, matches `reference_port.py` ground truth exactly (Living = Rs 16,500 = 100%, trend = [3200,3300,3100,3400,3500]) |
| 2 | Zero income | `BudgetAnalysisService.AnalyzeAsync` doesn't divide by income anywhere in the category/total logic, so this doesn't crash the analysis endpoint. `ForecastingService` also never divides by income. | By inspection |
| 3 | No transactions | `monthsWithData = 0 < 3` → `status: "insufficient_data"`, `categories: []` | **Verified** — same code path as Test 2 (insufficient_data), confirmed `topCategories`/`monthlyTrend` are never referenced when `isEmpty: true` |
| 4 | No budget configured | `budgets.Count == 0` → `status: "insufficient_data"` regardless of transaction history | By inspection — same gate as monthsWithData, `BudgetAnalysisService.cs` line 42 |
| 5 | Negative cash flow (expense > income) | No division or comparison in the analysis path depends on income vs. expense sign; `ForecastPeriod.Savings = Income - Expense` can be negative and the client does not reject negative trend values | By inspection |
| 6 | Large transactions | `decimal` (not float/double) is used throughout the C# service, so no precision loss at scale; client uses `toLocaleString()` for display | By inspection (C# side) — client-side formatting already tested at 16,500 scale in Test 1 |
| 7 | Duplicate transactions | Must be de-duplicated before reaching the repository (`IFinancialRepository.GetTransactionsAsync`) — the service has no dedup logic of its own | **Out of scope for this layer** — flagged to whoever implements the real (non-demo) `IFinancialRepository` |
| 8 | Missing/invalid category | `expenses = transactions.Where(x => x.Type == Expense && !string.IsNullOrWhiteSpace(x.Category))` — an expense with a null/blank category is silently excluded from `expenses` (so also excluded from `totalExpense`!) | By inspection — **this is a real gap worth flagging**: an expense with no category doesn't just get grouped as "Uncategorized", it's dropped from total spending entirely. See task report Gap #1. |
| 9 | User-data isolation | `service.AnalyzeAsync` takes `authenticatedUserId` from the auth claim (not the request body) and throws `ForecastValidationException("Invalid user ID.")` if `request.UserId != authenticatedUserId` — a user literally cannot request another user's data through this endpoint | By inspection — confirmed in `BudgetAnalysisController.cs` / `BudgetAnalysisService.ValidateRequest` |
| 10 | Forecast has fewer than 3 usable periods | `ForecastingService` throws `ForecastValidationException`, mapped to HTTP 400 | **Verified** — Test 3 confirms the client handles `forecastResult === null` (what `.catch(() => null)` produces) without crashing, `trendAvailable: false`, categories still render |
| 11 | Unauthorized (missing/invalid auth) | Controller returns 401 before either service method runs | `handleApiError()` maps 401 → "Can't load this data" state — verified by code review of `handleApiError()`, not executable without a live unauthenticated request |
| 12 | Invalid date range (`From > To`) | `ForecastValidationException` → 400 | `handleApiError()` maps 400 → "Check the request" state |
| 13 | Backend data-load failure | `ForecastUnavailableException` → 503 | `handleApiError()` maps 503 → "Data temporarily unavailable" state (new — this status code didn't exist in the earlier, wrong contract) |
| 14 | Forecasting engine failure | `ForecastEngineException` → 502 (forecasting endpoint only, not budget analysis) | `handleApiError()` maps 502 → "Trend data unavailable" state, distinct from a total failure since category data comes from a separate call |
| 15 | Network failure / offline | `fetch()` throws | Mapped to `state: "offline"` |
| 16 | Recurring spending requested | Neither endpoint returns transaction-level data | Reported honestly as unavailable via `getRecurringStatus()` — never fabricated |
| 17 | AI Insight requested | Neither endpoint has an insight/LLM field | Rule-based fallback from verified fields only, `aiInsightIsFallback: true` always set so the UI never presents it as real AI output |

## How these were actually run

1. `reference_port.py` — a line-by-line Python port of `BudgetAnalysisService.cs`
   / `ForecastingService.cs` / `DemoAdapters.cs`, run with `python3
   reference_port.py`, to get verified ground-truth JSON for the seeded demo
   dataset (the .NET SDK could not be installed in this sandbox — see task
   report). Every branch/threshold in it is copied verbatim from the C# source.
2. `test_client_math.mjs` — runs `buildDisplayPayload()` from the real
   `spending_intelligence_client.js` against that ground-truth JSON and
   asserts the numbers match exactly. Run with `node test_client_math.mjs`.

**Before sign-off:** once `dotnet run` works on a real machine (see the
step-by-step guide), re-run scenarios 2, 5, 6, 9, 11–15 against live HTTP
responses — those depend on the actual ASP.NET server behavior, not just
this client layer's logic.
