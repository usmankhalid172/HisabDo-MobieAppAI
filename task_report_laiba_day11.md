# Task Report — Spending Intelligence UI + API Integration (Day 11, CORRECTED)

**Task:** Design the Spending Intelligence UI + integrate the Spending Intelligence API
**Assignee:** Laiba **Status:** Rebuilt against the real backend; verified with automated tests

---

## What was wrong before (why this got rebuilt)

Two earlier versions of this deliverable existed in the `Day_11.zip` you
uploaded, and **both were built against a backend contract that doesn't
exist anywhere in this repo**:

- v1 assumed `GET /api/ai/spending-patterns/{userId}`
- v2 assumed `GET /api/v1/spending-pattern-intelligence/{user_id}` on a
  branch (`feature/spending-pattern-intelligence`) with Python files
  (`spending_pattern_intelligence.py`, `spending_pattern_router.py`) that
  aren't in `BudgetAnalysisService.zip`.

The **real** backend you sent this time is a working ASP.NET Core 8
service with two endpoints:

- `POST /api/budgets/analysis` → `BudgetAnalysisResult`
- `POST /api/forecasting` → `ForecastResult`

Neither matches either earlier assumption — different field names,
different shape, different HTTP method, different error codes, C# instead
of Python. Everything below is rebuilt from scratch against this real
contract. **This is the correction you asked for.**

## How this was verified without the .NET SDK

`dotnet-sdk-8.0` isn't installable in this sandbox — the Microsoft package
feed it needs isn't on the network allowlist here, and `apt-get install`
fails with 404s. So instead of guessing at the C# service's output a
third time, I read every `.cs` file line by line and wrote
`reference_port.py`, a faithful Python port of the exact same branches
and thresholds, run against the exact same seed data from `DemoAdapters.cs`.
Its output is the "verified ground truth" used below and in the test
suite. **You should still run the real project with `dotnet run`
yourself** (step-by-step below) — this Python port is a stand-in for
verification here, not a replacement for actually running your teammate's
code.

Both `reference_port.py` and `test_client_math.mjs` (the client-side test)
were actually executed — not just written — and both pass. Full output
in `test_cases_day11.md`.

## Implementation

**1. API integration** — `fetchBudgetAnalysis()` and `fetchForecast()` in
`spending_intelligence_client.js`, `POST`-ing to the two real endpoints
with the exact request/response shapes from `Models.cs`.

**2. Score/status display** — `totalSpending` comes straight from
`totalExpense` (never recalculated).

**3. Top categories / category amounts & percentages** — the API only
returns `averageMonthlySpend` per category, not a period total or a
percentage. This layer recovers the real total the exact same way the C#
service produced the average in the first place:
`categoryTotal = averageMonthlySpend × monthsBetween(from, to)`
(`monthsBetween` is a byte-for-byte copy of the C# service's own
`MonthsBetween()`). Percentage of total spending is then
`categoryTotal / totalExpense × 100`. No new financial figure is
invented — an existing verified one is un-averaged.

**4. Monthly spending trend** — `historical[].expense` from
`POST /api/forecasting`, one call, real per-month numbers. (The earlier,
wrong version needed N calls for this; the real API is actually better.)

**5. Chart** — a canvas line chart over the trend data, category rows as
proportional bars (kept the ledger/passbook visual style from the earlier
draft, since that part of the design wasn't the problem).

**6. Recurring spending** — genuinely not derivable from either endpoint
(both only return aggregated totals, never individual transactions).
Reported as unavailable with a specific reason, not faked.

**7. AI spending insight** — no insight/LLM field exists on either
endpoint. Rule-based fallback built only from verified fields (top
category, `adjustmentReason` for over-budget categories, trend direction),
clearly flagged `aiInsightIsFallback: true` so the UI never presents it as
real AI output.

**8. Empty/no-data state** — `status === "insufficient_data"` (fewer than
3 months of data, or zero budgets configured — confirmed in
`BudgetAnalysisService.cs`). Note this is not the same as "zero
transactions" — `totalIncome`/`totalExpense` can still be non-zero in this
state, so the empty screen still shows whatever total was recorded.

## Gaps found — flagging for team lead / backend owner

1. **An expense with a missing/blank category is silently dropped from
   `totalExpense` entirely**, not just left uncategorized. Line in
   `BudgetAnalysisService.cs`:
   `expenses = transactions.Where(x => x.Type == Expense &&
   !string.IsNullOrWhiteSpace(x.Category))`. This means "Total Spending"
   on the UI can under-report real spending if any transactions are missing
   a category. Recommend either grouping those under "Uncategorized"
   instead of excluding them, or explicitly documenting that category is
   mandatory at data-entry time.
2. **No category totals or percentages in the API response** — only
   `averageMonthlySpend`, `currentBudget`, and `utilizationPercent`
   (utilization is vs. *budget*, not vs. *total spending*, which is a
   different number). This client recovers what it needs by reversing the
   average, but it would be more robust if the API just returned the
   period total per category directly.
3. **No recurring-spending or AI-insight fields on either endpoint** —
   same category of gap as Day 10's spec; both are currently client-side
   approximations, clearly marked as such.
4. **Demo auth accepts any request as a fixed user** — fine for local
   testing, but `userId` in every request body must currently equal
   `11111111-1111-1111-1111-111111111111` or the service returns 400. Real
   auth needs to be wired in before this ships.

None of the above block using this UI locally against the demo data — they're
either contract-completeness gaps or clearly-flagged approximations, not bugs
in what's implemented.

## Files

- `spending_intelligence_client.js` — real API integration + category-total recovery + fallback insight
- `spending_intelligence_ui.html` — the UI, runnable standalone (ledger-style design, real demo-data fixtures)
- `test_client_math.mjs` — executed test proving the client's math matches `reference_port.py`'s verified output
- `test_cases_day11.md` — full scenario coverage table
- `reference_port.py` — verified ground-truth reference (see "How this was verified" above)
- `HOW_TO_RUN.md` — step-by-step guide to run the real backend + this UI in VS Code

## Team Lead Verification Checklist (self-assessed)

- [x] Spending Intelligence UI designed
- [x] Total spending displayed (from real `totalExpense`)
- [x] Top spending categories displayed (ranked from recovered totals)
- [x] Category-wise amounts and percentages displayed (recovered + derived, verified against ground truth)
- [x] Monthly spending trends displayed (real, single call, from `/api/forecasting`)
- [x] Chart/visual representation added (category bars + trend line)
- [x] Recurring spending information displayed (honestly reported as unavailable, with reason)
- [x] AI-generated spending insight displayed (fallback, clearly labeled)
- [x] Spending Intelligence API integrated — **against the real contract this time**
- [x] Empty/no-data states handled (`insufficient_data`, forecast-unavailable, network/auth errors)
- [x] Client math verified against a faithful port of the actual backend logic
- [ ] Re-run against the live `dotnet run` server on a machine with the .NET SDK (see `HOW_TO_RUN.md`) — recommended before sign-off
