import { buildBudgetRecDisplay } from "./budget_recommendations_client.js";
import assert from "node:assert";
import { readFileSync } from "node:fs";

const normal = JSON.parse(readFileSync(new URL("./real_response_normal.json", import.meta.url)));
const empty = JSON.parse(readFileSync(new URL("./real_response_empty.json", import.meta.url)));
const limited = JSON.parse(readFileSync(new URL("./real_response_limited.json", import.meta.url)));

console.log("=== Test 1: Normal (5 categories, real captured response) ===");
const d1 = buildBudgetRecDisplay(normal);
console.log(JSON.stringify(d1, null, 2));
assert.strictEqual(d1.hasRecommendations, true);
assert.strictEqual(d1.categories.length, 5);
assert.deepStrictEqual(d1.overspendingCategories.sort(), ["Entertainment", "Food"]);
assert.strictEqual(d1.hasOverspending, true);
const food = d1.categories.find((c) => c.category === "Food");
assert.strictEqual(food.isOverspending, true);
assert.strictEqual(food.recommendedBudget, "Rs 15,000");
assert.strictEqual(food.varianceDirection, "over");
const shopping = d1.categories.find((c) => c.category === "Shopping");
assert.strictEqual(shopping.existingBudget, "No existing budget");
assert.strictEqual(shopping.utilization, "Not available", "no existing budget means no utilization %");
const entertainment = d1.categories.find((c) => c.category === "Entertainment");
assert.strictEqual(entertainment.utilization, "Not available", "existing budget of 0 must not divide-by-zero");
console.log("PASS\n");

console.log("=== Test 2: Empty (no transactions, real captured response) ===");
const d2 = buildBudgetRecDisplay(empty);
console.log(JSON.stringify(d2, null, 2));
assert.strictEqual(d2.hasRecommendations, false);
assert.deepStrictEqual(d2.categories, []);
assert.strictEqual(d2.hasOverspending, false);
console.log("PASS\n");

console.log("=== Test 3: Limited window (1 month requested, real captured response) ===");
const d3 = buildBudgetRecDisplay(limited);
console.log(JSON.stringify(d3.categories.map((c) => ({ category: c.category, isLimitedData: c.isLimitedData })), null, 2));
assert.ok(d3.categories.every((c) => c.isLimitedData), "every category must be flagged Limited when the requested window is 1 month");
console.log("PASS\n");

console.log("ALL CLIENT-SIDE TESTS PASSED (against real, executed API responses)");
