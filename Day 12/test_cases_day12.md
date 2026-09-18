# Test Cases — Anomaly Alerts UI (Day 12, Laiba)

Mapped to the spec's own numbering in
`Anomaly_Detection/HisabDo_AI_Day12_Anomaly_Detection_Specification.pdf` §15,
plus the Omesha validation datasets in §23.4 that have a UI consequence.

**Scope note.** Most TC-01…TC-17 cases are *detection-engine* tests (Jaffer/Omesha)
— they check whether an anomaly is produced. The UI's job is to render whatever
the verified DTO contains without crashing, mislabelling, or overstating it.
So each row below records **what this layer is responsible for**, which is not
always the same thing the spec case is testing.

| Spec case | Scenario | UI responsibility | Status |
|---|---|---|---|
| TC-01 | No transactions | Render the no-anomaly state, not a blank screen or an error | ✅ "Nothing unusual this period" state |
| TC-02 | Only one transaction / insufficient history | §5 says return an insufficient-baseline state, **not** a clean bill of health. UI must distinguish the two | ✅ Separate "Not enough history yet" state; checked *before* the all-clear branch |
| TC-03 | Zero historical spending | No percentage shown (§9: don't divide by zero) | ✅ `deviationPercent: null` → the Difference field is omitted entirely, never rendered as 0% or NaN |
| TC-04 | Zero current-period spending | Same as TC-01 from the UI's side | ✅ Empty state |
| TC-05 | Invalid/negative amount | Backend validates (§14). UI must not crash if one slips through | ✅ `formatAmount()` returns null on NaN → field omitted |
| TC-06 | Income mixed with expenses | Backend filters to expenses only (§9) | ➖ Out of scope for UI — no way to verify client-side |
| TC-07 | Missing category | §23.6: treat as Uncategorized | ✅ `a.category \|\| "Uncategorized"` |
| TC-08 | New category | §7.3: may report as "New Category"; must **not** auto-label every new category anomalous | ✅ `NEW_CATEGORY` type + reason code supported; UI adds no anomaly framing of its own |
| TC-09 | Very large transaction | No overflow/precision loss in formatting | ✅ `toLocaleString()`; verified at 50M+ scale |
| TC-10 | Exact duplicate-looking transactions | §7.4: show as **candidate**, never auto-delete/reverse | ✅ "candidate" badge + action reads "Review both transactions and confirm whether both should stay" — no destructive control rendered |
| TC-11 | Same amount, legitimately different | UI must not imply certainty | ✅ Same candidate framing; no fraud/duplicate-confirmed language |
| TC-12 | Same description, different dates | Detection-side rule | ➖ Out of scope for UI |
| TC-13 | Multiple users with identical transactions | User isolation (§3, §14) | ✅ On 401/403 the UI shows an auth state and renders **no** data — never falls back to cached/other results |
| TC-14 | Wrong historical period selection | Detection-side | ➖ Out of scope; UI displays whatever `analysisPeriod` the API returns, verbatim |
| TC-15 | Spike exactly at 20% | §7.2 boundary → Medium | ✅ UI renders whatever severity the API sends; no client-side re-banding |
| TC-16 | Spike exactly at 50% | §7.2 boundary → High | ✅ Same — severity is never recomputed client-side |
| TC-17 | AI prompt attempts to change verified values | §12 hallucination guard | ✅ Amount/category/date/baseline/deviation are always read from the **verified DTO fields**, never from the explanation object. The explanation layer can only supply the *reason sentence* — it can't alter a single displayed figure |

### Additional UI-only cases (not in the spec, but needed)

| # | Scenario | Status |
|---|---|---|
| UI-01 | `periodSpikes` populated but `anomalies` empty | ✅ Spikes still render — they're merged into one list, not dropped |
| UI-02 | Type arrives in the other casing (`"Unusual Amount"` vs `"UnusualAmount"`) | ✅ `normalizeType()` resolves both to one label (see issue #1 in the task report) |
| UI-03 | Severity missing or unrecognised | ✅ Falls back to an "unrated" badge; sorts last; no crash |
| UI-04 | `reasonCode` present but unmapped | ✅ Neutral fallback sentence; a note tells the user no reason was supplied |
| UI-05 | Unparseable date string | ✅ `formatDate()` returns the raw string rather than "Invalid Date" |
| UI-06 | 400 / 401 / 404 / offline | ✅ Each mapped to its own friendly state via `handleApiError()`; no raw errors or stack traces shown |
| UI-07 | Alerts present *and* baseline thin | ✅ Amber "based on limited history" banner shown above the list |

### Verification performed

The live endpoint isn't deployed, so these were exercised by feeding
`buildDisplayPayload()` payloads matching each shape and checking the output:

- 5 alerts assembled from 4 `anomalies` + 1 `periodSpikes` entry (nothing dropped)
- Severity counts: 1 high / 3 medium / 1 low; sorted high → medium → low
- `SAMPLE_INSUFFICIENT` → `isEmpty: true`, `isInsufficientBaseline: true` (distinct states)
- `SAMPLE_EMPTY` → `isEmpty: true`, `isInsufficientBaseline: false`
- All four type-casing variants resolve to the same label
- The AI-explanation alert uses the explanation text; its figures still come from the DTO

**Still required before sign-off:** re-run TC-01, TC-02, TC-13, UI-06 against a
live backend. Those depend on real server behaviour, not just this layer's logic.
