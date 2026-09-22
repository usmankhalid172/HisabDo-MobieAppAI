import { buildRecommendationsDisplay } from "./recommendations_client.js";
import assert from "node:assert";
import { readFileSync } from "node:fs";

function load(name) {
  return JSON.parse(readFileSync(new URL(`./${name}`, import.meta.url)));
}

console.log("=== Test 1: AtRisk user — multiple recommendations, deterministic order ===");
const atRisk = buildRecommendationsDisplay(load("resp_at_risk.json"));
console.log(JSON.stringify(atRisk, null, 2));
assert.strictEqual(atRisk.hasRecommendations, true);
assert.strictEqual(atRisk.recommendations.length, 10, "10 real recommendations were returned by the live server");
assert.strictEqual(atRisk.highCount, 6);
assert.strictEqual(atRisk.mediumCount, 3);
assert.strictEqual(atRisk.lowCount, 1);
assert.strictEqual(atRisk.recommendations[0].priority, "High", "list arrives already sorted High first, exactly as the engine orders it");
assert.strictEqual(atRisk.recommendations[atRisk.recommendations.length - 1].priority, "Low");
const cashFlow = atRisk.recommendations.find((r) => r.type === "CashFlowRisk");
assert.ok(cashFlow, "cash-flow risk recommendation must be present (expected saving is -5000)");
assert.strictEqual(cashFlow.typeLabel, "Cash-Flow Risk");
assert.ok(cashFlow.verifiedFigure.includes("negative"), "negative verified amount must be flagged, not silently shown as a plain positive number");
const foodUnusual = atRisk.recommendations.filter((r) => r.category === "Food" && r.type === "UnusualSpendingReview");
assert.strictEqual(foodUnusual.length, 2, "two independent verified sources (spending pattern + anomaly) both produce a Food UnusualSpendingReview — expected engine behavior, not a duplicate bug");
assert.strictEqual(atRisk.isInsufficientData, false);
console.log("PASS\n");

console.log("=== Test 2: Healthy user — genuinely empty, no limitations ===");
const healthy = buildRecommendationsDisplay(load("resp_healthy_empty.json"));
console.log(JSON.stringify(healthy, null, 2));
assert.strictEqual(healthy.hasRecommendations, false);
assert.deepStrictEqual(healthy.recommendations, []);
assert.strictEqual(healthy.hasLimitations, false);
assert.strictEqual(healthy.isInsufficientData, false, "empty because things are fine, not because data is missing");
console.log("PASS\n");

console.log("=== Test 3: InsufficientData user — empty, but WITH limitations ===");
const insufficient = buildRecommendationsDisplay(load("resp_insufficient_data.json"));
console.log(JSON.stringify(insufficient, null, 2));
assert.strictEqual(insufficient.hasRecommendations, false);
assert.strictEqual(insufficient.hasLimitations, true);
assert.strictEqual(insufficient.isInsufficientData, true, "must be distinguishable from the Healthy user's empty state");
console.log("PASS\n");

console.log("=== Test 4: Healthy vs. InsufficientData both render empty lists but are NOT the same UI state ===");
assert.notStrictEqual(healthy.isInsufficientData, insufficient.isInsufficientData);
console.log("PASS\n");

console.log("ALL CLIENT-SIDE TESTS PASSED (against real, executed API responses)");
