"""Real HTTP contract tests; passing these does not claim a real model was used."""

import json
import threading
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest
import httpx
from fastapi.testclient import TestClient

from app.gateway_config import GatewayProviderRuntime, LocalGatewayConfigStore
from app.main import create_app
from app.protocols import resolve_endpoint
from app.providers import MockProvider, OpenAIProvider, ProviderInputError
from app.schemas import GatewayConfigureRequest, ResidentTaskRequest
from .test_openai_provider import _decision_request


@pytest.fixture
def upstream():
    class State:
        requests = []
        outcomes = []
        entered = threading.Event()
        release = threading.Event()
        block = False

    state = State()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass

        def do_GET(self):
            self.send_error(404)  # /models intentionally does not exist.

        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
            state.requests.append({"path": self.path, "headers": dict(self.headers), "body": body})
            if state.block:
                state.entered.set()
                state.release.wait(4)
            if state.outcomes:
                outcome = state.outcomes.pop(0)
            else:
                outcome = None
            if isinstance(outcome, tuple):
                code, payload = outcome
            else:
                if "messages" in body:
                    text = body["messages"][-1]["content"]
                    protocol = "anthropic" if "system" in body else "chat_completions"
                elif "contents" in body:
                    text = body["contents"][0]["parts"][0]["text"]
                    protocol = "gemini"
                else:
                    text, protocol = body["input"], "responses"
                try:
                    snapshot = json.loads(text.split("\nPrevious output")[0])
                except ValueError:
                    snapshot = {}
                if outcome is not None:
                    content = outcome
                elif "allowed_intents" in snapshot:
                    content = json.dumps({"resident_id": snapshot["resident_id"], "intent": snapshot["allowed_intents"][0],
                        "target_resident_id": None, "reason": "根据当前可执行活动选择行动。", "provider": "mock", "config_version": -99, "model": "spoofed"}, ensure_ascii=False)
                elif "participants" in snapshot:
                    content = json.dumps({"resident_id": snapshot["resident_id"], "lines": [{"speaker_id": owner, "mood": "Happy", "emoji": "🙂",
                        "text": "刚刚给农田浇了水，现在可以去果园看看。", "shared_knowledge_id": None} for owner in snapshot["participant_ids"]], "outcome": "Helpful"}, ensure_ascii=False)
                elif "command" in snapshot:
                    content = json.dumps({"resident_id": snapshot["resident_id"], "task_type": "Fish", "target_id": "fishing-2", "summary": "前往第二个钓位钓鱼。"}, ensure_ascii=False)
                else:
                    content = "你好，模型推理正常。"
                if protocol == "chat_completions":
                    payload = {"id": "contract-chat", "choices": [{"message": {"content": content}}]}
                elif protocol == "responses":
                    payload = {"id": "contract-responses", "status": "completed", "output": [{"content": [{"type": "output_text", "text": content}]}]}
                elif protocol == "anthropic":
                    payload = {"id": "contract-anthropic", "content": [{"type": "text", "text": content}], "stop_reason": "end_turn"}
                else:
                    payload = {"responseId": "contract-gemini", "candidates": [{"content": {"parts": [{"text": content}]}}]}
                code = 200
            encoded = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(encoded)))
            self.end_headers()
            try:
                self.wfile.write(encoded)
            except (BrokenPipeError, ConnectionResetError):
                pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    state.url = f"http://127.0.0.1:{server.server_port}"
    yield state
    state.release.set()
    server.shutdown()
    server.server_close()
    worker.join(timeout=2)


@pytest.mark.parametrize("protocol,suffix,header", [
    ("chat_completions", "/v1/chat/completions", "Authorization"),
    ("responses", "/v1/responses", "Authorization"),
    ("anthropic", "/v1/messages", "x-api-key"),
    ("gemini", "/v1beta/models/free-model-alias:generateContent", "x-goog-api-key"),
])
def test_four_protocols_actual_http_probe_and_task_contract(upstream, tmp_path, protocol, suffix, header):
    app = create_app(MockProvider(), LocalGatewayConfigStore(tmp_path / "gateway.json"))
    headers = {"X-AIFarm-Client": "unity"}
    with TestClient(app) as client:
        configured = client.post("/v1/gateway-config", headers=headers, json={"provider": "openai", "protocol": protocol,
            "base_url": upstream.url + "/proxy", "model": "free-model-alias", "api_key": "contract-secret", "persist": True})
        assert configured.status_code == 200
        assert configured.json()["endpoint"] == upstream.url + "/proxy" + suffix
        assert configured.json()["upstream_status"] == "connecting"
        probe = client.post("/v1/gateway-config/probe", headers=headers)
        assert probe.status_code == 200
        assert probe.json()["ok"] is True, probe.json()
        assert [item["name"] for item in probe.json()["checks"]] == ["text", "resident_decision", "conversation"]
        assert all(item["ok"] for item in probe.json()["checks"])
        assert client.get("/v1/gateway-config").json()["upstream_status"] == "online"
        decision = client.post("/v1/resident-decision", json=_decision_request("resident-004", "墨墨", "仅墨墨知道的事情", allowed_intents=["Fish"]).model_dump(mode="json"))
        assert decision.json()["provider"] == "openai"
        assert decision.json()["execution_source"] == "remote"
        assert decision.json()["config_version"] == configured.json()["config_version"]
        assert decision.json()["model"] == "free-model-alias"
        assert len(decision.json()["request_id"]) == 32
        task = client.post("/v1/resident-task", json={"resident_id": "resident-003", "command": "去第二个钓位钓鱼", "allowed_target_ids": ["fishing-1", "fishing-2"]})
        assert task.status_code == 200
        assert task.json()["task_type"] == "Fish"
        assert task.json()["target_id"] == "fishing-2"
        assert task.json()["resident_id"] == "resident-003"
        assert task.json()["execution_source"] == "remote"
        assert "contract-secret" not in probe.text + configured.text + decision.text + task.text
    assert len(upstream.requests) == 5
    assert {request["path"] for request in upstream.requests} == {"/proxy" + suffix}
    assert all(httpx.Headers(request["headers"]).get(header) == ("Bearer contract-secret" if header == "Authorization" else "contract-secret") for request in upstream.requests)
    assert all("response_format" not in request["body"] and "text" not in request["body"] for request in upstream.requests)


@pytest.mark.parametrize("base,protocol,expected", [
    ("https://example.invalid", "chat_completions", "https://example.invalid/v1/chat/completions"),
    ("https://example.invalid/v1/", "chat_completions", "https://example.invalid/v1/chat/completions"),
    ("https://example.invalid/proxy/v1", "responses", "https://example.invalid/proxy/v1/responses"),
    ("https://example.invalid/proxy/messages", "anthropic", "https://example.invalid/proxy/messages"),
    ("https://example.invalid/proxy/v1/chat/completions?api-version=2025", "chat_completions", "https://example.invalid/proxy/v1/chat/completions?api-version=2025"),
    ("https://example.invalid/v1beta", "gemini", "https://example.invalid/v1beta/models/model-alias:generateContent"),
    ("https://example.invalid/prefix/v1/models/custom:generateContent", "gemini", "https://example.invalid/prefix/v1/models/custom:generateContent"),
])
def test_endpoint_paths_preserve_prefixes(base, protocol, expected):
    assert resolve_endpoint(base, protocol, "model-alias") == expected


def test_empty_key_and_same_model_key_url_change_reach_new_configuration(upstream, tmp_path):
    runtime = GatewayProviderRuntime(MockProvider(), LocalGatewayConfigStore(tmp_path / "gateway.json"))
    request = _decision_request("resident-002", "阿木", "本人的记忆", allowed_intents=["Rest"])
    for prefix, key in (("a", "first-key"), ("b", "second-key"), ("c", "")):
        status = runtime.configure(GatewayConfigureRequest(provider="openai", model="same-model", base_url=upstream.url + "/" + prefix, api_key=key, persist=False))
        result = runtime.execute("decide_resident", request)
        assert result.config_version == status.config_version
        assert result.execution_source == "remote"
    assert [item["path"] for item in upstream.requests] == [f"/{prefix}/v1/chat/completions" for prefix in "abc"]
    assert [item["headers"].get("Authorization", "") for item in upstream.requests] == ["Bearer first-key", "Bearer second-key", ""]


@pytest.mark.parametrize("status,body,expected", [
    (401, {"error": {"message": "Invalid credential contract-secret"}}, "authentication_failed"),
    (403, {"error": {"message": "Access denied"}}, "permission_denied"),
    (404, {"error": {"message": "Unknown endpoint"}}, "endpoint_error"),
    (404, {"error": {"code": "model_not_found", "message": "Unknown model"}}, "model_not_found"),
    (429, {"error": {"message": "Too many requests"}}, "rate_limited"),
    (429, {"error": {"message": "insufficient_quota"}}, "quota_exceeded"),
    (500, {"error": {"message": "Server failed"}}, "upstream_error"),
])
def test_errors_are_distinct_and_fallback_is_never_remote(upstream, tmp_path, status, body, expected, caplog):
    provider = OpenAIProvider(api_key="contract-secret", model="alias", base_url=upstream.url)
    runtime = GatewayProviderRuntime(provider, LocalGatewayConfigStore(tmp_path / "gateway.json"))
    upstream.outcomes = [(status, body)]
    result = runtime.execute("decide_resident", _decision_request("resident-002", "阿木", "记忆", allowed_intents=["Walk"]))
    assert result.intent == "Walk"
    assert result.execution_source == "fallback"
    assert result.provider == "mock"
    assert result.error_code == expected
    assert runtime.status().upstream_status == "degraded"
    assert "contract-secret" not in result.model_dump_json() + caplog.text
    recovered = runtime.execute("decide_resident", _decision_request("resident-002", "阿木", "记忆", allowed_intents=["Fish"]))
    assert recovered.execution_source == "remote"
    assert recovered.intent == "Fish"
    assert runtime.status().upstream_status == "online"


def test_invalid_json_repairs_only_once_and_markdown_is_supported(upstream):
    provider = OpenAIProvider(api_key="", model="alias", base_url=upstream.url)
    request = _decision_request("resident-001", "芽芽", "记忆", allowed_intents=["Walk"])
    upstream.outcomes = ["invalid JSON"]
    assert provider.decide_resident(request).provider == "openai"
    assert len(upstream.requests) == 2
    assert "Previous output failed" in upstream.requests[-1]["body"]["messages"][-1]["content"]
    upstream.outcomes = ["invalid JSON", "invalid JSON"]
    assert provider.decide_resident(request).provider == "mock"
    assert len(upstream.requests) == 4
    assert provider.last_error.get().code == "output_parse_error"
    upstream.outcomes = ['```json\n{"resident_id":"resident-001","intent":"Walk","target_resident_id":null,"reason":"散步"}\n```']
    assert provider.decide_resident(request).provider == "openai"
    assert len(upstream.requests) == 5


def test_optional_json_mode_rejection_is_cached_per_configuration(upstream):
    provider = OpenAIProvider(api_key="", model="alias", base_url=upstream.url, output_mode="json")
    upstream.outcomes = [(400, {"error": {"message": "response_format is not supported"}})]
    request = _decision_request("resident-001", "芽芽", "记忆", allowed_intents=["Walk"])
    assert provider.decide_resident(request).provider == "openai"
    assert provider.decide_resident(request).provider == "openai"
    assert len(upstream.requests) == 3
    assert "response_format" in upstream.requests[0]["body"]
    assert all("response_format" not in request["body"] for request in upstream.requests[1:])


def test_timeout_and_configuration_switch_discard_inflight_result(upstream, tmp_path):
    provider = OpenAIProvider(api_key="", model="alias", base_url=upstream.url, timeout_seconds=0.05)
    upstream.block = True
    request = _decision_request("resident-001", "芽芽", "记忆", allowed_intents=["Walk"])
    assert provider.decide_resident(request).provider == "mock"
    assert provider.last_error.get().code == "timeout"
    upstream.release.set()
    upstream.block = False
    runtime = GatewayProviderRuntime(OpenAIProvider(api_key="", model="alias", base_url=upstream.url), LocalGatewayConfigStore(tmp_path / "gateway.json"))
    upstream.entered.clear()
    upstream.release.clear()
    upstream.block = True
    with ThreadPoolExecutor(max_workers=1) as executor:
        future = executor.submit(runtime.execute, "decide_resident", request)
        assert upstream.entered.wait(2)
        runtime.configure(GatewayConfigureRequest(provider="mock", persist=False))
        upstream.release.set()
        with pytest.raises(ProviderInputError, match="旧模型请求结果已忽略"):
            future.result(timeout=3)
    assert runtime.status().upstream_status == "unconfigured"


@pytest.mark.parametrize("resident", ["resident-001", "resident-002", "resident-003", "resident-004"])
@pytest.mark.parametrize("command,expected", [("播种", "Sow"), ("浇水", "Water"), ("施肥", "Fertilize"), ("除草", "Weed"), ("收获", "Harvest"),
    ("持续照料农田", "TendFarm"), ("钓鱼", "Fish"), ("摘果", "PickFruit"), ("前往水井", "Move"), ("停止任务", "Stop")])
def test_every_resident_has_same_task_contract(resident, command, expected):
    result = MockProvider().interpret_task(ResidentTaskRequest(resident_id=resident, command=command, allowed_target_ids=["well", "farm"]))
    assert result.resident_id == resident
    assert result.task_type == expected
    assert result.provider == "mock"


def test_task_targets_and_unknown_commands_are_not_redirected():
    provider = MockProvider()
    with pytest.raises(ProviderInputError, match="未找到"):
        provider.interpret_task(ResidentTaskRequest(resident_id="resident-004", command="去火星", allowed_target_ids=["well"]))
    with pytest.raises(ProviderInputError, match="未识别"):
        provider.interpret_task(ResidentTaskRequest(resident_id="resident-004", command="建造航天飞机"))
    task = provider.interpret_task(ResidentTaskRequest(resident_id="resident-004", command="给第3块地浇水"))
    assert task.target_plot_numbers == [3]
    assert task.repeat is False
    chat = provider.interpret_task(ResidentTaskRequest(resident_id="resident-004", command="与阿木聊天", allowed_target_resident_ids=["resident-002"]))
    assert chat.target_resident_id == "resident-002"


@pytest.mark.parametrize("command", ["给第10块地浇水", "给第0块地施肥", "去fishing-99钓鱼", "和resident-099聊天"])
def test_invalid_explicit_target_never_becomes_auto_selected(command):
    with pytest.raises(ProviderInputError) as failure:
        MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-004", command=command,
            allowed_target_ids=["fishing-1"], allowed_target_resident_ids=["resident-002"]))
    assert failure.value.code == "target_not_found"


def test_default_launcher_restores_saved_configuration_but_test_override_is_offline(tmp_path, monkeypatch):
    from app.gateway_config import GatewayCredentials
    store = LocalGatewayConfigStore(tmp_path / "gateway.json")
    store.save(GatewayCredentials("openai", "local alias", "", "http://127.0.0.1:11434/v1"))
    monkeypatch.setenv("AIFARM_PROVIDER", "mock")
    monkeypatch.delenv("AIFARM_IGNORE_SAVED_CONFIG", raising=False)
    restored = GatewayProviderRuntime(store=store)
    assert restored.status().provider == "openai"
    assert restored.status().model == "local alias"
    assert restored.status().source == "local_config"
    assert restored.status().upstream_status == "connecting"
    monkeypatch.setenv("AIFARM_IGNORE_SAVED_CONFIG", "1")
    isolated = GatewayProviderRuntime(store=store)
    assert isolated.status().provider == "mock"
    assert isolated.status().upstream_status == "unconfigured"


def test_revision_one_saved_configuration_migrates_without_exposing_secret(tmp_path, monkeypatch):
    path = tmp_path / "gateway.json"
    path.write_text(json.dumps({"version": 1, "provider": "openai", "model": "old-alias", "api_key": "migration-secret"}), encoding="utf-8")
    store = LocalGatewayConfigStore(path)
    credentials = store.load()
    assert credentials.api_key == "migration-secret"
    assert credentials.protocol == "chat_completions"
    assert credentials.output_mode == "text"
    assert credentials.base_url == "https://api.deepseek.com"
    store.save(credentials)
    assert json.loads(path.read_text(encoding="utf-8"))["version"] == 2


def test_observed_world_memories_use_json_null_source_and_reach_inference(upstream, tmp_path):
    """Regression for the five Unity wire source IDs that had serialized as empty strings."""
    request = _decision_request("resident-001", "芽芽", "我刚刚给农田浇水。", allowed_intents=["Walk"]).model_dump(mode="json")
    request["context"]["relevant_memories"] = [{
        "owner_resident_id": "resident-001", "text": "我刚刚完成了农田工作。", "importance": 4,
        "knowledge_id": f"knowledge:resident-001:{index}", "root_fact_id": f"world-event:contract:{index}",
        "tags": ["farm", "actioncompleted"], "is_shareable": True, "immediate_source_resident_id": None,
    } for index in range(5)]
    provider = OpenAIProvider(api_key="", model="wire-contract", base_url=upstream.url)
    with TestClient(create_app(provider, LocalGatewayConfigStore(tmp_path / "gateway.json"))) as client:
        valid = client.post("/v1/resident-decision", json=request)
        assert valid.status_code == 200
        assert valid.json()["resident_id"] == "resident-001"
        assert valid.json()["intent"] == "Walk"
        assert valid.json()["execution_source"] == "remote"
        sent = json.loads(upstream.requests[-1]["body"]["messages"][-1]["content"])
        assert all(memory["immediate_source_resident_id"] is None for memory in sent["context"]["relevant_memories"])
        for memory in request["context"]["relevant_memories"]:
            memory["immediate_source_resident_id"] = ""
        invalid = client.post("/v1/resident-decision", json=request)
        assert invalid.status_code == 422
        assert [detail["location"] for detail in invalid.json()["error"]["details"]] == [
            f"body.context.relevant_memories.{index}.immediate_source_resident_id" for index in range(5)]
        assert len(upstream.requests) == 1, "Invalid wire values must not reach the model."


def test_conversation_memories_with_null_ids_keep_each_participants_ownership(upstream, tmp_path):
    from .test_openai_provider import _conversation_request
    payload = _conversation_request(max_lines=2).model_dump(mode="json")
    provider = OpenAIProvider(api_key="", model="wire-contract", base_url=upstream.url)
    with TestClient(create_app(provider, LocalGatewayConfigStore(tmp_path / "gateway.json"))) as client:
        response = client.post("/v1/conversation-script", json=payload)
        assert response.status_code == 200
        assert response.json()["execution_source"] == "remote"
        assert len(response.json()["lines"]) == 2
    sent = json.loads(upstream.requests[0]["body"]["messages"][-1]["content"])
    for participant in sent["participants"]:
        for memory in participant["relevant_memories"]:
            assert memory["owner_resident_id"] == participant["resident_id"]
            assert memory["knowledge_id"] is None
            assert memory["root_fact_id"] is None
            assert memory["immediate_source_resident_id"] is None


@pytest.mark.parametrize("command,expected", [
    ("播种1号地", [1]), ("给第1块地播种", [1]), ("浇水地块2", [2]), ("给plot3施肥", [3]),
    ("收获9号地", [9]), ("播种1、3号地", [1, 3]), ("给1号地和9号地浇水", [1, 9]),
    ("播种1至3号地", [1, 2, 3]), ("播种1-3号地", [1, 2, 3]),
    ("给9号地浇水1次", [9]), ("给9号地浇水一次", [9]),
])
def test_explicit_plot_scope_never_expands_to_the_whole_farm(command, expected):
    task = MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-003", command=command))
    assert task.resident_id == "resident-003"
    assert task.target_plot_numbers == expected
    assert task.quantity == 1


@pytest.mark.parametrize("command", ["播种10号地", "播种0号地", "播种-1号地", "播种1、10号地", "播种1至10号地", "播种3到1号地", "播种1.5号地", "播种第十块地"])
def test_nonexistent_explicit_plot_is_rejected_instead_of_replaced(command):
    with pytest.raises(ProviderInputError) as failure:
        MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-004", command=command))
    assert failure.value.code == "target_not_found"


@pytest.mark.parametrize("command,kind,quantity", [
    ("钓鱼3次", "Fish", 3), ("钓3条鱼", "Fish", 3), ("钓鱼2尾", "Fish", 2),
    ("摘3个果", "PickFruit", 3), ("摘三个苹果", "PickFruit", 3), ("摘果两份", "PickFruit", 2),
    ("钓鱼十次", "Fish", 10), ("钓鱼二十一次", "Fish", 21), ("钓鱼九十九次", "Fish", 99),
    ("fish 3 times", "Fish", 3), ("持续钓鱼3次", "Fish", 3),
])
def test_explicit_activity_quantity_is_bounded_and_finite(command, kind, quantity):
    task = MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-002", command=command))
    assert task.task_type == kind
    assert task.quantity == quantity
    assert task.repeat is False
    assert task.target_plot_numbers == []


@pytest.mark.parametrize("command", ["钓鱼0次", "钓鱼100次", "钓鱼-1次", "钓鱼1.5次", "钓鱼零次", "钓鱼一百次", "钓鱼负一次", "钓鱼三点五次", "钓鱼3次再钓鱼2次"])
def test_invalid_activity_quantity_is_not_clamped_or_silently_ignored(command):
    with pytest.raises(ProviderInputError) as failure:
        MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-003", command=command))
    assert failure.value.code == "invalid_quantity"


@pytest.mark.parametrize("command,target,quantity", [
    ("去2号钓位钓鱼3次", "fishing-2", 3), ("去第二个钓位钓鱼3次", "fishing-2", 3),
    ("去3号果树摘4个果", "fruit-3", 4), ("去第三棵果树摘果", "fruit-3", 1),
])
def test_interaction_point_number_is_separate_from_activity_quantity(command, target, quantity):
    task = MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-001", command=command,
        allowed_target_ids=["fishing-1", "fishing-2", "fruit-1", "fruit-2", "fruit-3", "fruit-4"]))
    assert task.target_id == target
    assert task.quantity == quantity
    assert task.target_plot_numbers == []


@pytest.mark.parametrize("command", ["去3号钓位钓鱼", "去第三个钓位钓鱼", "去5号果树摘果"])
def test_unknown_numbered_interaction_point_is_not_auto_replaced(command):
    with pytest.raises(ProviderInputError) as failure:
        MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-001", command=command,
            allowed_target_ids=["fishing-1", "fishing-2", "fruit-1", "fruit-2", "fruit-3", "fruit-4"]))
    assert failure.value.code == "target_not_found"


def test_ongoing_activity_without_quantity_remains_explicitly_repeatable():
    task = MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-003", command="持续钓鱼"))
    assert task.repeat is True
    assert task.quantity == 1


@pytest.mark.parametrize("command", ["浇水3次", "给9号地浇水2次", "和阿木聊天3次", "一直和阿木聊天"])
def test_unsupported_farm_counts_and_repeated_chats_are_explicit_errors(command):
    with pytest.raises(ProviderInputError) as failure:
        MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-001", command=command,
            allowed_target_resident_ids=["resident-002"]))
    assert failure.value.code in {"unsupported_quantity", "unsupported_task_mode"}


@pytest.mark.parametrize("kind,repeat,quantity", [("Chat", True, 1), ("Chat", False, 2), ("Water", False, 2), ("TendFarm", True, 2)])
def test_task_schema_rejects_parameters_the_executor_cannot_honor(kind, repeat, quantity):
    from pydantic import ValidationError
    from app.schemas import ResidentTaskSpec
    with pytest.raises(ValidationError):
        ResidentTaskSpec(resident_id="resident-001", task_id="bounded-test", task_type=kind, repeat=repeat,
            quantity=quantity, summary="不可执行的任务参数", provider="mock")


@pytest.mark.parametrize("command", MockProvider._supported_commands)
def test_legacy_full_field_commands_remain_compatible_with_general_task_route(command):
    task = MockProvider().interpret_task(ResidentTaskRequest(resident_id="resident-004", command=command))
    assert task.task_type == "TendFarm"
    assert task.target_plot_numbers == list(range(1, 10))
    assert task.quantity == 1
    assert task.repeat is False, "Care until harvest is a bounded lifecycle unless repetition was requested."
