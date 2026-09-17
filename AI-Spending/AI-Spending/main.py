from google import genai
from dotenv import load_dotenv
import os
import json

# Load environment variables
load_dotenv()

# Get Gemini API key
api_key = os.getenv("GEMINI_API_KEY")

# Create Gemini client
client = genai.Client(api_key=api_key)

# Verified spending data from backend
spending_data = {
    "total_spending": 85000,
    "previous_month_total": 70000,
    "categories": {
        "Food": 25000,
        "Shopping": 20000,
        "Transport": 15000,
        "Bills": 15000,
        "Entertainment": 10000
    }
}

# Prompt for AI explanation
prompt = f"""
You are a financial spending explanation assistant.

Analyze ONLY the following VERIFIED spending data:

{json.dumps(spending_data, indent=2)}

Rules:
- Use ONLY the data provided above.
- Do not invent or assume financial numbers.
- Do not create categories that are not provided.
- Do not claim category-level increases or decreases because previous month
  category data is unavailable.
- Calculate percentages only from the provided numbers.
- Generate clear and natural-language spending insights.

Return ONLY valid JSON in exactly this structure:

{{
  "spending_summary": "",
  "top_spending_categories": [
    {{
      "category": "",
      "amount": 0,
      "percentage": 0
    }}
  ],
  "significant_changes": [],
  "key_insights": [],
  "actionable_observations": []
}}
"""

# Generate AI explanation
response = client.models.generate_content(
    model="gemini-3.6-flash",
    contents=prompt
)

# Display the AI response
print("===== AI SPENDING EXPLANATION =====")

try:
    clean_response = response.text.strip()

    # Remove Markdown code fences if Gemini adds them
    if clean_response.startswith("```json"):
        clean_response = clean_response[7:]
    elif clean_response.startswith("```"):
        clean_response = clean_response[3:]

    if clean_response.endswith("```"):
        clean_response = clean_response[:-3]

    result = json.loads(clean_response.strip())

    print(json.dumps(result, indent=2))

except json.JSONDecodeError:
    print("AI returned an invalid JSON response:")
    print(response.text)