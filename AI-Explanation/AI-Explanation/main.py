from ai_explanation import generate_financial_explanation


financial_data = {
    "company_name": "ABC Technologies",
    "revenue": 10000000,
    "profit": 1800000,
    "expenses": 8200000,
    "revenue_growth": 15,
    "profit_growth": 10,
    "financial_score": 78
}


explanation = generate_financial_explanation(financial_data)

print("\n===== AI FINANCIAL EXPLANATION =====\n")

print("Summary:")
print(explanation["overall_summary"])

print("\nStrengths:")
for strength in explanation["strengths"]:
    print("-", strength)

print("\nWeaknesses:")
for weakness in explanation["weaknesses"]:
    print("-", weakness)

print("\nKey Insight:")
print(explanation["key_insight"])