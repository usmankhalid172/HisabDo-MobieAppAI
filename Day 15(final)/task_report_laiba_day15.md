# Task Report — Ask HisabDo UI + API Integration (Day 15)

**Task:** Integrate the Ask HisabDo API, build the question input flow, display
the AI answer + relevant verified values, handle loading/response/missing-data/
unsupported states, display forecast/budget/anomaly info where applicable, test
the complete question-to-answer flow, validate mobile/web integration
**Assignee:** Laiba (Day 15's team table doesn't name a UI owner explicitly —
Omesha owns classification, Taha owns orchestration + AI explanation
boundary — so this follows the same API/UI integration role Laiba has had
since Day 14)
**Status:** Complete — built and verified against a real, running backend

---

## What existed in the repo before this

Two real, independent files, never connected to each other or to an HTTP
endpoint:

- `query classification/Day15_AskHisabDo_QueryClassification_Service.cs`
  (Omesha) — a complete, working rule-based classifier
  (`AskHisabDoQueryClassifier`) that maps a question to a
  `FinancialQueryCategory`.
- `Day-15/Day15_Taha_AskHisabDoAI_Service.cs` (Taha) — an orchestration
  service (`AskHisabDoService`) that depends on two interfaces,
  `IAskHisabDoIntentClassifier` and `IAskHisabDoDataService`, **neither of
  which had any implementation anywhere in the repo.** The two files even
  use different enum names for the same categories (`SpendingCategory` vs.
  `SpendingPattern`).

To make this testable at all, I wrote three new files:
- `AskHisabDoIntentClassifierAdapter.cs` — wires Omesha's classifier to
  Taha's interface.
- `AskDataService.cs` — a real implementation of `IAskHisabDoDataService`.
  **Important scope note:** in production this should call the actual Day
  10-14 services (already built and verified in those days' deliverables),
  not duplicate their math. Since combining five separate ASP.NET projects
  into one was out of scope for an API/UI integration task, this class
  reproduces the same MVP formulas from each of those specs directly over
  one shared demo dataset — every number is genuinely computed from seeded
  data, never hardcoded per question, but it's a stand-in for the real
  integration, not the real integration itself.
- `AskHisabDoController.cs` — the missing HTTP endpoint,
  `POST /api/ai/ask-hisabdo/{userId}`.

**These three files need review/ownership from Omesha and Taha before a
real merge** — same caveat as every prior day.

## Implementation

**1. API integration** — `askHisabDo()` in `ask_hisabdo_client.js` posts
the question to the real endpoint.

**2. Question input flow** — a chat-style composer (text input + Ask
button + Enter-to-send) at the bottom of `ask_hisabdo_ui.html`, plus a row
of one-tap sample questions covering every category in the spec's own
Section 9 validation table.

**3. AI-generated answer display** — each response renders as a chat
bubble with the `answer` text. Per Section 13.6 of the Day 15 spec ("LLM
integration boundary… until an LLM provider is connected, this safe
implementation returns the verified backend context directly"), no LLM is
actually wired up yet — the "AI-generated answer" is Taha's own
deterministic fallback text, exactly as his service already specifies.

**4. Relevant verified financial values** — shown as a chip row under each
answer (e.g. `Income: Rs 50,000`, `Category: Food`, `Deviation: 990.9%`),
sourced from a new `verifiedValues` field the controller adds (Taha's
`VerifiedFinancialContext` record only carries answer text, not structured
values, so this was added at the controller layer without touching his
sealed types).

**5/6. Loading and response states** — an animated "typing" indicator bubble
while the request is in flight, replaced by the real answer or a friendly
error box.

**7. Missing-data handling** — verified live: asking about a Shopping
budget (no budget exists for that category) returns
`isVerified: false` with the limitation `"No budget exists for Shopping."`
and **no fabricated number anywhere in the answer** — the UI renders this
as a distinct "missing data" pill, not lumped in with Unsupported.

**8. Unsupported-question handling** — verified live: off-topic questions
("What is today's weather?") return `isUnsupported: true` with the spec's
own fallback message, rendered with a distinct gray pill.

**9. Forecast/budget/anomaly info displayed where applicable** — each of
those three categories has its own verified-values shape (forecast: income/
expense/saving/closing balance; budget: category vs. limit and %
utilization; anomaly: category, amount, baseline, deviation %) — see the
captured `resp_*.json` fixtures.

**10/11. Testing the complete flow + mobile/web validation** — see below.

## Verification — actually run, not guessed

```
dotnet build   # succeeded, 0 errors, first attempt
dotnet run     # real server, http://127.0.0.1:5080
```

Tested every sample question from the spec's own Section 9 table against
the live server with `curl`, capturing real responses
(`resp_income_expense.json` through `resp_unsupported_or_ambiguous.json`).
Then went further than a curl check: used a headless browser (jsdom) to
load the **actual HTML file**, fill in the live panel, click the **actual
sample-question buttons**, and type into the **actual composer input**
with a real `Enter` keypress — against the real running server — and
confirmed:
- The Budget sample button produces a bubble containing the real `Rs
  9,000` figure and a `Budget` category pill.
- Typing "Did I have any unusual expenses?" and pressing Enter produces
  the real `990.9%` deviation figure, and clears the input afterward.
- The missing-data sample button produces the exact limitation text `"No
  budget exists for Shopping"` and a distinct `missing` pill class (not
  `unsupported`).
- The unsupported sample button produces the `unsupported` pill class.
- 8 messages total rendered correctly across 4 question/answer exchanges.

`test_client_math.mjs` then runs `buildAnswerDisplay()` from the actual
client file against all 10 captured real responses and asserts the
UI-ready output is correct for every category — also actually executed,
all checks pass.

## Gaps / issues found — flagging for team lead

**Both of these were found by running the spec's own Section 9 sample
questions against the real classifier — not by inspection:**

1. **"Where is most of my money going?" — the spec's own Spending/Category
   example — is misclassified as Unsupported.** The keyword list only
   contains the exact phrase `"where is my money going"` (no "most"), and
   the classifier does substring matching, not fuzzy/synonym matching, so
   the natural variant with one extra word scores 0 everywhere and falls
   through to Unsupported. Confirmed live and captured in
   `resp_spec_example_should_be_spending.json`.
2. **"Tell me something about my finances." — the spec's own Ambiguous
   example — is also misclassified as Unsupported, not Ambiguous.** The
   classifier's ambiguous path (`best.Value == second.Value`) only fires
   when two categories **tie with a nonzero score**; a question that
   matches **zero** keywords in every category always hits the
   `best.Value == 0 → Unsupported` branch first, regardless of how vague
   the question is. This means genuinely vague questions can never
   actually produce a clarification prompt — confirmed live in
   `resp_unsupported_or_ambiguous.json`. This directly contradicts spec
   Section 9's own expected-category column for this exact sentence.

Both are real, reproducible gaps between the spec's own acceptance table
and the shipped classifier — worth a quick fix from Omesha (broadening the
Spending keyword list; adding a low-confidence-catch-all before the
zero-score Unsupported branch) rather than a UI workaround, since the
client correctly has no way to know a question was "supposed to" classify
differently.

3. **`IAskHisabDoDataService` had no implementation at all** — same class
   of gap as Days 12-14, but one level more foundational here since it
   blocks Taha's entire orchestration service from doing anything. The
   demo implementation in `AskDataService.cs` should be replaced with real
   calls into the Day 10-14 services before this goes further.

## Files

- `ask_hisabdo_ui.html` — the chat UI, with a built-in live-connection panel (Base URL / User ID) and one-tap sample questions covering every spec category — tested end-to-end with a headless browser against a live server, no code edits needed to try it
- `ask_hisabdo_client.js` — the reusable integration module (same logic, as importable ES module functions)
- `test_client_math.mjs` — executed test against all 10 real captured responses
- `resp_*.json` — actual server output for every category, plus missing-data, unsupported, and the two spec-sample-question gap cases
- `AskHisabDoController.cs`, `AskHisabDoIntentClassifierAdapter.cs`, `AskDataService.cs`, `AskHisabDoApi_Program.cs` (as `Program.cs`, includes CORS), `AskHisabDoApi.csproj`, `QueryClassificationService.cs`, `AskHisabDoAIService.cs` — backend files for running this; the three new files need team review
- `HOW_TO_RUN_day15.md` — step-by-step guide to run all of this in VS Code

## Team Lead Checklist (self-assessed against the task list)

- [x] Ask HisabDo API integrated — built the missing adapter, data service and controller, ran it for real, tested with curl and a headless browser
- [x] User question input flow created (composer + Enter-to-send + one-tap samples)
- [x] AI-generated answer displayed
- [x] Relevant verified financial values displayed
- [x] Loading and response states handled
- [x] Missing-data responses handled — verified live, no fabricated numbers
- [x] Unsupported questions handled — verified live
- [x] Forecast/budget/anomaly information displayed where applicable
- [x] Clear, understandable response UI maintained
- [x] Complete question-to-answer flow tested — end to end, real server, real DOM, real clicks and keystrokes
- [x] Mobile/web integration validated — same client module works for both; responsive layout
- [x] Found and flagged two real classifier gaps against the spec's own acceptance table, instead of silently working around them
