"""
Financial Health Score — HisabDo AI, Day 10
---------------------------------------------
Implements the exact scoring model from:
"HisabDo AI — Day 10 — Financial Health Score: Professional MVP
Specification & Implementation Guide"

6 weighted factors (total = 100 points):
Saving Behavior   -> 25
Expense Control   -> 20
Budget Control    -> 20
Cash Flow         -> 15
Debt / Udhaar     -> 10
Expense Growth    -> 10

Golden Rule (Section 14): All financial totals, percentages, scores and
forecast values must come from this deterministic calculation engine.
No AI/LLM-invented numbers. This module produces verified values only;
an LLM layer may explain them afterwards but must never override them.

NOTE: This file is a local copy of financial_health_scoring.py from the
usmankhalid172/MedNoviAI-AI repo (authored by Omesh Lakhani), reproduced
here only so integration + edge-case tests can be run and verified.
"""

from dataclasses import dataclass
from typing import List, Optional


# ---------------------------------------------------------------------
# 1. INPUT DATA MODEL (Section 15 — Required HisabDo Data)
# ---------------------------------------------------------------------
@dataclass
class FinancialData:
    total_income: float
    total_expense: float
    budget: Optional[float] = None
    previous_expense: Optional[float] = None
    debt_amount: Optional[float] = None
    has_transactions: bool = True


# ---------------------------------------------------------------------
# 2. FACTOR SCORING — exact tables from the spec (Sections 5-10)
# ---------------------------------------------------------------------
def score_saving_behavior(data: FinancialData) -> tuple:
    if data.total_income <= 0:
        return 0.0, 0.0
    saving_rate = ((data.total_income - data.total_expense) / data.total_income) * 100
    if saving_rate >= 25:
        return 25.0, saving_rate
    elif saving_rate >= 20:
        return 22.0, saving_rate
    elif saving_rate >= 15:
        return 18.0, saving_rate
    elif saving_rate >= 10:
        return 14.0, saving_rate
    elif saving_rate >= 5:
        return 8.0, saving_rate
    else:
        return 3.0, saving_rate


def score_expense_control(data: FinancialData) -> tuple:
    if data.total_income <= 0:
        return 0.0, 0.0
    expense_ratio = (data.total_expense / data.total_income) * 100
    if expense_ratio <= 50:
        return 20.0, expense_ratio
    elif expense_ratio <= 60:
        return 18.0, expense_ratio
    elif expense_ratio <= 70:
        return 15.0, expense_ratio
    elif expense_ratio <= 80:
        return 12.0, expense_ratio
    elif expense_ratio <= 90:
        return 7.0, expense_ratio
    else:
        return 3.0, expense_ratio


def score_budget_control(data: FinancialData) -> tuple:
    if data.budget is None or data.budget <= 0:
        return None, None, "No Budget Set"
    utilization = (data.total_expense / data.budget) * 100
    if utilization <= 100:
        return 20.0, utilization, "Within Budget"
    elif utilization <= 105:
        return 17.0, utilization, "Slightly Over Budget"
    elif utilization <= 115:
        return 13.0, utilization, "Over Budget"
    elif utilization <= 125:
        return 8.0, utilization, "Significantly Over Budget"
    else:
        return 3.0, utilization, "Far Over Budget"


def score_cash_flow(data: FinancialData) -> tuple:
    cash_flow = data.total_income - data.total_expense
    if data.total_income <= 0:
        return 0.0, cash_flow, "Unknown (no income)"
    cash_flow_pct = (cash_flow / data.total_income) * 100
    if cash_flow_pct >= 20:
        return 15.0, cash_flow, "Strong Positive"
    elif cash_flow_pct > 0:
        return 12.0, cash_flow, "Positive"
    elif cash_flow_pct == 0:
        return 7.0, cash_flow, "Near Zero"
    else:
        return 3.0, cash_flow, "Negative"


def score_debt_udhaar(data: FinancialData) -> tuple:
    if data.debt_amount is None:
        return 10.0, "No Debt Data"
    if data.total_income <= 0:
        return 0.0, "Unknown (no income)"
    debt_ratio = (data.debt_amount / data.total_income) * 100
    if debt_ratio <= 5:
        return 10.0, "None / Very Low"
    elif debt_ratio <= 20:
        return 8.0, "Low"
    elif debt_ratio <= 40:
        return 6.0, "Moderate"
    elif debt_ratio <= 70:
        return 3.0, "High"
    else:
        return 1.0, "Very High"


def score_expense_growth(data: FinancialData) -> tuple:
    if data.previous_expense is None or data.previous_expense <= 0:
        return None, None
    growth_pct = ((data.total_expense - data.previous_expense) / data.previous_expense) * 100
    if growth_pct < 0:
        return 10.0, growth_pct
    elif growth_pct <= 5:
        return 8.0, growth_pct
    elif growth_pct <= 10:
        return 6.0, growth_pct
    elif growth_pct <= 20:
        return 3.0, growth_pct
    else:
        return 1.0, growth_pct


# ---------------------------------------------------------------------
# 3. CLASSIFICATION (Section 12)
# ---------------------------------------------------------------------
def classify_score(score: float) -> str:
    if score >= 90:
        return "Excellent"
    elif score >= 75:
        return "Good"
    elif score >= 60:
        return "Fair"
    elif score >= 40:
        return "Needs Attention"
    else:
        return "Poor"


# ---------------------------------------------------------------------
# 4. INSIGHTS
# ---------------------------------------------------------------------
def generate_insights(saving_rate, expense_ratio, budget_status,
                       cash_flow_status, growth_pct) -> List[str]:
    insights = []
    if saving_rate >= 20:
        insights.append("Saving behavior is healthy")
    elif saving_rate < 10:
        insights.append("Saving rate is low and needs improvement")
    if expense_ratio > 80:
        insights.append("Expenses are consuming a very large share of income")
    if budget_status and budget_status not in ("Within Budget", "No Budget Set"):
        insights.append("Spending has exceeded the planned budget")
    if cash_flow_status == "Negative":
        insights.append("Cash flow is negative this period")
    if growth_pct is not None and growth_pct > 10:
        insights.append("Expenses increased significantly compared with the previous period")
    elif growth_pct is not None and growth_pct < 0:
        insights.append("Expenses decreased compared with the previous period")
    if not insights:
        insights.append("Overall financial behavior is stable")
    return insights


# ---------------------------------------------------------------------
# 5. MAIN ENTRY POINT
# ---------------------------------------------------------------------
def calculate_financial_health_score(data: FinancialData) -> dict:
    if not data.has_transactions:
        return {
            "score": 0,
            "status": "Poor",
            "savingRate": 0,
            "expenseRatio": 0,
            "budgetStatus": "No Data",
            "cashFlow": 0,
            "expenseGrowth": None,
            "insights": ["No transaction data available for this period"],
        }

    saving_score, saving_rate = score_saving_behavior(data)
    expense_score, expense_ratio = score_expense_control(data)
    budget_score, budget_util, budget_status = score_budget_control(data)
    cash_score, cash_flow_amount, cash_flow_status = score_cash_flow(data)
    debt_score, debt_label = score_debt_udhaar(data)
    growth_score, growth_pct = score_expense_growth(data)

    components = {
        "saving_behavior": (saving_score, 25),
        "expense_control": (expense_score, 20),
        "budget_control": (budget_score, 20),
        "cash_flow": (cash_score, 15),
        "debt_udhaar": (debt_score, 10),
        "expense_growth": (growth_score, 10),
    }

    available = {k: v for k, v in components.items() if v[0] is not None}
    available_weight = sum(w for _, w in available.values())

    if available_weight == 0:
        final_score = 0.0
    else:
        raw_total = sum(score for score, _ in available.values())
        final_score = (raw_total / available_weight) * 100
    final_score = round(final_score, 2)

    status = classify_score(final_score)
    insights = generate_insights(
        saving_rate, expense_ratio, budget_status, cash_flow_status, growth_pct
    )

    return {
        "score": final_score,
        "status": status,
        "savingRate": round(saving_rate, 2),
        "expenseRatio": round(expense_ratio, 2),
        "budgetStatus": budget_status,
        "cashFlow": round(cash_flow_amount, 2),
        "expenseGrowth": round(growth_pct, 2) if growth_pct is not None else None,
        "insights": insights,
    }
