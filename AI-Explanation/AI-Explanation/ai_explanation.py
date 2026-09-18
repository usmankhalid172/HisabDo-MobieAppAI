import os
import json
from dotenv import load_dotenv
from google import genai

load_dotenv()

api_key = os.getenv("GEMINI_API_KEY")

if not api_key:
    raise ValueError("GEMINI_API_KEY is not set. Please create a .env file.")

client = genai.Client(api_key=api_key)


def generate_financial_explanation(data):

    prompt = f"""
You are a financial explanation assistant.

Use ONLY the verified financial data provided below.

IMPORTANT RULES:
- Do not change any financial figures.
- Do not invent financial figures.
- Do not calculate a new financial score.
- The financial score provided by the backend is the official score.
- Explain the information in simple language.
- Do not make assumptions about financial trends that are not explicitly provided.
- Do not infer expense growth from revenue growth or profit growth.
- Only make claims that can be directly supported by the provided data.

Verified Financial Data:

Company: {data['company_name']}
Revenue: {data['revenue']}
Profit: {data['profit']}
Expenses: {data['expenses']}
Revenue Growth: {data['revenue_growth']}%
Profit Growth: {data['profit_growth']}%
Financial Score: {data['financial_score']}

Return the response as JSON with exactly these fields:

{{
    "overall_summary": "A simple explanation of the company's overall financial performance.",
    "strengths": [
        "First strength",
        "Second strength"
    ],
    "weaknesses": [
        "First weakness",
        "Second weakness"
    ],
    "key_insight": "One important insight based only on the verified data."
}}

Do not include any text outside the JSON.
"""

    response = client.interactions.create(
        model="gemini-3.6-flash",
        input=prompt
    )

    result = json.loads(response.output_text)

    return result