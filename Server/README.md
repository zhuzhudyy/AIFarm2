# AIFarm Local AI Gateway

This FastAPI service is an optional local enhancement. It never plans or mutates Unity game state. `MockProvider` remains the safe default; `OpenAIProvider` uses DeepSeek's OpenAI-compatible Responses API with JSON Schema Structured Outputs.

From the repository root:

```powershell
python -m venv Server/.venv
.\Server\.venv\Scripts\python.exe -m pip install -r Server/requirements.txt
.\Server\.venv\Scripts\python.exe -m uvicorn Server.app.main:app --host 127.0.0.1 --port 8000
```

Open `http://127.0.0.1:8000/docs` for the generated API contract. Run tests with:

```powershell
.\Server\.venv\Scripts\python.exe -m pytest Server/tests
```

The default suite uses injected fake clients and never calls a real model.

## DeepSeek provider

Set the provider, model, and secret in the process environment before starting the server:

```powershell
$env:AIFARM_PROVIDER = "openai"
$env:OPENAI_MODEL = "deepseek-v4-flash"
$env:OPENAI_API_KEY = "<your DeepSeek API key>"
.\Server\.venv\Scripts\python.exe -m uvicorn Server.app.main:app --host 127.0.0.1 --port 8000
```

The model name is read only from `OPENAI_MODEL`; the key is read only from `OPENAI_API_KEY`. The API response and `/health` expose only whether a key is configured, never its value. Do not commit a real `.env` file.

Each operation sends a bounded snapshot rather than an unrestricted world state. Successful model JSON is validated again with the endpoint's Pydantic model. Timeouts, refusals, rate limits, connection failures, and invalid model output fall back to `MockProvider`. Logs contain a local request ID, elapsed milliseconds, result type, source, and upstream request ID; prompts and credentials are not logged.

An opt-in live smoke test is available. It is skipped unless explicitly enabled:

```powershell
$env:RUN_DEEPSEEK_INTEGRATION = "1"
$env:OPENAI_MODEL = "deepseek-v4-flash"
$env:OPENAI_API_KEY = "<your DeepSeek API key>"
.\Server\.venv\Scripts\python.exe -m pytest Server/tests/test_openai_provider.py -k live
```
