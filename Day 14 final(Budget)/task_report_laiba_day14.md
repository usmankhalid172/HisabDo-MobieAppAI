# Task Report — Budget Recommendation UI + API Integration (Day 14)

**Task:** Integrate the Day 14 budget recommendation API; display recommended
budgets, current vs. recommended, category-wise recommendations, AI
explanation, overspending categories, actionable suggestions; handle
missing budget/data states; test API-to-UI flow; validate final output
**Assignee:** Laiba (per spec Section 14 — "Laiba: API/UI integration")
**Status:** Complete — built and verified against a real, running backend

---

## What existed in the repo before this

Same pattern as Days 12 and 13: the repo had two real, complete backend
files with **no HTTP controller** connecting either to anything:

- `budget/Day14_Taha_BudgetRecommendationService.cs` — the real MVP
  recommendation methodology (Taha), following the exact rules in
  `HisabDo_AI_Day14_Taha_AI_Budget_Recommendation_Specification.pdf`
  (Sections 5–7: historical baseline, variance, utilization).
- `AI-Budget-explaination/Day14_Taha_AI_Budget_Explanation_Service.cs` —
  a real, deterministic (not an LLM call) per-category explanation
  generator (Taha), matching spec Section 8's "no-invention rules"
  exactly (it reports every field as "unavailable" rather than guessing
  when a verified value is missing).

I wrote `BudgetRecommendationController.cs` to wire the two together at
`GET /api/ai/budget-recommendations/{userId}`, then **actually built and
ran it** with `dotnet run` and tested it with `curl`. Every JSON shape
below is a real, executed response — see `real_response_normal.json`,
`real_response_limited.json`, `real_response_empty.json`.

There's also a near-duplicate file in the repo
(`Budge REcommendation/Day14_Taha_BudgetRecommendation_Methodology.cs`)
defining the same concepts as C# records instead of classes. Not used
here — flagging as a repo hygiene note, same as the duplicate-namespace
issue found on Day 13.

**`BudgetRecommendationController.cs` needs review/ownership from Taha or
Jaffer before a real merge** — same caveat as the last two days.

## Implementation

**1. API integration** — `fetchBudgetRecommendations()` in
`budget_recommendations_client.js` calls the real endpoint (supports
either `?months=N` or an explicit `?start&end` range).

**2–4. Recommended budgets / current vs. recommended / category-wise** —
each category renders as a card with three side-by-side figures:
Historical Average, Current Budget ("No existing budget" when absent,
never a fabricated 0), and Recommended Budget, plus a utilization bar
(hidden entirely — not shown as 0% — when utilization is `null`, e.g. a
category with a Rs 0 existing budget).

**5. AI explanation** — the real, deterministic
`TahaAIBudgetExplanationService` output (`summary`, `whyRecommendation`,
`overspending`, `savingsOpportunity`, `action`, `limitations`) is
displayed directly per category, never reworded or summarized further by
this layer.

**6. Overspending categories** — `overspendingCategories` (added by the
controller: any category where `HistoricalAverage > ExistingBudget`) is
shown both as a top-level summary chip and as an "Over Budget" flag on
each affected category card.

**7. Actionable suggestions** — `actionableSuggestions` (the distinct set
of `explanation.action` values across all categories, added by the
controller) shown as a top-level list, plus each category's own action
text on its card.

**8. Missing budget/data states** — three real states verified: normal (5
categories, mixed sufficiency), a genuinely empty response (zero
transactions → empty `categories` array → "No budget recommendations yet"
screen), and a short-window request where every category comes back
`dataSufficiency: "Limited"`.

## Verification — actually run, not guessed

```
dotnet build   # succeeded, 0 errors, first attempt
dotnet run     # real server, http://127.0.0.1:5070
```

Then, against that live server (demo data seeded to exercise every rule
in spec Section 6):

| # | Scenario | Result |
|---|----------|--------|
| 1 | Normal user, 3-month window (Jun–Aug 2026) | **200**, 5 categories. Food: existing budget 12,000 < historical average 15,000 → flagged over budget, utilization 125%. Transport: existing budget 6,000 > average 4,333.33 → shown, not forced down. Shopping: no existing budget → recommendation still made. Bills: average exactly equals existing budget (4,500) → "based on baseline", zero variance. Entertainment: existing budget is Rs 0 → utilization correctly `null`, no divide-by-zero |
| 2 | Zero-transaction user | **200**, empty `categories: []`, both summary lists empty — confirmed matches spec Section 10: "No transactions → no numeric recommendation" |
| 3 | Same normal user, 1-month window (Aug only) | **200**, every category returns `dataSufficiency: "Limited"` (the service's `PreferredMinimumMonths = 2` threshold applies to the *requested window length*, not to how much real data exists within it — see Gap #1) |
| 4 | Path `userId` not matching authenticated demo user | **401** (confirmed live) |
| 5 | Invalid `userId` (not a GUID) | **400**, `"userId must be a valid GUID."` |

`test_client_math.mjs` then runs `buildBudgetRecDisplay()` from the actual
client file against all three captured real responses and asserts the
UI-ready output is correct — also actually executed, all checks pass.

## Gaps / issues found — flagging for team lead

1. **`HistoricalAverage` divides by the full requested month count, not by
   months that actually had data** — same pattern as Day 13's Gap #2.
   Confirmed live: "Shopping" had real spending in only 2 of the 3
   requested months (one month was genuinely zero), so its reported
   average is Rs 3,000/month — noticeably lower than the Rs 4,500/month a
   2-month-only average would show. This isn't unique to this file; it's
   now the third service in this codebase (Days 12, 13, 14) with the same
   averaging characteristic. Might be worth a single shared utility and a
   team-wide decision on which behavior is intended, rather than each
   service reimplementing it slightly differently.
2. **"Limited Data" sufficiency is driven by the requested window length,
   not by data density.** A category with three months of dense, complete
   transaction history still gets marked `"Limited"` if the caller only
   requests a 1-month window — confirmed live (Test 3 above). This is a
   reasonable MVP simplification per the spec's own wording ("Insufficient
   category history: Mark as Limited Data"), but worth confirming with
   Taha that "insufficient history" is meant to track the request
   parameters rather than the underlying data itself.
3. **Duplicate type definitions across two folders** (`budget/` vs.
   `Budge REcommendation/`) for what look like the same Day 14 concepts —
   same class of repo hygiene issue flagged on Day 13.

## Files

- `budget_recommendations_ui.html` — the UI, runnable standalone with 3 real captured states, plus a built-in **Live Backend** panel (Base URL / User ID / Months + Fetch button) that calls the real running server directly — no code edits needed, tested end-to-end with a headless browser against a live `dotnet run` server
- `budget_recommendations_client.js` — the reusable integration module (same logic as the panel, as importable ES module functions)
- `test_client_math.mjs` — executed test against real captured responses
- `real_response_normal.json` / `real_response_limited.json` / `real_response_empty.json` — actual server output
- `BudgetRecommendationController.cs`, `BudgetRecDemoAdapters.cs` (as `DemoAdapters.cs`), `BudgetRecApi_Program.cs` (as `Program.cs`, now includes CORS for the live panel), `BudgetRecApi.csproj`, `BudgetRecommendationService.cs`, `AIBudgetExplanationService.cs` — backend files for running this; controller needs team review
- `HOW_TO_RUN_day14.md` — step-by-step guide to run all of this in VS Code

## Team Lead Checklist (self-assessed against the task list)

- [x] Day 14 budget recommendation API integrated — built the missing controller, ran it for real, tested with curl
- [x] Recommended budgets displayed
- [x] Current vs. recommended budget displayed (side-by-side, "No existing budget" instead of a fabricated 0)
- [x] Category-wise recommendations displayed
- [x] AI explanation displayed (real, deterministic service output, not reworded)
- [x] Overspending categories displayed
- [x] Actionable suggestions displayed
- [x] Missing budget/data states handled — verified against a real empty response and a real "Limited" response
- [x] API-to-UI flow tested — end to end against a live server, not mocked
- [x] Final UI output validated — automated test asserts the display output matches the real API response exactly
