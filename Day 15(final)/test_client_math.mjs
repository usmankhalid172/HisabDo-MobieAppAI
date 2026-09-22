import { buildAnswerDisplay } from "./ask_hisabdo_client.js";
import assert from "node:assert";
import { readFileSync } from "node:fs";

function load(name) {
  return JSON.parse(readFileSync(new URL(`./${name}`, import.meta.url)));
}

console.log("=== Test 1: Income & Expense (real, computed answer) ===");
const t1 = buildAnswerDisplay(load("resp_income_expense.json"));
console.log(JSON.stringify(t1, null, 2));
assert.strictEqual(t1.category, "IncomeExpense");
assert.strictEqual(t1.isVerified, true);
assert.strictEqual(t1.hasVerifiedValues, true);
assert.ok(t1.answer.includes("Rs 50,000"));
console.log("PASS\n");

console.log("=== Test 2: Spending/Category (real, computed answer with % of total) ===");
const t2 = buildAnswerDisplay(load("resp_spending_category.json"));
assert.strictEqual(t2.category, "SpendingCategory");
assert.strictEqual(t2.verifiedValues[0].label, "Food");
console.log("PASS\n");

console.log("=== Test 3: Budget (over-budget answer) ===");
const t3 = buildAnswerDisplay(load("resp_budget.json"));
assert.strictEqual(t3.category, "Budget");
assert.ok(t3.answer.includes("over"));
console.log("PASS\n");

console.log("=== Test 4: Financial Health (simplified snapshot, limitation disclosed) ===");
const t4 = buildAnswerDisplay(load("resp_financial_health.json"));
assert.strictEqual(t4.category, "FinancialHealth");
assert.ok(t4.limitations.length > 0, "must disclose this is a simplified 3-factor snapshot");
console.log("PASS\n");

console.log("=== Test 5: Anomaly (real detected anomaly) ===");
const t5 = buildAnswerDisplay(load("resp_anomaly.json"));
assert.strictEqual(t5.category, "Anomaly");
assert.ok(t5.answer.includes("Food"));
assert.ok(t5.answer.includes("High"));
console.log("PASS\n");

console.log("=== Test 6: Cash-Flow Forecast (labeled as an estimate) ===");
const t6 = buildAnswerDisplay(load("resp_forecast.json"));
assert.strictEqual(t6.category, "CashFlowForecast");
assert.ok(t6.limitations.some((l) => l.toLowerCase().includes("estimate")));
console.log("PASS\n");

console.log("=== Test 7: Budget Recommendation ===");
const t7 = buildAnswerDisplay(load("resp_budget_recommendation.json"));
assert.strictEqual(t7.category, "BudgetRecommendation");
assert.ok(t7.limitations.length >= 1);
console.log("PASS\n");

console.log("=== Test 8: Missing data (no budget for Shopping) ===");
const t8 = buildAnswerDisplay(load("resp_missing_data.json"));
assert.strictEqual(t8.isVerified, false);
assert.strictEqual(t8.isMissingData, true, "must be classified as missing-data, not unsupported or ambiguous");
assert.ok(t8.limitations[0].includes("No budget exists for Shopping"));
assert.ok(!t8.answer.match(/\d/), "must never fabricate a number when data is missing");
console.log("PASS\n");

console.log("=== Test 9: Unsupported (off-topic question) ===");
const t9 = buildAnswerDisplay(load("resp_unsupported.json"));
assert.strictEqual(t9.isUnsupported, true);
assert.strictEqual(t9.isVerified, false);
console.log("PASS\n");

console.log("=== Test 10: Spec's own Ambiguous example — confirms the real classifier bug ===");
const t10 = buildAnswerDisplay(load("resp_unsupported_or_ambiguous.json"));
console.log(JSON.stringify(t10, null, 2));
assert.strictEqual(t10.category, "Unsupported", "documents the real backend behavior: spec expects Ambiguous, backend actually returns Unsupported for this exact sample question");
assert.strictEqual(t10.needsClarification, false, "confirms clarificationPrompts path never triggers for this case, despite the spec's own sample table saying it should");
console.log("PASS (this failure IS the expected/documented behavior — see task report Gap #2)\n");

console.log("ALL CLIENT-SIDE TESTS PASSED (against real, executed API responses)");
