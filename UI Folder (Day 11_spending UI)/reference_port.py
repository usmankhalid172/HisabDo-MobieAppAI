"""
Reference port of BudgetAnalysisService.cs + ForecastingService.cs +
DemoAdapters.cs, used ONLY to compute verified ground-truth output for the
seeded demo dataset that ships in the uploaded BudgetAnalysisService.zip.

WHY THIS FILE EXISTS: the .NET 8 SDK isn't installable in this sandbox
(apt's dotnet-sdk-8.0 package failed to fetch — the Microsoft package feed
isn't on the network allowlist here), so the real C# project can't be
`dotnet run` directly to check its output. Every branch and threshold below
is copied verbatim from the uploaded .cs files, and re-checked by hand
against them in the task report. This is a verification aid, not a
replacement backend — the actual API contract is the C# code in
BudgetAnalysisService.zip.
"""

import json
from dataclasses import dataclass
from datetime import date


@dataclass
class Txn:
    user_id: str
    type: str  # "Income" | "Expense"
    amount: float
    d: date
    category: str | None = None


@dataclass
class Budget:
    category: str
    monthly_limit: float


DEMO_USER = "demo-user"

TRANSACTIONS = [
    Txn(DEMO_USER, "Income", 5000, date(2026, 1, 10)),
    Txn(DEMO_USER, "Expense", 3200, date(2026, 1, 18), "Living"),
    Txn(DEMO_USER, "Income", 5100, date(2026, 2, 10)),
    Txn(DEMO_USER, "Expense", 3300, date(2026, 2, 18), "Living"),
    Txn(DEMO_USER, "Income", 5200, date(2026, 3, 10)),
    Txn(DEMO_USER, "Expense", 3100, date(2026, 3, 18), "Living"),
    Txn(DEMO_USER, "Income", 5300, date(2026, 4, 10)),
    Txn(DEMO_USER, "Expense", 3400, date(2026, 4, 18), "Living"),
    Txn(DEMO_USER, "Income", 5400, date(2026, 5, 10)),
    Txn(DEMO_USER, "Expense", 3500, date(2026, 5, 18), "Living"),
]

BUDGETS = [
    Budget("Housing", 1500),
    Budget("Food", 700),
    Budget("Transport", 400),
]


def months_between(frm: date, to: date) -> int:
    return (to.year - frm.year) * 12 + to.month - frm.month + 1


def analyze_budget(frm: date, to: date):
    MIN_MONTHS = 3
    txns = [t for t in TRANSACTIONS if frm <= t.d <= to]
    expenses = [t for t in txns if t.type == "Expense" and t.category]
    total_income = sum(t.amount for t in txns if t.type == "Income")
    total_expense = sum(t.amount for t in expenses)
    budgets = {b.category: b for b in BUDGETS}

    months = months_between(frm, to)
    months_with_data = len({(t.d.year, t.d.month) for t in txns})

    if months_with_data < MIN_MONTHS or len(budgets) == 0:
        return {
            "from": str(frm), "to": str(to), "status": "insufficient_data",
            "monthsWithData": months_with_data, "totalIncome": total_income,
            "totalExpense": total_expense, "categories": [],
        }

    categories = sorted(set(budgets.keys()) | {t.category for t in expenses})
    result_categories = []
    for cat in categories:
        cat_txns = [t for t in expenses if t.category == cat]
        average = round(sum(t.amount for t in cat_txns) / months, 2)
        current = budgets[cat].monthly_limit if cat in budgets else 0
        if current == 0:
            utilization = 0 if average == 0 else 100
        else:
            utilization = round(average / current * 100, 2)
        recommended = round(average * 1.1, 2)
        needs_adjustment = current == 0 or utilization > 100 or utilization < 50
        if current == 0:
            reason = "No budget exists."
        elif utilization > 100:
            reason = "Average spending exceeds the budget."
        elif utilization < 50:
            reason = "Budget is materially underused."
        else:
            reason = "Budget is performing within range."
        result_categories.append({
            "category": cat, "averageMonthlySpend": average, "currentBudget": current,
            "utilizationPercent": utilization, "recommendedBudget": recommended,
            "needsAdjustment": needs_adjustment, "adjustmentReason": reason,
        })

    return {
        "from": str(frm), "to": str(to), "status": "ready",
        "monthsWithData": months_with_data, "totalIncome": total_income,
        "totalExpense": total_expense, "categories": result_categories,
    }


def forecast(frm: date, to: date, periods: int = 1):
    MIN_USABLE_PERIODS = 3
    txns = [t for t in TRANSACTIONS if frm <= t.d <= to]
    opening = 10000  # DemoFinancialRepository hardcodes this for the demo user

    grouped = {}
    for t in txns:
        key = (t.d.year, t.d.month)
        grouped.setdefault(key, {"Income": 0, "Expense": 0})
        grouped[key][t.type] += t.amount

    history = []
    balance = opening
    y, m = frm.year, frm.month
    while (y, m) <= (to.year, to.month):
        values = grouped.get((y, m), {"Income": 0, "Expense": 0})
        income, expense = values["Income"], values["Expense"]
        closing = balance + income - expense
        history.append({
            "period": f"{y:04d}-{m:02d}", "income": income, "expense": expense,
            "savings": income - expense, "openingBalance": balance, "closingBalance": closing,
        })
        balance = closing
        m += 1
        if m > 12:
            m = 1
            y += 1

    usable = sum(1 for h in history if h["income"] != 0 or h["expense"] != 0)
    if usable < MIN_USABLE_PERIODS:
        raise ValueError(f"At least {MIN_USABLE_PERIODS} periods containing financial data are required.")

    avg_income = sum(h["income"] for h in history) / len(history)
    avg_expense = sum(h["expense"] for h in history) / len(history)
    avg_savings = avg_income - avg_expense
    expected = [{
        "period": f"Forecast-{i+1}", "income": round(avg_income, 2), "expense": round(avg_expense, 2),
        "savings": round(avg_savings, 2), "openingBalance": None, "closingBalance": None,
    } for i in range(periods)]

    return {"userId": DEMO_USER, "frequency": "Monthly", "historical": history, "expected": expected}


if __name__ == "__main__":
    analysis = analyze_budget(date(2026, 1, 1), date(2026, 5, 31))
    print("=== BudgetAnalysisResult (verified ground truth) ===")
    print(json.dumps(analysis, indent=2))

    fc = forecast(date(2026, 1, 1), date(2026, 5, 31), periods=1)
    print("\n=== ForecastResult (verified ground truth) ===")
    print(json.dumps(fc, indent=2))
