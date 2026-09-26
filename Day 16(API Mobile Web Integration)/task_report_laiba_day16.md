# Task Report — AI Recommendation UI + API Integration (Day 16)

**Task:** Integrate the recommendation API; display recommendations in
mobile UI with priority, category, explanation, actionable suggestion,
and limitations; handle loading/error/empty states; test the API→UI flow
across multiple recommendations and different users; verify user-specific
recommendations; prepare the UI for Day 17/18 integration
**Assignee:** Laiba (per spec Section 12 — "Laiba: integrate API into
mobile/web UI and show priority, severity, reason, action and
limitations")
**Status:** Complete — built and verified against a real, running backend

---

## What existed in the repo before this

`AI-Engine/Day16_Taha_AI_Recommendation_Engine.cs` is a complete,
deterministic recommendation engine (Taha) — **used here completely
unmodified**, every rule and threshold exactly as specified. But
`TahaRecommendationEngine.Generate()` takes an already-assembled
`VerifiedRecommendationInput`; nothing builds that input from real
verified data (that's Jaffer's task per the spec's own team table:
"connect verified Day 10-14 results, UserId isolation, recommendation
API, missing-data handling"), and there was no HTTP controller either —
same pattern as every day since Day 12.

I wrote two new files to make this testable:
- `RecommendationDemoDataAggregator.cs` — builds `VerifiedRecommendationInput`
  for three demo users covering the exact scenarios this task asks to
  test. **This is explicitly a stand-in for Jaffer's real Day 10-14
  integration work, not a replacement for it.**
- `RecommendationController.cs` — the missing HTTP endpoint,
  `GET /api/ai/recommendations/{userId}`.

**Both need review/ownership from Jaffer before a real merge.** The
engine itself (`RecommendationEngine.cs`) needs no changes — it's
included in this delivery unmodified, for convenience only.

## Implementation

**1. API integration** — `fetchRecommendations()` in
`recommendations_client.js` calls the real endpoint.

**2. Recommendations displayed in mobile UI** — `recommendations_ui.html`
is built mobile-first (480px content width, card-based layout) rather
than adapted from a wider desktop design.

**3. Priority displayed** — a colored badge (`High`/`Medium`/`Low`) on
every card, plus a summary chip row at the top counting each priority
level.

**4. Category displayed** — shown as a small label under the
recommendation type on every card.

**5. Explanation (reason) displayed** — the verified `reason` text
rendered directly, e.g. *"Verified budget utilization for Food is 138%."*

**6. Actionable suggestion displayed** — the verified `action` text,
visually set apart as a highlighted pill on each card.

**7. Limitations displayed where applicable** — a dedicated box above the
card list when `limitations` is non-empty (verified live: the
insufficient-data demo user's three limitation messages).

**8/9. Loading and error states handled** — a skeleton-card loading state
while a live fetch is in flight, and a distinct error box for
unauthorized/not-found/offline/unknown failures (`handleApiError()`).

**10. Empty recommendations handled** — and split into **two distinct
states**, which the spec's Section 8 explicitly calls for but doesn't
name a UI treatment for: a genuinely healthy user gets "✓ Nothing to flag
right now," while a user with no verified data yet gets "⏳ Not enough
data yet" with the specific limitation messages. Both return an empty
`recommendations` array from the API — `isInsufficientData` in the client
tells them apart by whether `limitations` is also empty.

**11/12. Tested for multiple recommendations and different users** — see
below.

**13. User-specific recommendations verified** — see the isolation test
below.

**14. Prepared for Day 17/18** — `toRecommendationCard()` in the client
produces a single stable, typed shape
(`type/typeLabel/priority/severity/category/reason/action/verifiedFigure`)
that Day 17/18's dashboard or mobile screens can consume directly without
re-deriving anything from the raw API response; `TYPE_LABELS` centralizes
the only place recommendation-type display text needs to change.

## Verification — actually run, not guessed

```
dotnet build   # succeeded, 0 errors, first attempt (after adding a
                # JsonStringEnumConverter so priority/severity/type
                # serialize as readable strings, not raw enum integers —
                # the API returned 0/1/2 until this was added)
dotnet run     # real server, http://127.0.0.1:5090
```

Then, against that live server, with three demo users designed to cover
the task's explicit test list:

| # | Scenario | Result |
|---|----------|--------|
| 1 | At-risk user | **200**, 10 real recommendations, correctly sorted High→Medium→Low then by severity then category — matches the spec's own sample dataset (Section 13) for health score 55, saving rate 6%, expense growth 25%, Food budget utilization 138%, expected saving -5000 |
| 2 | Healthy user | **200**, empty `recommendations: []`, empty `limitations: []` |
| 3 | Insufficient-data user | **200**, empty `recommendations: []`, but 3 real limitation messages |
| 4 | User isolation: authenticated as Healthy, requesting At-risk's data | **401** (confirmed live) — a user can never pull another user's recommendations |
| 5 | Unknown user (self-consistent auth, no demo data) | **404**, clear message |

Then went further than curl: used a headless browser (jsdom) to load the
**actual HTML file** and drive it through real DOM interactions against
the real running server — clicked all three sample-data buttons and
confirmed the two distinct empty states render correctly, then used the
**live-fetch panel** to make a real network call and confirmed the real
`Rs 17,940` figure and `Cash-Flow Risk` card appear, then pointed the Base
URL at a closed port and confirmed the friendly error box appears instead
of a crash.

`test_client_math.mjs` then runs `buildRecommendationsDisplay()` from the
actual client file against all three captured real responses and asserts
the UI-ready output is correct, including that two independent verified
sources (a spending-pattern flag and a separate anomaly result) both
producing a Food "Unusual Spending Review" is expected engine behavior,
not a duplicate-rendering bug — also actually executed, all checks pass.

## Gaps / issues found — flagging for team lead

1. **Enums serialize as raw integers by default.** Without adding a
   `JsonStringEnumConverter`, the API returned `"priority": 0` instead of
   `"priority": "High"` — technically correct but unusable for a UI or
   for anyone reading the response by eye. Fixed in `Program.cs`; worth
   confirming this same converter is present wherever this engine's
   output actually gets serialized in production, since it's easy to
   silently omit.
2. **No dedup for recommendations from multiple sources.** The engine
   correctly (and per spec) produces two separate `UnusualSpendingReview`
   entries for Food in the at-risk demo data — one from the spending
   pattern engine (`IsUnusual`), one from the anomaly engine — since both
   verified sources genuinely detected something. This isn't a bug, but
   it's worth confirming with Taha/Aarti whether the UI should visually
   merge same-category-same-type cards from different sources, or keep
   them separate as-is (this delivery keeps them separate, since merging
   would mean deciding which verified reason "wins," which isn't this
   layer's call to make).
3. Same recurring theme as Days 12-15: the aggregation step from real
   verified Day 10-14 results into `VerifiedRecommendationInput` still
   needs to be built for real — this delivery's demo aggregator is a
   testing stand-in, not that integration.

## Files

- `recommendations_ui.html` — the mobile-first UI, with a built-in live-fetch panel and three one-tap demo users — tested end-to-end with a headless browser against a live server, no code edits needed
- `recommendations_client.js` — the reusable integration module (same logic, as importable ES module functions)
- `test_client_math.mjs` — executed test against all 3 real captured responses
- `resp_at_risk.json` / `resp_healthy_empty.json` / `resp_insufficient_data.json` — actual server output
- `RecommendationController.cs`, `RecommendationDemoDataAggregator.cs`, `RecommendationApi_Program.cs` (as `Program.cs`, includes CORS), `RecommendationApi.csproj`, `RecommendationEngine.cs` (Taha's file, unmodified) — backend files for running this; the two new files need team review
- `HOW_TO_RUN_day16.md` — step-by-step guide to run all of this in VS Code

## Team Lead Checklist (self-assessed against the task list)

- [x] Recommendation API integrated
- [x] Recommendations displayed in mobile UI (mobile-first layout)
- [x] Priority displayed
- [x] Category displayed
- [x] Explanation (reason) displayed
- [x] Actionable suggestion displayed
- [x] Limitations displayed where applicable
- [x] Loading state handled
- [x] API errors handled
- [x] Empty recommendations handled — and split into two distinct, correctly-labeled states
- [x] API → UI flow tested — end to end, real server, real DOM, real clicks
- [x] Multiple recommendations tested — 10 real recommendations, correct deterministic order
- [x] Different users tested — 3 distinct demo users
- [x] User-specific recommendations verified — cross-user request correctly rejected with 401
- [x] UI prepared for Day 17/18 — stable typed card shape, centralized type-label mapping
