# AIFarm shared AI gateway

The FastAPI gateway interprets high-level resident tasks, chooses allowed intents
and produces dialogue. Unity owns navigation, simulation time, inventory and all
world mutations. Every request carries an explicit resident_id.

## Start and configure

Use the game's native **全镇共享模型设置** panel: enter an upstream Base URL /
Endpoint, a free model name and an optional API Key, then select **测试并应用**.
All residents share the configuration. The optional browser page at
http://127.0.0.1:8000/setup edits exactly the same running configuration.

From Server/:

```powershell
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8000 --no-access-log
.\.venv\Scripts\python.exe -m pytest -q
```

If dependencies are missing, create .venv and install the existing requirements.
Unity discovers Server beside the player, in the repository, or under
StreamingAssets/AIFarmGateway. AIFARM_GATEWAY_SERVER_DIR and AIFARM_GATEWAY_PYTHON
override discovery. Unity stops only the exact process it owns. Existing
gateways can be reconfigured without stopping them.

The local Python gateway address is a loopback HTTP address used by Unity.
The upstream model address can be public, a reverse proxy or an unauthenticated
local service. It is not restricted to loopback.

## Protocol and inference contract

| Protocol value | Default request path |
| --- | --- |
| chat_completions (default) | /v1/chat/completions |
| responses | /v1/responses |
| anthropic | /v1/messages |
| gemini | /v1beta/models/{model}:generateContent |

Protocol selection is explicit. Root URLs, version URLs, reverse-proxy prefixes
and full endpoints are accepted. The final endpoint is displayed in status.
A full endpoint inconsistent with the protocol is rejected before sending a Key.
Redirects are not followed; HTTPS certificate verification remains enabled.

The default output_mode=text uses a normal text JSON convention. Optional json
and schema modes are supported by the relevant adapters. Unsupported formatting
parameters are cached and omitted for later requests in the same configuration.
All responses are locally schema-validated and business-validated. Markdown JSON
fences are accepted. Invalid output receives at most one controlled repair, then
an explicitly marked local fallback.

The connection test performs **three actual upstream inferences**: a short text
completion, a resident decision and a two-resident conversation. It never requires
/models. /health only means the local gateway is running. Mock or fallback
results never count as successful model inference. Diagnostics distinguish
authentication, permission, model, endpoint/protocol, quota, rate limiting,
timeout and output parsing errors.

## Configuration API

GET /v1/gateway-config reads status. Native Unity writes carry
X-AIFarm-Client: unity without Origin, from loopback. Browser writes retain
same-origin CSRF cookie/header checks from /setup.

POST /v1/gateway-config accepts provider (openai/mock), protocol, base_url, model,
api_key, persist, and optional output_mode (text/json/schema).
An explicit empty Key supports unauthenticated services. A null Key reuses the
stored secret only when address and protocol are both unchanged. Changing URL
never silently forwards the old Key. Every update replaces the adapter, increments
config_version, clears stale diagnostics and rejects old in-flight results.

POST /v1/gateway-config/probe returns checks for text, resident_decision and
conversation, plus endpoint and config_version.
POST /v1/gateway-config/clear clears persisted credentials and selects local rules.
Status separates provider, upstream_status (unconfigured/connecting/online/degraded/error),
last_error_code and last_error.

Remembered configuration is outside the repository at
%LOCALAPPDATA%\AIFarm\ai-gateway.json on Windows, or the user's XDG config directory.
The local file contains the Key; it is never written to Unity assets, PlayerPrefs,
saves, logs or API responses. Version 1 files migrate on load. persist=false
removes remembered settings. AIFARM_LOCAL_CONFIG_PATH allows an isolated test path.

Saved configuration is restored when Unity starts the default mock gateway.
Explicit AIFARM_PROVIDER=openai environment configuration takes precedence and uses
OPENAI_API_KEY, OPENAI_MODEL, OPENAI_BASE_URL, AIFARM_PROTOCOL and AIFARM_OUTPUT_MODE.
AIFARM_IGNORE_SAVED_CONFIG=1 with mock mode forces an isolated offline environment.

## Resident tasks and provenance

POST /v1/resident-task accepts resident_id, command, allowed_target_ids and
allowed_target_resident_ids. It returns task_id, resident_id, task_type, target_id,
target_resident_id, target_plot_numbers, crop, quantity, repeat and summary.
Task types: Move, Sow, Water, Fertilize, Weed, Harvest, TendFarm, Fish, PickFruit,
Chat, Stop. Unsupported commands and unknown explicit targets are errors.

Local task parsing preserves explicit plot scope (for example, 播种1号地 selects
only plot 1). Fishing and fruit picking accept quantities from 1 to 99, including
common Chinese numbers. An explicit quantity makes an activity finite even when
the command also says 持续; without a quantity, 持续 remains repeatable. Invalid
targets/counts are rejected. Repeated Chat and farm operation counts greater than
one are explicitly unsupported; farm care can use TendFarm/repeat instead.

The legacy /v1/interpret-command carrot lifecycle contract remains.
The existing resident-decision, conversation-script, resident-reflection,
generate-utterance and reflect routes retain their functional contracts.
Every output additionally carries gateway-owned config_version, request_id, model,
execution_source (remote/local/fallback), error_code and error_message.
The provider marker is also gateway-owned. The model cannot override provenance.
Private-memory ownership and conversation knowledge validation remain enforced.

## Validation scope

tests/test_protocol_contracts.py uses a controlled HTTP server for all four
protocols, three-inference probes without /models, key/URL changes, unauthenticated
models, invalid JSON/one repair, optional-parameter caching, error categories,
recovery, timeouts, stale configuration, and all residents' task contracts.
Existing tests retain private-context isolation, intent constraints and dialogue
limits. Contract tests do not claim a real model was used.

On 2026-09-12, the existing locally saved DeepSeek configuration was used for the
three minimal real-upstream checks. All returned authentication_failed.
No real-model check passed. The other three protocols have no available real
credentials and are contract-tested only. No credential is included here.

Protocol references: [Chat Completions](https://developers.openai.com/api/reference/resources/chat),
[Responses](https://platform.openai.com/docs/api-reference/responses),
[Anthropic Messages](https://docs.anthropic.com/en/api/messages),
[Gemini generateContent](https://ai.google.dev/api/generate-content).
