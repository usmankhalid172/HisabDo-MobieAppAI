# HisabDo-MobieAppAI

## Render Deployment

This repository is prepared as a minimal Python AI backend for Render.

- Python version: 3.12
- Build command: `pip install --upgrade pip && pip install -r requirements.txt`
- Start command: `uvicorn main:app --host 0.0.0.0 --port $PORT`
- Health endpoint: `GET /health`
- Required environment variable names: `GEMINI_API_KEY`, `CORS_ALLOWED_ORIGINS`
- Local verification: `python -m unittest tests/test_health.py`

The deployed API exposes a lightweight FastAPI wrapper around the existing AI explanation logic without rewriting or replacing the original AI scripts in the repo.