# Task Report — Cash Flow Forecast UI (Day 13)

**Repo:** `usmankhalid172/HisabDo-MobieAppAI`
**Spec:** `day-13-forecasting/HisabDo_AI_Day13_Cash_Flow_Forecasting_Detailed_Specification.pdf`
**Assignee:** Laiba — §15 UI job steps 49–58
**Status:** Complete against both contracts. Not yet run against a live backend.

---

## Deliverables

| File | What it is |
|---|---|
| `cashflow_forecast_ui.html` | The Cash Flow Forecast section. Opens standalone; 7 toggleable states |
| `cashflow_forecast_client.js` | API integration module — both contracts, error mapping, display-payload builder |
| `test_cases_day13.md` | Coverage mapped to spec §16 edge cases and §15 job steps |

## Assigned items

| Requirement | Status |
|---|---|
| 1. Design Cash Flow Forecast section | ✅ |
| 2. Display expected income | ✅ |
| 3. Display expected expenses | ✅ |
| 4. Display expected savings | ✅ Colour-coded; negative outlook reads unmistakably |
| 5. Display expected closing balance | ✅ Shows "Not available" when no trusted opening balance (§8) — never guessed |
| 6. Display forecast period clearly | ✅ Header block, chart boundary, tagged table rows |
| 7. Historical vs forecast comparison | ✅ Chart + table. Only available on the real contract — see issue 3 |
| 8. Display AI-generated forecast insight | ⚠️ Renders Aarti's explanation object when present; currently falls back — see issue 4 |
| 9. Handle insufficient-data states | ✅ Including the 400-that-isn't-an-error case — see issue 2 |
| 10. Handle loading and API error states | ✅ Skeleton loading + 5 distinct error states mapped to the real exception types |
| 11. Label predictions as forecasts, not actuals | ✅ Five independent signals — see below |
| §15 step 53: confidence indicator | ⚠️ Derived client-side — see issue 1 |

## Requirement 11 — how forecasts are labelled

A forecast misread as a recorded balance is the most costly failure in this
feature, so the labelling is deliberately redundant:

1. Standing "Forecast" banner naming the period and stating actuals can differ
2. An "est" pill on every figure tile
3. Chart: forecast region shaded, dashed boundary rule, forecast leg dashed and translucent
4. Comparison table: forecast row tinted and tagged
5. Insight closes with "Actual results can differ from this estimate"

## Golden rule compliance (§1, §7, §14)

This layer performs **no arithmetic on financial values**. Every figure is read
from the verified engine result. The one derived value is the confidence band
(issue 1) — a data-quality label, not a financial figure — and it is visibly
marked as derived wherever it appears.

## Issues found — please review

**1. The implemented API returns no `confidence`, `limitations` or `method`.**
Spec §8 defines a full confidence model (High / Medium / Low / Limited /
Unavailable) and §15 step 53 requires the UI to display it, but
`ForecastResult` in `Forecasting/Models.cs` carries none of these fields.
*Handled here:* confidence is derived from the count of historical months
containing data, using the spec's own §8 bands, and labelled in the UI as
derived rather than presented as a backend verdict. *Limitation:* month count
can't capture "Limited" (missing/inconsistent months) or irregular income —
both of which §8 expects to affect confidence. **Recommend adding `confidence`
and `limitations` to `ForecastResult`.**

**2. Insufficient data arrives as an HTTP 400, not a forecast result.**
`ForecastingService` throws `ForecastValidationException` when fewer than 3
periods contain data. Spec §5/§8 instead expects a Low/Limited *result*. A
straightforward client shows that 400 as a generic error — which hides the
insufficient-data state requirement 9 asks for. *Handled here:*
`isInsufficientDataError()` inspects the 400's message and routes it to the
proper state. **This is brittle — it depends on the exception's wording.**
Recommend either a machine-readable error code, or returning a result with
`confidence: "Low"` per the spec.

**3. Two different contracts exist, and they don't match.**

| | Spec §9 | Implemented (`Forecasting/`) |
|---|---|---|
| Method + path | `GET /api/ai/cash-flow-forecast/{userId}` | `POST /api/forecasting` |
| Shape | Flat single forecast | `historical[]` + `expected[]` arrays |
| Confidence / limitations / method | Present | Absent |
| Historical data | Absent | Present |

The client supports both and normalises them to one display payload, so the UI
works either way. But the mobile team needs to know which is authoritative
before they integrate. Worth noting the implemented version is **better** for
requirement 7 — the spec's flat shape gives nothing to compare a forecast
against, so under that contract the comparison section is hidden rather than faked.

**4. Neither endpoint returns the AI explanation.**
`ForecastExplanationService` (Aarti's, §14) exists and produces a well-formed
object — summary, per-figure lines, cash-flow trend, actionable observation,
uncertainty — but no controller calls it or includes it in a response. The UI
renders that exact object whenever the payload carries it; until then it shows
a clearly-marked rule-based fallback built only from the verified figures,
adding no new numbers. **Needs a decision on where the explanation gets attached.**

**5. Minor — insight text is currently rendered as HTML.**
That's fine for the trusted explanation service. If explanation text ever
incorporates user-supplied content (transaction descriptions, per §16's
prompt-injection case), switch that insertion to `textContent`. Noting it now
so it isn't missed later.

**6. Backend not deployed.** The `Forecasting/` project lives on the unmerged
`feature/calculations` branch and depends on an `IFinancialRepository` boundary
with no production implementation. This layer is verified against both
documented contracts but has not seen a live response.

## Verification checklist

- [x] All required figures display, with null-safe "Not available" handling
- [x] Forecast period shown clearly in three places
- [x] Historical vs forecast comparison (chart + table)
- [x] Confidence indicator, honestly labelled as derived
- [x] AI insight rendered; fallback clearly marked
- [x] Forecasts unmistakably labelled as estimates (5 signals)
- [x] Insufficient-data state, including the 400 special case
- [x] Loading skeleton + error states for 400/401/502/503/offline
- [x] No client-side recalculation of any financial value
- [x] Responsive at mobile and desktop breakpoints
- [x] Arithmetic verified: savings = income − expense; closing = opening + saving
- [ ] **Live API integration test — blocked on deployment**
- [ ] **Authoritative contract confirmed — blocked on issue 3**
- [ ] **Confidence/limitations added to the API — blocked on issue 1**
