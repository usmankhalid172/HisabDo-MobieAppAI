import os
from decimal import Decimal
from typing import Any

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from google import genai

app = FastAPI(
    title="HisabDo AI Explanation Service",
    version="1.0.0",
)

GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
if not GEMINI_API_KEY:
    raise RuntimeError("GEMINI_API_KEY is required")

client = genai.Client(api_key=GEMINI_API_KEY)

SYSTEM_PROMPT = """
You are the HisabDo AI explanation layer.

STRICT RULES:
1. Financial calculations are performed by the .NET backend.
2. Use only values present in the supplied verified context.
3. Never invent an amount, percentage, score, category, forecast,
   priority, severity, recommendation, or expected benefit.
4. Do not recalculate financial values.
5. Do not create a new financial recommendation.
6. If information is missing, clearly say that the data is insufficient.
7. Keep the answer simple and useful.
8. Do not expose internal implementation details to the user.
9. Treat IsVerified=false records as unusable.
"""

class Recommendation(BaseModel):
    id: str
    type: str
    summary: str
    reason: str
    action: str
    priority: str
    severity: str | None = None
    expectedBenefit: Decimal | None = None
    expectedBenefitVerified: bool
    isVerified: bool

class SpendingPattern(BaseModel):
    category: str
    currentAmount: Decimal
    previousAmount: Decimal | None = None
    changePercent: Decimal | None = None
    direction: str
    isVerified: bool

class Anomaly(BaseModel):
    id: str
    type: str
    category: str
    amount: Decimal
    severity: str
    reason: str
    isVerified: bool

class BudgetRecommendation(BaseModel):
    category: str
    existingBudget: Decimal | None = None
    recommendedBudget: Decimal
    historicalAverage: Decimal
    reason: str
    isVerified: bool

class Forecast(BaseModel):
    period: str
    forecastIncome: Decimal | None = None
    forecastExpense: Decimal | None = None
    expectedSaving: Decimal | None = None
    expectedClosingBalance: Decimal | None = None
    confidence: str
    isVerified: bool

class FinancialContext(BaseModel):
    userId: str
    healthScore: Decimal | None = None
    savingRate: Decimal | None = None
    expenseGrowthPercent: Decimal | None = None
    cashFlow: Decimal | None = None
    cashFlowRisk: str | None = None
    recommendations: list[Recommendation] = Field(default_factory=list)
    spendingPatterns: list[SpendingPattern] = Field(default_factory=list)
    anomalies: list[Anomaly] = Field(default_factory=list)
    budgetRecommendations: list[BudgetRecommendation] = Field(default_factory=list)
    forecasts: list[Forecast] = Field(default_factory=list)
    limitations: str | None = None

class ExplainRequest(BaseModel):
    userId: str
    question: str
    context: FinancialContext

class ExplainResponse(BaseModel):
    answer: str
    isVerified: bool
    limitations: list[str]

def verified_context(context: FinancialContext) -> dict[str, Any]:
    """Remove any records that are not explicitly verified."""
    return {
        "healthScore": context.healthScore,
        "savingRate": context.savingRate,
        "expenseGrowthPercent": context.expenseGrowthPercent,
        "cashFlow": context.cashFlow,
        "cashFlowRisk": context.cashFlowRisk,
        "recommendations": [
            x.model_dump() for x in context.recommendations if x.isVerified
        ],
        "spendingPatterns": [
            x.model_dump() for x in context.spendingPatterns if x.isVerified
        ],
        "anomalies": [
            x.model_dump() for x in context.anomalies if x.isVerified
        ],
        "budgetRecommendations": [
            x.model_dump()
            for x in context.budgetRecommendations
            if x.isVerified
        ],
        "forecasts": [
            x.model_dump() for x in context.forecasts if x.isVerified
        ],
        "limitations": context.limitations,
    }

@app.get("/health")
async def health():
    return {"status": "healthy", "service": "hisabdo-ai"}

@app.post("/api/ai/explain", response_model=ExplainResponse)
async def explain(request: ExplainRequest):
    if request.userId != request.context.userId:
        raise HTTPException(
            status_code=403,
            detail="User ID does not match financial context."
        )

    if not request.question.strip():
        raise HTTPException(
            status_code=400,
            detail="Question is required."
        )

    context = verified_context(request.context)

    prompt = f"""
{SYSTEM_PROMPT}

USER QUESTION:
{request.question}

VERIFIED HISABDO CONTEXT:
{context}

Answer the user's question using ONLY the verified context.
"""

    try:
        result = client.models.generate_content(
            model="gemini-2.5-flash",
            contents=prompt,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=502,
            detail="AI explanation service failed."
        ) from exc

    limitations = []
    if request.context.limitations:
        limitations.append(request.context.limitations)

    return ExplainResponse(
        answer=result.text.strip(),
        isVerified=True,
        limitations=limitations,
    )
