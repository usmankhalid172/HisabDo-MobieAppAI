# Task Report — Spending Intelligence UI + API Integration (Day 11)

**Task:** AI-task-11, Spending Pattern Intelligence
**Assignee:** Laiba
**Status:** UI + integration layer complete and tested against mock/sample data.
Blocked on live endpoint + two API contract gaps (see Notes).

---

## Summary

Designed and built the Spending Intelligence UI (`spending_intelligence_ui.html`)
and the client-side integration layer (`spending_intelligence_client.js`) that
consumes `GET /api/ai/spending-patterns/{userId}?from=...&to=...`, per Section
13 of the Day 11 spec. Covers every required output: Total Spending, Top
Categories, Category Percentages, Current vs Previous Period, Expense Growth,
Largest Increasing/Decreasing Category, Recurring Spending, and AI Spending
Insight — plus empty/no-data states and error handling.

## Design Approach

Styled as a ledger/passbook layout (paper tone, serif headline figures, hairline
dividers between sections) rather than a generic card-grid dashboard, since the
subject is a personal finance/khata app. Category breakdown uses proportional
bar rows instead of a pie/donut chart, so amount, percentage and category name
are all readable at a glance without a legend. A simple canvas line chart
covers the monthly trend requirement.

## Implementation Details

1. **API call** — `fetchSpendingPatterns(userId, from, to)` calls the endpoint,
   throws a typed `SpendingIntelligenceAPIError` on any non-OK response.
2. **Error handling** — `handleApiError()` maps 404 / 401-403 / 400 / network
   failure to friendly UI states — no raw errors ever reach the screen.
3. **Display payload** — `buildDisplayPayload()` reshapes the raw API response
   into exactly the blocks Section 13 asks for, with an `isEmpty` flag driving
   the empty-state screen.
4. **Monthly trend** — `getMonthlyTrend()` calls the endpoint once per month
   and assembles a series client-side (see Note #3 — this is a workaround,
   not the ideal solution).
5. **AI Insight** — `buildFallbackInsight()` generates insight text from
   verified response fields only (growth %, top category, biggest movers,
   recurring count) since the API has no insight field yet (see Note #2).

## Files

- `spending_intelligence_ui.html` — the UI, runnable standalone with sample data
- `spending_intelligence_client.js` — the reusable integration module
- `test_cases_day11.md` — test coverage for the Section 14 edge cases

## Testing

All scenarios in Section 14 were exercised against hand-built JSON matching
each case (live endpoint isn't deployed yet — see Note #1). Full results in
`test_cases_day11.md`. Everything the client layer is responsible for passed;
a few scenarios (duplicates, negative amounts, period-length correctness) are
explicitly out of scope for a display layer and are called out as backend/DB
responsibilities instead of silently assumed to be handled.

## Notes — Items to raise with the team lead / backend owner

1. **Blocking dependency:** `ITransactionRepository.GetTransactionsAsync()` in
   `SpendingPatternIntelligenceService.cs` has no implementation yet — same
   situation as Day 10's `get_user_financial_data()`. This integration layer
   is built and tested against the documented contract, but hasn't been run
   against a live 200 response.

2. **Contract gap — no AI Insight field:** Section 13 requires an "AI Spending
   Insight" in the UI, but neither the example JSON in Section 11 nor the C#
   service produces one anywhere. There's no LLM-explanation step implemented
   for this feature at all yet (unlike Day 10, which at least had an `insights`
   array). Currently working around this with a rule-based client-side fallback
   built only from verified numbers — but recommend the team decide whether
   this insight generation belongs server-side (consistent with the spec's own
   Golden-Rule-style flow diagram in Section 10: "Analytics Engine → Verified
   Spending Patterns → LLM Explanation → User") before this ships for real.

3. **Contract gap — incomplete example response:** The spec's example JSON
   (Section 11) only shows `largestIncreasingCategory`. The C# service also
   computes `LargestDecreasingCategory` and per-category `New`/`Stopped`/
   `Unchanged` trends (Section 8), none of which appear in the example. Built
   this layer to expect the full set and degrade gracefully (shows a "not
   returned by API yet" placeholder) if any are missing — but the actual API
   contract needs to be finalized to include all of them, since Section 13
   explicitly requires "Largest Increasing/Decreasing Category" in the UI.

4. **"Monthly spending trends" needs multiple API calls:** The endpoint only
   returns one current-vs-previous comparison per request. Built
   `getMonthlyTrend()` to call it once per month and stitch together a trend
   series, but this is N network calls for one chart. Recommend a dedicated
   trend endpoint (e.g. `GET /api/ai/spending-patterns/{userId}/trend?months=6`)
   if this feature is used often — flagged in the UI itself as a caption under
   the trend chart so this isn't lost.

## Team Lead Verification Checklist (self-assessed)

- [x] Spending Intelligence UI designed
- [x] Total spending displayed
- [x] Top spending categories displayed
- [x] Category-wise amounts and percentages displayed
- [x] Monthly spending trends displayed (via client-side workaround — Note #4)
- [x] Chart/visual representation added (category bars + trend line)
- [x] Recurring spending information displayed
- [x] AI-generated spending insight displayed (fallback — Note #2)
- [x] Spending Intelligence API integrated (client layer complete)
- [x] Empty/no-data states handled
- [ ] Tested against a live 200 response — **blocked until DB layer is connected (Note #1)**
- [ ] AI Insight field added to real API contract — **needs team decision (Note #2)**
