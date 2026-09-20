import importlib.util
import os
from pathlib import Path
from typing import Any, Dict

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

load_dotenv()

app = FastAPI(title="HisabDo AI", version="1.0.0")

allowed_origins = [
    origin.strip()
    for origin in os.getenv("CORS_ALLOWED_ORIGINS", "http://localhost:3000,http://localhost:5173").split(",")
    if origin.strip()
]

if not allowed_origins:
    allowed_origins = ["http://localhost:3000", "http://localhost:5173"]

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


def _load_ai_module():
    module_path = Path(__file__).resolve().parent / "AI-Explanation" / "AI-Explanation" / "ai_explanation.py"
    if not module_path.exists():
        raise FileNotFoundError(f"AI explanation module not found at {module_path}")

    spec = importlib.util.spec_from_file_location("hisabdo_ai_explanation", module_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Unable to load AI explanation module from {module_path}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@app.get("/health")
def health() -> Dict[str, str]:
    return {"status": "ok", "service": "hisabdo-ai"}


@app.post("/api/explain")
async def explain(payload: Dict[str, Any] | None = None) -> Dict[str, Any]:
    if not os.getenv("GEMINI_API_KEY"):
        raise HTTPException(status_code=503, detail="GEMINI_API_KEY is not configured.")

    ai_module = _load_ai_module()
    data = payload or {
        "company_name": "ABC Technologies",
        "revenue": 10000000,
        "profit": 1800000,
        "expenses": 8200000,
        "revenue_growth": 15,
        "profit_growth": 10,
        "financial_score": 78,
    }

    try:
        return ai_module.generate_financial_explanation(data)
    except Exception as exc:  # pragma: no cover - surfaced to API caller
        raise HTTPException(status_code=500, detail=f"AI explanation failed: {exc}") from exc
