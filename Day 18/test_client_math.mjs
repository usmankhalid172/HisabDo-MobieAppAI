import { DashboardViewModel, LoadState, recommendationCard, findRecommendationDetail } from "./web_dashboard_client.js";
import assert from "node:assert";
import { readFileSync } from "node:fs";

function load(name) {
  return JSON.parse(readFileSync(new URL(`./${name}`, import.meta.url)));
}

// Patch global fetch per-test so DashboardViewModel exercises real logic
// against real, previously-captured server responses (not a live server).
function mockFetchOnce(jsonBody, status = 200) {
  global.fetch = async () => ({
    ok: status >= 200 && status < 300,
    status,
    json: async () => jsonBody,
  });
}

console.log("=== Test 1: Rich dashboard -> Success state, all cards present ===");
mockFetchOnce(load("resp_dashboard_rich.json"));
const vmRich = new DashboardViewModel({ demoUserHeader: "dddddddd-dddd-dddd-dddd-dddddddddddd" });
const stateRich = await vmRich.load();
console.log(JSON.stringify({ state: stateRich.state, cardCount: stateRich.data.recommendations.length }, null, 2));
assert.strictEqual(stateRich.state, LoadState.Success);
assert.strictEqual(stateRich.data.financialHealth.score, 72);
assert.strictEqual(stateRich.data.recommendations.length, 4);
const detail = findRecommendationDetail(stateRich.data, "rec-transport-budget");
assert.ok(detail, "recommendation detail must be findable by id from the loaded dashboard");
assert.strictEqual(detail.severity, null, "missing severity must stay null, never a placeholder string");
assert.strictEqual(detail.expectedBenefit, null, "missing expected benefit must stay null, never invented");
assert.ok(detail.limitation, "this recommendation does carry a real limitation and it must survive through");
console.log("PASS\n");

console.log("=== Test 2: Empty dashboard -> Empty state, not Success with nulls ===");
mockFetchOnce(load("resp_dashboard_empty.json"));
const vmEmpty = new DashboardViewModel({ demoUserHeader: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" });
const stateEmpty = await vmEmpty.load();
console.log(JSON.stringify(stateEmpty, null, 2));
assert.strictEqual(stateEmpty.state, LoadState.Empty);
assert.strictEqual(stateEmpty.message, "No AI financial insights are available yet.");
console.log("PASS\n");

console.log("=== Test 3: HealthOnly dashboard -> Success (one real card is enough), not Empty ===");
mockFetchOnce(load("resp_dashboard_health_only.json"));
const vmHealthOnly = new DashboardViewModel({ demoUserHeader: "ffffffff-ffff-ffff-ffff-ffffffffffff" });
const stateHealthOnly = await vmHealthOnly.load();
console.log(JSON.stringify({ state: stateHealthOnly.state, health: stateHealthOnly.data.financialHealth }, null, 2));
assert.strictEqual(stateHealthOnly.state, LoadState.Success, "one verified card is enough for Success — must not fall through to Empty");
assert.strictEqual(stateHealthOnly.data.spendingPatterns.length, 0, "other cards render their own independent empty state, not shown here");
console.log("PASS\n");

console.log("=== Test 4: API error -> Error state with safe message, never a stack trace ===");
global.fetch = async () => ({ ok: false, status: 500, json: async () => ({ code: "DASHBOARD_ERROR", message: "Unable to load AI dashboard.", retryable: true }) });
const vmError = new DashboardViewModel({});
const stateError = await vmError.load();
console.log(JSON.stringify(stateError, null, 2));
assert.strictEqual(stateError.state, LoadState.Error);
assert.strictEqual(stateError.message, "Unable to load AI dashboard.");
assert.strictEqual(stateError.canRetry, true);
console.log("PASS\n");

console.log("=== Test 5: Retry recovers from Error to Success ===");
mockFetchOnce(load("resp_dashboard_rich.json"));
const stateRetried = await vmError.retry();
assert.strictEqual(stateRetried.state, LoadState.Success);
console.log("PASS\n");

console.log("=== Test 6: Ask HisabDo — real grounded health answer ===");
const askHealth = load("resp_ask_health.json");
mockFetchOnce(askHealth);
const vmAsk = new DashboardViewModel({});
const askState1 = await vmAsk.ask("What is my financial health score?");
assert.strictEqual(askState1.state, LoadState.Success);
assert.ok(askState1.data.answer.includes("72/100"));
console.log("PASS\n");

console.log("=== Test 7: Ask HisabDo — missing-data answer maps to Empty, not Success ===");
const askMissing = load("resp_ask_missing_data.json");
mockFetchOnce(askMissing);
const askState2 = await vmAsk.ask("What budget do you recommend?");
console.log(JSON.stringify(askState2, null, 2));
assert.strictEqual(askState2.state, LoadState.Empty);
assert.ok(askState2.message.includes("No verified budget data"));
console.log("PASS\n");

console.log("=== Test 8: Ask HisabDo — empty question never calls the API ===");
let apiWasCalled = false;
global.fetch = async () => { apiWasCalled = true; return { ok: true, status: 200, json: async () => ({}) }; };
const askState3 = await vmAsk.ask("   ");
assert.strictEqual(askState3.state, LoadState.Error);
assert.strictEqual(askState3.canRetry, false);
assert.strictEqual(apiWasCalled, false, "a blank question must be rejected client-side, per spec — no API call needed");
console.log("PASS\n");

console.log("ALL CLIENT-SIDE TESTS PASSED (against real, executed API responses)");
