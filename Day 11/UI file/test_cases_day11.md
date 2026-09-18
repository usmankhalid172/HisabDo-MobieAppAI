# Test Cases — Spending Pattern Intelligence (Day 11)

**Scope:** Client-side integration layer (`spending_intelligence_client.js`) and
UI (`spending_intelligence_ui.html`) built on top of
`GET /api/ai/spending-patterns/{userId}?from=...&to=...`.

Per Section 14 of the spec, plus the API-error states this layer adds on top.

| # | Scenario | Expected behavior | Verified in this layer |
|---|----------|--------------------|--------------------------|
| 1 | No transactions at all | `totalExpense = 0`, empty `topCategories`, no crash | `buildDisplayPayload` sets `isEmpty: true`; UI shows the empty-state screen (see "Empty state" toggle in the HTML) |
| 2 | Transactions exist but none are expenses (all income) | Same as #1 — `totalExpense = 0` | Same empty-state path handles this identically, since the client only reads `totalExpense`, not transaction count |
| 3 | Total expense = 0 | No division-by-zero on category %; percentages all 0 or category list empty | `buildDisplayPayload` never divides — it only reads pre-computed percentages from the API; UI's `maxCat` calculation is skipped entirely by the empty-state branch |
| 4 | Previous period expense = 0 | `expenseGrowthPercent` must be `null` (per spec Section 7), not `Infinity` or a crash | `periodComparison.trendLabel` handles `null`/`undefined` explicitly → shows "No prior period" instead of computing a % |
| 5 | Missing/invalid category | Falls under "Uncategorized" (per `NormalizeCategory` in the C# service) | UI renders whatever category string the API sends, including "Uncategorized" — no special-casing needed since normalization happens server-side |
| 6 | Duplicate transaction | Must be de-duplicated before aggregation (DB/repository layer), not by this layer | **Out of scope for this layer** — same conclusion as Day 10's duplicate test; flagged to backend/repository owner, not something a display layer can detect |
| 7 | Negative/invalid amount | Must be filtered out before totals reach the client (`ValidExpenses` in the C# service filters `Amount >= 0`) | **Out of scope for this layer** — verified the filter exists server-side; client trusts the numbers it receives per the Golden Rule (Section 10) |
| 8 | Very large transaction | No overflow/precision loss when formatting | `fmt()` uses `toLocaleString()`, tested manually at 50,000,000+ scale with no formatting errors |
| 9 | Only one period of history | `previousTotalExpense` and comparison categories may be absent/zero | Handled the same way as #4 — `growthPercent: null` path |
| 10 | Different period lengths (custom range vs. full month) | Both periods must be equal-length day counts (Section 5) | **Backend responsibility** (`AnalyzeAsync` computes `previousFrom`/`previousTo` from the requested range) — client just displays whatever `period`/`previousPeriod` come back, doesn't recompute them |
| 11 | Unauthorized access to another user's data | API must reject with 401/403, never leak data | `handleApiError()` maps `401`/`403` to a generic "Can't load this data" UI state — never displays partial data on this path |
| 12 | API returns 404 (user/records not found) | Friendly empty/no-data message, no raw error | Mapped to `state: "no_data"` |
| 13 | API returns 400 (invalid date range) | Friendly message asking to change the range | Mapped to `state: "invalid_range"` |
| 14 | Network failure / offline | Friendly offline message, no unhandled exception | Mapped to `state: "offline"` |
| 15 | API omits `largestDecreasingCategory` (contract gap — see task report) | UI must not crash on missing field | `data.largestDecreasingCategory \|\| null` guard; UI shows "not returned by API yet" placeholder instead of breaking |
| 16 | API omits `aiInsight` (contract gap — see task report) | UI shows a usable insight anyway | Falls back to `buildFallbackInsight()`, built only from verified fields already in the response, with a visible "fallback" note so no one mistakes it for a verified AI insight |
| 17 | "Verify AI uses only supplied verified values" (spec requirement) | The fallback insight generator must never invent a number | Manually reviewed `buildFallbackInsight()` — every value it prints (`growth`, `top.percentage`, `changeAmount`, candidate count) is read directly from the API response, nothing is calculated or guessed client-side |

## How to exercise these manually right now

Since the live `/api/ai/spending-patterns/{userId}` endpoint isn't deployed yet
(same blocking state as Day 10 — backend/DB wiring still pending per the C#
service's repository interface), scenarios were verified by feeding
`buildDisplayPayload()` hand-built JSON objects matching each scenario above,
plus the two toggle states built into `spending_intelligence_ui.html`
("Sample data" / "Empty state").

**Recommend before sign-off:** once the endpoint is live, re-run scenarios
1, 4, 9, 11, 12, 13 against real HTTP responses — those are the ones whose
correctness depends on the actual server behavior, not just this layer's logic.
