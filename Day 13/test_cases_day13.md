# Test Cases — Cash Flow Forecast UI (Day 13, Laiba)

Mapped to the edge cases in §16 of
`day-13-forecasting/HisabDo_AI_Day13_Cash_Flow_Forecasting_Detailed_Specification.pdf`,
plus the UI job steps in §15 (49–58).

**Scope note.** Most §16 cases test the *forecasting engine* (Jaffer/Omesha) —
whether the right number comes out. The UI's job is to render whatever the
verified result contains without crashing, without recalculating, and without
letting an estimate read as a recorded amount. Each row records what this
layer is responsible for.

| Spec case | Scenario | UI responsibility | Status |
|---|---|---|---|
| 3 months history | Minimum usable history | Show forecast, confidence = Medium (§8) | ✅ Verified: 3 months → Medium |
| 6 months history | Full history | Confidence = High | ✅ Verified: 6 months → High |
| <3 months history | Below engine minimum | §5: mark Low/Limited, don't present as reliable. Real service throws **HTTP 400** instead | ✅ `isInsufficientDataError()` catches that 400 and routes to the insufficient-data state, not the generic error state |
| No transactions | Nothing to forecast from | Dedicated no-data state, not a blank card | ✅ Insufficient-data state |
| Only income | No expense history | Render whatever the engine returns; don't infer a missing expense | ✅ Null-safe — missing figures show "Not available" |
| Only expenses | No income history | Same | ✅ Same path |
| Zero income | Income = 0 | No division anywhere client-side (§7) | ✅ This layer performs no arithmetic on figures at all |
| Zero expenses | Expense = 0 | Same | ✅ Same |
| Negative historical cash flow | Expected saving < 0 | Must read clearly as negative, not be hidden | ✅ "Negative cash flow" toggle — figure turns red, insight names the shortfall |
| Large one-time expense | Big outlier in history | No overflow/precision loss in formatting | ✅ `toLocaleString()`; verified at 50M+ scale in prior days |
| Irregular income / expenses | Unstable history | Confidence should reflect data quality | ⚠️ Real API returns no confidence — derived from month count only, so *irregularity* isn't captured. See issue 1 |
| Missing category/description | Not used by forecasting | No UI impact — forecast is income/expense totals only | ➖ N/A |
| Invalid dates | Engine-side validation | UI displays `period` strings verbatim, never reparses them into figures | ✅ |
| Duplicate transactions | Engine/repository responsibility | Cannot be detected from aggregate totals | ➖ Out of scope for UI |
| Missing historical month | Gap in history | §8 suggests "Limited" confidence | ⚠️ Real API returns zero-filled months; the client counts only months with data, so gaps correctly reduce the count — but "Limited" specifically is never returned. See issue 1 |
| No trusted opening balance | Closing balance not computable | §8: closing balance unavailable — must **not** be guessed | ✅ "No opening balance" toggle — shows "Not available" + "Needs a trusted opening balance"; never substitutes 0 |
| User A / User B isolation | Authorization | On 401/403 render the auth state and **no** data | ✅ No cached/partial fallback on auth failure |
| AI cannot modify backend numbers | §1, §14 golden rule | Every figure read from verified DTO fields; the explanation object supplies prose only | ✅ Structurally enforced — explanation fields are never used as a numeric source |
| Prompt injection in descriptions | Text reaching the UI | Insight text is inserted as text content, not executed | ⚠️ Sample insight is rendered as HTML for formatting. **If backend text is ever untrusted, switch to `textContent`.** Flagged in the task report |

### UI job steps (§15) coverage

| Step | Requirement | Status |
|---|---|---|
| 49 | Create Cash Flow Forecast section/card | ✅ |
| 50 | Show forecast period | ✅ Header block + row tags in the comparison table |
| 51 | Show expected income / expense / saving | ✅ Three of the four figure tiles |
| 52 | Show expected closing balance when available | ✅ Fourth tile; graceful "Not available" when absent |
| 53 | Show confidence / data-quality indicator | ✅ Badge + explanation, labelled as derived (issue 1) |
| 54 | Show AI explanation | ✅ Renders the explanation object when present, else marked fallback |
| 55 | Clearly label predicted values as forecast/estimated | ✅ See below |
| 56 | Create limited-data and no-data states | ✅ Insufficient-data state |
| 57 | Handle API loading/error states | ✅ Skeleton loading + 5 distinct error states |
| 58 | Test mobile/web presentation | ✅ Responsive at 720px and 460px breakpoints |

### Requirement 11 — forecast labelling (five independent signals)

Because a forecast misread as an actual balance is the most costly failure here:

1. Standing "Forecast" banner above the figures naming the period and stating actuals can differ
2. An "est" pill on every single figure tile
3. Chart: forecast region shaded, boundary marked with a dashed rule, forecast leg dashed and translucent
4. Comparison table: forecast row tinted and tagged "forecast"
5. Insight text closes with "Actual results can differ from this estimate"

### API error states (requirement 10)

| Condition | Source | UI state |
|---|---|---|
| <3 usable periods | `ForecastValidationException` → 400 | Insufficient data (not an error) |
| Other validation failure | `ForecastValidationException` → 400 | "Check the dates" |
| Unauthenticated / wrong user | Controller → 401 | "Can't load your forecast" |
| History or balance unloadable | `ForecastUnavailableException` → 503 | "Temporarily unavailable" |
| Engine failure / invalid result | `ForecastEngineException` → 502 | "Couldn't build your forecast" |
| Network failure | fetch throws | "You're offline" |
| Request in flight | — | Skeleton loading state |

### Verification performed

Live backend isn't deployed, so `buildDisplayPayload()` was exercised against
payloads matching each shape:

- Real contract: savings = income − expense ✓ · closing = opening + saving ✓ · 6 months → High ✓ · 6 historical periods available for comparison ✓
- Spec contract: backend-supplied confidence used as-is (not derived) ✓ · comparison section correctly hidden (contract carries no history) ✓ · limitations rendered ✓
- Negative: expected saving −21,750 renders negative ✓ · 4 months → Medium ✓
- No opening balance: closing balance stays `null`, never substituted ✓ · insight states it's unavailable ✓
- Confidence bands 0/1/2/3/5/6/7 months → Unavailable/Low/Low/Medium/Medium/High/High, matching §8 ✓

**Before sign-off:** re-run the insufficient-data 400, 401, 502 and 503 paths
against a live backend — those depend on real server behaviour, not this layer.
