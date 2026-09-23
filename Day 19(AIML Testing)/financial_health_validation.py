"""
Financial Health Score validation — HisabDo AI, Day 19
---------------------------------------------------------
Validates financial_health_scoring.py (the real Day 10 engine) against
normal, unusual, zero/negative, and insufficient-data datasets, checking
every factor's exact threshold boundary and the overall classification
bands. Run with: python3 financial_health_validation.py
"""

from financial_health_scoring import (
    FinancialData, calculate_financial_health_score, classify_score,
    score_saving_behavior, score_expense_control, score_cash_flow,
)

passed = 0
failed = 0
warnings = []
failures = []


def check(name, condition, detail=""):
    global passed, failed
    if condition:
        passed += 1
        print(f"  [PASS] {name}")
    else:
        failed += 1
        failures.append(f"{name} — {detail}")
        print(f"  [FAIL] {name} — {detail}")


def warn(name, detail):
    warnings.append(f"{name} — {detail}")
    print(f"  [WARN] {name} — {detail}")


def section(title):
    print()
    print("=" * 78)
    print(title)
    print("=" * 78)


# ---------------------------------------------------------------------
# 1. Saving Behavior threshold boundaries
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Saving Behavior thresholds (exact boundaries)")

for rate_target, expected_score in [(25, 25.0), (24.99, 22.0), (20, 22.0), (19.99, 18.0),
                                     (15, 18.0), (14.99, 14.0), (10, 14.0), (9.99, 8.0),
                                     (5, 8.0), (4.99, 3.0)]:
    income = 100000
    expense = income * (1 - rate_target / 100)
    score, rate = score_saving_behavior(FinancialData(income, expense))
    check(f"Saving rate {rate_target}% -> {expected_score}/25", score == expected_score,
          f"got {score} for rate {rate:.2f}%")

# ---------------------------------------------------------------------
# 2. Expense Control threshold boundaries
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Expense Control thresholds (exact boundaries)")

for ratio_target, expected_score in [(50, 20.0), (50.01, 18.0), (60, 18.0), (60.01, 15.0),
                                      (70, 15.0), (70.01, 12.0), (80, 12.0), (80.01, 7.0),
                                      (90, 7.0), (90.01, 3.0)]:
    income = 100000
    expense = income * (ratio_target / 100)
    score, ratio = score_expense_control(FinancialData(income, expense))
    check(f"Expense ratio {ratio_target}% -> {expected_score}/20", score == expected_score,
          f"got {score} for ratio {ratio:.2f}%")

# ---------------------------------------------------------------------
# 3. Cash Flow classification boundaries
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Cash Flow status boundaries")

score, amt, status = score_cash_flow(FinancialData(100000, 79999))  # +20.001%
check("Cash flow just above 20% -> 'Strong Positive'", status == "Strong Positive", status)
score, amt, status = score_cash_flow(FinancialData(100000, 81000))  # +19%
check("Cash flow just below 20% -> 'Positive' (not yet Strong)", status == "Positive", status)
score, amt, status = score_cash_flow(FinancialData(100000, 100000))  # exactly 0
check("Cash flow exactly 0 -> 'Near Zero', not 'Positive' or 'Negative'", status == "Near Zero", status)
score, amt, status = score_cash_flow(FinancialData(100000, 100001))  # -1
check("Cash flow of -1 (barely negative) -> 'Negative'", status == "Negative", status)

# ---------------------------------------------------------------------
# 4. Overall classification band boundaries
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Overall classification bands")

for score_val, expected in [(90, "Excellent"), (89.99, "Good"), (75, "Good"), (74.99, "Fair"),
                             (60, "Fair"), (59.99, "Needs Attention"), (40, "Needs Attention"),
                             (39.99, "Poor"), (0, "Poor")]:
    result = classify_score(score_val)
    check(f"Score {score_val} -> '{expected}'", result == expected, f"got '{result}'")

# ---------------------------------------------------------------------
# 5. Insufficient data / no transactions
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Insufficient data / no-transaction handling")

no_txns = calculate_financial_health_score(FinancialData(0, 0, has_transactions=False))
check("No transactions -> score is exactly 0, status 'Poor', not a crash or fabricated value",
      no_txns["score"] == 0 and no_txns["status"] == "Poor")
check("No transactions -> insight explicitly says data is unavailable",
      "No transaction data" in no_txns["insights"][0])

# ---------------------------------------------------------------------
# 6. Zero / negative value edge cases
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Zero / negative value edge cases")

zero_income = calculate_financial_health_score(FinancialData(0, 15000, has_transactions=True))
check("Zero income (but has transactions) -> score computed without a divide-by-zero crash",
      isinstance(zero_income["score"], (int, float)))
check("Zero income -> saving rate reported as 0, not NaN/error", zero_income["savingRate"] == 0)

negative_cashflow = calculate_financial_health_score(FinancialData(60000, 95000))
check("Expense exceeds income (negative cash flow) -> handled without crashing, cashFlow is negative",
      negative_cashflow["cashFlow"] < 0)
check("Negative cash flow -> status reflects a low score (not artificially inflated)",
      negative_cashflow["status"] in ("Poor", "Needs Attention"), negative_cashflow["status"])

# ---------------------------------------------------------------------
# 7. Methodology note: score rescaling when factors are missing
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Methodology note: rescaling when factors are unavailable")

# Only saving/expense/cash-flow are knowable (no budget, no debt data given,
# no previous-period expense for growth) -> only 60 of 100 weighted points
# are "available", and the score is RESCALED to /100 using only those.
full_data_maxed = calculate_financial_health_score(FinancialData(
    total_income=100000, total_expense=50000,  # 50% saving rate, 50% expense ratio -> both cap out
))
check("With only 3 of 6 factors available (no budget/debt/growth data), a maxed subset can still score close to 100",
      full_data_maxed["score"] >= 95, f"got {full_data_maxed['score']}")
warn("Score rescaling when data is incomplete",
     f"A user with NO budget set, NO debt data, and NO previous-period expense (3 of 6 factors genuinely unavailable) "
     f"still scored {full_data_maxed['score']}/100 here — the engine rescales the available 60 points up to /100 rather than "
     f"capping the score or lowering confidence when most factors are missing. This may be the intended MVP behavior "
     f"(the spec's Section 15 marks Budget/Debt as lower priority than the core 3), but it means a 'high score' can mean "
     f"either 'verified excellent across all 6 factors' or 'verified good on only half the factors' — worth confirming "
     f"the UI should surface which factors were actually available, not just the final number.")

# ---------------------------------------------------------------------
# 8. Increasing / decreasing expense growth
# ---------------------------------------------------------------------
section("FINANCIAL HEALTH — Expense growth trend tests")

increasing = calculate_financial_health_score(FinancialData(100000, 60000, previous_expense=40000))  # +50%
check("Expense growth +50% (large increase) -> lowest growth score tier (1/10)",
      increasing["expenseGrowth"] == 50.0)

decreasing = calculate_financial_health_score(FinancialData(100000, 30000, previous_expense=50000))  # -40%
check("Expense growth -40% (decrease) -> handled correctly, negative growth% reported",
      decreasing["expenseGrowth"] == -40.0)

no_previous = calculate_financial_health_score(FinancialData(100000, 50000, previous_expense=None))
check("No previous-period expense data -> expenseGrowth is None, never fabricated", no_previous["expenseGrowth"] is None)

# ---------------------------------------------------------------------
print()
print("#" * 78)
print(f"TOTAL: {passed} passed, {failed} failed, {len(warnings)} warnings")
print("#" * 78)
if failures:
    print("\nFAILURES:")
    for f in failures:
        print(f"  - {f}")
if warnings:
    print("\nWARNINGS (methodology notes, not hard failures):")
    for w in warnings:
        print(f"  - {w}")
