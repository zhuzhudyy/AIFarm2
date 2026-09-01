# AIFarm Local AI Gateway

This FastAPI service is an optional local enhancement. It never plans or mutates Unity game state. `MockProvider` remains the safe default; `OpenAIProvider` uses DeepSeek's OpenAI-compatible Responses API with JSON Schema Structured Outputs.

From the `Server` directory:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8000
```

Open `http://127.0.0.1:8000/setup`, or click `API SETUP` in the Unity HUD,
to configure the shared provider and model. Open
`http://127.0.0.1:8000/docs` for the generated API contract. Run tests with:

```powershell
.\.venv\Scripts\python.exe -m pytest
```

The default suite uses injected fake clients and never calls a real model.

## Player API settings

The setup page can switch immediately between DeepSeek and the deterministic
offline `MockProvider`, set the shared model ID, accept a masked API key, test
authentication/model availability, and clear the local configuration. The page
loads no third-party resources. Configuration routes accept only loopback clients
and same-origin requests with a short-lived CSRF token; the key is never returned
by an API response or sent to Unity.

With **Remember locally** enabled, the gateway writes one current-user file outside
the repository:

- Windows: `%LOCALAPPDATA%\AIFarm\ai-gateway.json`
- Linux/macOS: `$XDG_CONFIG_HOME/aifarm/ai-gateway.json` or
  `~/.config/aifarm/ai-gateway.json`

This local-demo file may contain the key in plaintext and is not a production
secret manager. Use process environment variables or an operating-system secret
manager on shared or production machines. `AIFARM_PROVIDER` in the process
environment takes precedence at server startup; UI changes still apply to the
current process, but that environment configuration will be restored on restart.

The generated Unity demo scene uses the local HTTP gateway by default. If the
gateway or upstream model is unavailable, `RemoteAiGatewayClient` continues with
its deterministic local fallback.

## Multi-resident contracts

The gateway keeps one application-scoped provider and one `OPENAI_MODEL`
configuration for every resident. All six AI request schemas require an explicit
stable `resident_id`. Town V2 adds these operations without creating a second
provider stack:

- `POST /v1/resident-decision` returns a high-level intent constrained by the
  request's `allowed_intents` and `allowed_target_resident_ids`.
- `POST /v1/conversation-script` accepts exactly two participant IDs and one
  owner-checked `ResidentContext` per participant, then returns two to six lines.
- `POST /v1/resident-reflection` creates a bounded reflection from only the
  request owner's context.

`ResidentContext` contains a persona snapshot, owner-tagged relationship
snapshots, and owner-tagged relevant memories. The schema rejects a relationship
or private memory whose owner differs from the enclosing resident. Conversation
output is revalidated so every `speaker_id` is a participant and the response
cannot exceed either the request's `max_lines` or the global six-line limit.
Invalid model JSON, unknown speakers, disallowed intents or targets, timeouts,
authentication failures, and other provider failures use the same deterministic
`MockProvider` fallback as the original endpoints.

## DeepSeek provider

Set the provider, model, and secret in the process environment before starting the server:

```powershell
$env:AIFARM_PROVIDER = "openai"
$env:OPENAI_MODEL = "deepseek-v4-flash"
$env:OPENAI_API_KEY = "<your DeepSeek API key>"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8000
```

The gateway can read the model and key from these environment variables or from
the user-local setup file above. API responses and `/health` expose only whether
a key is configured, never its value. Do not commit a real `.env` file. The
`Test connection` action calls the provider's model-list endpoint and does not
start an inference request.

Each operation sends a bounded snapshot rather than an unrestricted world state. Successful model JSON is validated again with the endpoint's Pydantic model and request-specific allowlists. Timeouts, authentication failures, refusals, rate limits, connection failures, and invalid model output fall back to `MockProvider`. Logs contain a local request ID, resident ID, elapsed milliseconds, result type, source, and upstream request ID; prompts, private memories, and credentials are not logged.

An opt-in live smoke test is available. It is skipped unless explicitly enabled:

```powershell
$env:RUN_DEEPSEEK_INTEGRATION = "1"
$env:OPENAI_MODEL = "deepseek-v4-flash"
$env:OPENAI_API_KEY = "<your DeepSeek API key>"
.\.venv\Scripts\python.exe -m pytest tests/test_openai_provider.py -k live
```
