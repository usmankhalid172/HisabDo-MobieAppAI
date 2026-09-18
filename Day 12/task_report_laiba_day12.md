# Task Report — Anomaly Alerts UI (Day 12)

**Repo:** `usmankhalid172/HisabDo-MobieAppAI`
**Spec:** `Anomaly_Detection/HisabDo_AI_Day12_Anomaly_Detection_Specification.pdf`
**Assignee:** Laiba — §17 "Frontend / UI: Alerts UI, severity/reason/details, API integration, empty/no-anomaly states"
**Status:** Complete against the spec contract. Not yet run against a live backend.

---

## Deliverables

| File | What it is |
|---|---|
| `anomaly_alert_ui.html` | The Alerts UI. Opens standalone in a browser; 5 toggleable states |
| `anomaly_alert_client.js` | API integration module — fetch, error mapping, display-payload builder |
| `test_cases_day12.md` | Coverage mapped to the spec's own TC-01…TC-17 |

## Assigned items

| Requirement | Status |
|---|---|
| Create anomaly alert UI | ✅ |
| Display anomaly type | ✅ Card heading, normalised across both naming conventions |
| Display amount | ✅ Plus the baseline ("Usual") and deviation % when the API supplies them |
| Display category | ✅ Falls back to "Uncategorized" per §23.6 |
| Display date | ✅ Period range shown instead for `periodSpikes` entries, which carry no date |
| Display reason | ⚠️ Works, but the API returns a *code*, not text — see issue 3 |
| Display severity | ✅ Colour-coded badge + stripe, sorted High → Medium → Low, with summary counts |
| Integrate anomaly API | ✅ Built to §11; live call pending deployment |
| Handle no-anomaly/empty states | ✅ Plus a **separate** insufficient-baseline state required by §5 |

## Spec rules the UI enforces

These were requirements, not styling choices:

- **§2 "An anomaly is not proof of fraud."** No fraud/suspicious/theft wording anywhere in the copy.
- **§7.4 duplicates are candidates.** Rendered with a "candidate" badge and a review prompt. No delete or reverse control exists in the UI at all.
- **§5 insufficient baseline.** Fewer than 3 comparable periods gets its own state — the UI will not show thin history as a confident "all clear". When alerts *are* present on thin history, an amber caveat appears above them.
- **§9 no division by zero.** The UI never computes a percentage. If `deviationPercent` is null, the field is omitted rather than shown as 0%.
- **§12 hallucination guard.** Every figure (amount, category, date, baseline, deviation) is read from the verified DTO fields. The AI explanation layer can only supply the *reason sentence* — it structurally cannot alter a displayed number.

## Issues found — please review

**1. Anomaly type naming is inconsistent between two spec files.**
§11's example payload uses `"UnusualAmount"` (PascalCase, no spaces).
`Anomaly explanation/Day12_AI_Anomaly_Explanation_Service.cs` switches on
`"Unusual Amount"` (spaced). If the backend emits one and the explanation
service expects the other, **every `switch` falls through to its default**
and users get generic text instead of the intended wording.
*Handled here:* `normalizeType()` accepts both. *Still needs:* the backend
contract to pick one form, mainly for the explanation service's sake.

**2. No human-readable reason field in the API contract.**
§11 returns `reasonCode` (e.g. `AMOUNT_ABOVE_BASELINE`) — a machine token.
The task requires displaying a "Reason". The client resolves it in priority
order: AI Explanation layer text (§12, preferred) → a backend free-text field
if one is added → a static reasonCode→sentence map → neutral fallback. The
static sentences describe only what each rule checked and contain no figures.
*Decision needed:* is the explanation layer (Aarti) shipping in time, or
should `reasonCode` → text mapping be formalised as the official approach?

**3. `periodSpikes` is a separate top-level array from `anomalies`.**
Easy to miss when integrating, and missing it would silently drop every
Medium/High spending-spike alert (§7.2) from the UI. This client merges both
arrays into one alert list. Flagging so the mobile team doesn't hit the same trap.

**4. Backend not deployed.** The `.cs` files in this repo are reference
implementations against an `IExpenseTransactionRepository` boundary (§19), not
a running service. This layer is built and verified against the documented
contract but has not seen a live 200 response.

## Verification checklist

- [x] All six required fields display
- [x] Empty / no-anomaly state
- [x] Insufficient-baseline state (§5) handled separately
- [x] Duplicates shown as candidates, no destructive action (§7.4)
- [x] No fraud language (§2, §7.1)
- [x] No client-side recalculation of severity or percentages (§9, §12)
- [x] Error states for 400 / 401 / 404 / offline
- [x] Spec test cases reviewed and mapped
- [ ] **Live API integration test — blocked on deployment**
- [ ] **Type-naming convention confirmed — blocked on issue 1**
