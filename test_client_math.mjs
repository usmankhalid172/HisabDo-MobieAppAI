import { buildDisplayPayload } from "./spending_intelligence_client.js";
import assert from "node:assert";

// Ground truth from reference_port.py (verified, byte-for-byte port of the
// uploaded BudgetAnalysisService.cs / ForecastingService.cs logic)
const ANALYSIS_READY = {
  userId: "11111111-1111-1111-1111-111111111111", from: "2026-01-01", to: "2026-05-31",
  status: "ready", monthsWithData: 5, totalIncome: 26000, totalExpense: 16500,
  categories: [
    { category: "Food", averageMonthlySpend: 0, currentBudget: 700, utilizationPercent: 0, recommendedBudget: 0, needsAdjustment: true, adjustmentReason: "Budget is materially underused." },
    { category: "Housing", averageMonthlySpend: 0, currentBudget: 1500, utilizationPercent: 0, recommendedBudget: 0, needsAdjustment: true, adjustmentReason: "Budget is materially underused." },
    { category: "Living", averageMonthlySpend: 3300, currentBudget: 0, utilizationPercent: 100, recommendedBudget: 3630, needsAdjustment: true, adjustmentReason: "No budget exists." },
    { category: "Transport", averageMonthlySpend: 0, currentBudget: 400, utilizationPercent: 0, recommendedBudget: 0, needsAdjustment: true, adjustmentReason: "Budget is materially underused." },
  ],
};

const FORECAST_READY = {
  userId: "11111111-1111-1111-1111-111111111111", frequency: "Monthly",
  historical: [
    { period: "2026-01", income: 5000, expense: 3200, savings: 1800, openingBalance: 10000, closingBalance: 11800 },
    { period: "2026-02", income: 5100, expense: 3300, savings: 1800, openingBalance: 11800, closingBalance: 13600 },
    { period: "2026-03", income: 5200, expense: 3100, savings: 2100, openingBalance: 13600, closingBalance: 15700 },
    { period: "2026-04", income: 5300, expense: 3400, savings: 1900, openingBalance: 15700, closingBalance: 17600 },
    { period: "2026-05", income: 5400, expense: 3500, savings: 1900, openingBalance: 17600, closingBalance: 19500 },
  ],
  expected: [{ period: "Forecast-1", income: 5200, expense: 3300, savings: 1900, openingBalance: null, closingBalance: null }],
};

const ANALYSIS_INSUFFICIENT = {
  userId: "11111111-1111-1111-1111-111111111111", from: "2026-01-01", to: "2026-02-28",
  status: "insufficient_data", monthsWithData: 2, totalIncome: 10100, totalExpense: 6500, categories: [],
};

console.log("=== Test 1: Normal ready state ===");
const normal = buildDisplayPayload(ANALYSIS_READY, FORECAST_READY);
console.log(JSON.stringify(normal, null, 2));
assert.strictEqual(normal.isEmpty, false, "should not be empty");
assert.strictEqual(normal.totalSpending.amount, 16500, "total spending must match totalExpense");
assert.strictEqual(normal.topCategories[0].category, "Living", "Living must rank #1 (it's the only category with actual spend)");
assert.strictEqual(normal.topCategories[0].amount, 16500, "Living's recovered total must equal averageMonthlySpend(3300) * months(5)");
assert.strictEqual(normal.topCategories[0].percentage, 100, "Living is 100% of total spending in this fixture");
assert.strictEqual(normal.topCategories[1].amount, 0, "Housing/Food/Transport have zero recovered spend (budgeted but unused)");
assert.strictEqual(normal.trendAvailable, true, "forecast historical data must produce a trend");
assert.strictEqual(normal.monthlyTrend.length, 5, "5 historical months expected");
assert.strictEqual(normal.recurring.available, false, "recurring must be honestly reported as unavailable");
assert.strictEqual(normal.aiInsightIsFallback, true, "insight must be marked as a fallback, never presented as real AI");
console.log("PASS\n");

console.log("=== Test 2: Insufficient data (empty state) ===");
const empty = buildDisplayPayload(ANALYSIS_INSUFFICIENT, null);
console.log(JSON.stringify(empty, null, 2));
assert.strictEqual(empty.isEmpty, true, "status=insufficient_data must map to isEmpty=true");
assert.strictEqual(empty.totalSpending.amount, 6500, "empty state must still show totalExpense if backend returned one, not force 0");
console.log("PASS\n");

console.log("=== Test 3: Forecast unavailable, categories still shown ===");
const noForecast = buildDisplayPayload(ANALYSIS_READY, null);
assert.strictEqual(noForecast.isEmpty, false);
assert.strictEqual(noForecast.trendAvailable, false, "null forecast must not crash, must set trendAvailable=false");
assert.strictEqual(noForecast.topCategories[0].category, "Living", "category ranking must not depend on forecast data");
console.log("PASS\n");

console.log("ALL CLIENT-SIDE MATH TESTS PASSED");
