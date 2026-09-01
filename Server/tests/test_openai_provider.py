import json
import logging
import os
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import httpx
import pytest
from fastapi.testclient import TestClient
from openai import (
    APIConnectionError,
    APITimeoutError,
    AuthenticationError,
    OpenAI,
    RateLimitError,
)
from pydantic import BaseModel

from app import providers
from app.main import _default_provider, create_app
from app.providers import MockProvider, OpenAIProvider
from app.schemas import (
    ConversationLineSpec,
    ConversationOutcome,
    ConversationScriptRequest,
    ConversationScriptSpec,
    FarmGoalSpec,
    GenerateUtteranceRequest,
    InterpretCommandRequest,
    NpcExpressionTrigger,
    NpcMood,
    ReflectRequest,
    ReflectionOutcome,
    ReflectionSpec,
    RelationshipSnapshot,
    ResidentContext,
    ResidentDecisionRequest,
    ResidentDecisionSpec,
    ResidentMemorySnapshot,
    ResidentPersonaSnapshot,
    UtteranceSpec,
)


TEST_API_KEY = "unit-test-api-key"
TEST_MODEL = "deepseek-v4-flash"
SUPPORTED_COMMAND = "把地种满胡萝卜并照顾到收获。"


def _goal() -> FarmGoalSpec:
    return FarmGoalSpec(
        goal_id="full_field_carrot_lifecycle",
        crop="carrot",
        target_plot_numbers=list(range(1, 10)),
        requires_sowing=True,
        requires_watering=True,
        requires_fertilizing=True,
        requires_weeding=True,
        requires_harvesting=True,
        summary="完成 3×3 农田的胡萝卜全周期",
    )


def _command_request(
    resident_id: str = "resident-001",
) -> InterpretCommandRequest:
    return InterpretCommandRequest(
        resident_id=resident_id,
        command=SUPPORTED_COMMAND,
    )


def _resident_context(
    resident_id: str,
    display_name: str,
    memory_text: str,
    relationship_target: str | None = None,
) -> ResidentContext:
    relationships = []
    if relationship_target is not None:
        relationships.append(
            RelationshipSnapshot(
                owner_resident_id=resident_id,
                target_resident_id=relationship_target,
                familiarity=10,
                trust=5,
            )
        )
    return ResidentContext(
        resident_id=resident_id,
        persona=ResidentPersonaSnapshot(
            display_name=display_name,
            role=f"{display_name}的角色",
            personality_traits=[f"{display_name}特质"],
            speaking_style=f"{display_name}风格",
            preference=f"{display_name}偏好",
        ),
        current_state=f"{display_name}正在广场休息",
        relationship_snapshots=relationships,
        relevant_memories=[
            ResidentMemorySnapshot(
                owner_resident_id=resident_id,
                text=memory_text,
                importance=7,
            )
        ],
    )


def _decision_request(
    resident_id: str,
    display_name: str,
    memory_text: str,
    *,
    allowed_intents: list[str] | None = None,
    allowed_targets: list[str] | None = None,
) -> ResidentDecisionRequest:
    return ResidentDecisionRequest(
        resident_id=resident_id,
        context=_resident_context(resident_id, display_name, memory_text),
        situation="按确定性日程选择下一项高层活动。",
        allowed_intents=allowed_intents or ["Idle"],
        allowed_target_resident_ids=allowed_targets or [],
    )


def _conversation_request(max_lines: int = 6) -> ConversationScriptRequest:
    return ConversationScriptRequest(
        resident_id="resident-001",
        participant_ids=["resident-001", "resident-002"],
        participants=[
            _resident_context(
                "resident-001",
                "芽芽",
                "芽芽只知道的记忆",
                "resident-002",
            ),
            _resident_context(
                "resident-002",
                "阿木",
                "阿木只知道的记忆",
                "resident-001",
            ),
        ],
        topic="水井维护",
        max_lines=max_lines,
    )


def _completed_response(
    payload: BaseModel | str,
    response_id: str,
) -> SimpleNamespace:
    output_text = payload.model_dump_json() if isinstance(payload, BaseModel) else payload
    return SimpleNamespace(
        id=response_id,
        status="completed",
        incomplete_details=None,
        output=[],
        output_text=output_text,
    )


class FakeResponses:
    def __init__(self, outcomes: list[object] | None = None) -> None:
        self.outcomes = list(outcomes or [])
        self.calls: list[dict[str, Any]] = []

    def create(self, **kwargs: Any) -> Any:
        self.calls.append(kwargs)
        if not self.outcomes:
            raise AssertionError("Unexpected fake Responses API call.")
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome


class FakeClient:
    def __init__(self, outcomes: list[object] | None = None) -> None:
        self.responses = FakeResponses(outcomes)


def _provider(*outcomes: object) -> tuple[OpenAIProvider, FakeClient]:
    client = FakeClient(list(outcomes))
    provider = OpenAIProvider(
        api_key=TEST_API_KEY,
        model=TEST_MODEL,
        client=client,
        request_id_factory=lambda: "request-test-123",
    )
    return provider, client


def test_each_operation_uses_its_only_allowed_schema_and_minimal_snapshot(
    caplog: pytest.LogCaptureFixture,
) -> None:
    utterance = UtteranceSpec(
        trigger=NpcExpressionTrigger.COMMAND_ACCEPTED,
        mood=NpcMood.HAPPY,
        emoji="🙂",
        text="收到，我会照顾好这片农田。",
        provider="openai",
    )
    reflection = ReflectionSpec(
        goal_id="full_field_carrot_lifecycle",
        outcome=ReflectionOutcome.COMPLETED,
        mood=NpcMood.PROUD,
        emoji="★",
        text="九块农田已经全部完成。",
        provider="openai",
    )
    provider, client = _provider(
        _completed_response(_goal(), "response-goal"),
        _completed_response(utterance, "response-utterance"),
        _completed_response(reflection, "response-reflection"),
    )
    utterance_request = GenerateUtteranceRequest(
        resident_id="resident-001",
        trigger="CommandAccepted",
        context="玩家刚刚下达任务。",
    )
    reflection_request = ReflectRequest(
        resident_id="resident-001",
        goal=_goal(),
        outcome="Completed",
        event_summary="确定性检查确认九块农田均已收获。",
    )

    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        assert provider.interpret_command(_command_request()) == _goal()
        assert provider.generate_utterance(utterance_request) == utterance
        assert provider.reflect(reflection_request) == reflection

    calls = client.responses.calls
    assert [call["model"] for call in calls] == [TEST_MODEL] * 3
    assert [call["text"]["format"]["type"] for call in calls] == [
        "json_schema",
        "json_schema",
        "json_schema",
    ]
    assert calls[0]["text"]["format"]["schema"] == FarmGoalSpec.model_json_schema()
    assert calls[1]["text"]["format"]["schema"] == UtteranceSpec.model_json_schema()
    assert calls[2]["text"]["format"]["schema"] == ReflectionSpec.model_json_schema()

    assert json.loads(calls[0]["input"]) == {
        "resident_id": "resident-001",
        "command": SUPPORTED_COMMAND,
    }
    assert json.loads(calls[1]["input"]) == {
        "resident_id": "resident-001",
        "trigger": "CommandAccepted",
        "context": "玩家刚刚下达任务。",
    }
    assert json.loads(calls[2]["input"]) == {
        "resident_id": "resident-001",
        "goal_id": "full_field_carrot_lifecycle",
        "outcome": "Completed",
        "event_summary": "确定性检查确认九块农田均已收获。",
    }
    assert "target_plot_numbers" not in calls[2]["input"]
    assert "requires_sowing" not in calls[2]["input"]

    assert "request_id=request-test-123" in caplog.text
    assert "result_type=FarmGoalSpec" in caplog.text
    assert "elapsed_ms=" in caplog.text
    assert TEST_API_KEY not in caplog.text


def test_openai_sdk_serializes_responses_schema_without_real_network() -> None:
    captured_request: dict[str, Any] = {}

    def handle_request(request: httpx.Request) -> httpx.Response:
        captured_request["url"] = str(request.url)
        captured_request["body"] = json.loads(request.content)
        return httpx.Response(
            200,
            request=request,
            json={
                "id": "response-sdk-test",
                "object": "response",
                "created_at": 0,
                "status": "completed",
                "model": TEST_MODEL,
                "output": [
                    {
                        "id": "message-sdk-test",
                        "type": "message",
                        "status": "completed",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": _goal().model_dump_json(),
                                "annotations": [],
                            }
                        ],
                    }
                ],
                "parallel_tool_calls": False,
                "tool_choice": "auto",
                "tools": [],
            },
        )

    transport = httpx.MockTransport(handle_request)
    with httpx.Client(transport=transport) as http_client:
        sdk_client = OpenAI(
            api_key=TEST_API_KEY,
            base_url=providers.DEEPSEEK_BASE_URL,
            http_client=http_client,
            max_retries=0,
        )
        provider = OpenAIProvider(
            api_key=TEST_API_KEY,
            model=TEST_MODEL,
            client=sdk_client,
        )

        result = provider.interpret_command(_command_request())

    assert result == _goal()
    assert captured_request["url"] == "https://api.deepseek.com/responses"
    body = captured_request["body"]
    assert body["model"] == TEST_MODEL
    assert body["text"]["format"]["type"] == "json_schema"
    assert body["text"]["format"]["name"] == "farm_goal_spec"
    assert body["text"]["format"]["schema"] == FarmGoalSpec.model_json_schema()


def _timeout_error() -> APITimeoutError:
    return APITimeoutError(request=httpx.Request("POST", "https://example.invalid"))


def _connection_error() -> APIConnectionError:
    return APIConnectionError(
        message="Connection failed.",
        request=httpx.Request("POST", "https://example.invalid"),
    )


def _rate_limit_error() -> RateLimitError:
    request = httpx.Request("POST", "https://example.invalid")
    response = httpx.Response(429, request=request)
    return RateLimitError("Rate limited.", response=response, body=None)


def _authentication_error() -> AuthenticationError:
    request = httpx.Request("POST", "https://example.invalid")
    response = httpx.Response(401, request=request)
    return AuthenticationError("Invalid API key.", response=response, body=None)


@pytest.mark.parametrize(
    "error",
    [_timeout_error(), _connection_error(), _rate_limit_error()],
    ids=["timeout", "network", "rate-limit"],
)
def test_transient_openai_errors_fall_back_without_logging_the_key(
    error: Exception,
    caplog: pytest.LogCaptureFixture,
) -> None:
    provider, _ = _provider(error)

    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        result = provider.interpret_command(_command_request())

    assert result == _goal()
    assert "source=mock" in caplog.text
    assert f"fallback_reason={type(error).__name__}" in caplog.text
    assert "result_type=FarmGoalSpec" in caplog.text
    assert TEST_API_KEY not in caplog.text


def test_refusal_falls_back_to_mock_provider(
    caplog: pytest.LogCaptureFixture,
) -> None:
    refusal = SimpleNamespace(
        id="response-refusal",
        status="completed",
        incomplete_details=None,
        output=[
            SimpleNamespace(
                content=[SimpleNamespace(type="refusal", refusal="Cannot comply.")]
            )
        ],
        output_text="",
    )
    provider, _ = _provider(refusal)

    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        result = provider.interpret_command(_command_request())

    assert result == _goal()
    assert "fallback_reason=ProviderRefusalError" in caplog.text
    assert "source=mock" in caplog.text


def test_structured_output_is_revalidated_with_pydantic_before_returning(
    caplog: pytest.LogCaptureFixture,
) -> None:
    invalid_goal = {
        **_goal().model_dump(mode="json"),
        "target_plot_numbers": list(range(1, 9)),
    }
    provider, _ = _provider(
        _completed_response(json.dumps(invalid_goal, ensure_ascii=False), "response-invalid")
    )

    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        result = provider.interpret_command(_command_request())

    assert result == _goal()
    assert "fallback_reason=ValidationError" in caplog.text
    assert "source=mock" in caplog.text


def test_four_residents_share_one_provider_and_model_configuration() -> None:
    residents = [
        ("resident-001", "芽芽"),
        ("resident-002", "阿木"),
        ("resident-003", "小穗"),
        ("resident-004", "墨墨"),
    ]
    provider, client = _provider(
        *[
            _completed_response(
                ResidentDecisionSpec(
                    resident_id=resident_id,
                    intent="Idle",
                    target_resident_id=None,
                    reason="当前没有更高优先级的活动。",
                    provider="openai",
                ),
                f"response-{resident_id}",
            )
            for resident_id, _ in residents
        ]
    )

    results = [
        provider.decide_resident(
            _decision_request(
                resident_id,
                display_name,
                f"{display_name}的私有记忆",
            )
        )
        for resident_id, display_name in residents
    ]

    assert provider.model == TEST_MODEL
    assert [result.resident_id for result in results] == [
        resident_id for resident_id, _ in residents
    ]
    assert [call["model"] for call in client.responses.calls] == [TEST_MODEL] * 4
    assert all("model" not in json.loads(call["input"]) for call in client.responses.calls)


def test_resident_requests_do_not_mix_persona_or_private_memory() -> None:
    first = _decision_request("resident-001", "芽芽", "SECRET-YAYA")
    second = _decision_request("resident-002", "阿木", "SECRET-AMU")
    provider, client = _provider(
        _completed_response(
            ResidentDecisionSpec(
                resident_id="resident-001",
                intent="Idle",
                target_resident_id=None,
                reason="等待下一项日程。",
                provider="openai",
            ),
            "response-yaya",
        ),
        _completed_response(
            ResidentDecisionSpec(
                resident_id="resident-002",
                intent="Idle",
                target_resident_id=None,
                reason="等待下一项日程。",
                provider="openai",
            ),
            "response-amu",
        ),
    )

    provider.decide_resident(first)
    provider.decide_resident(second)

    first_input = client.responses.calls[0]["input"]
    second_input = client.responses.calls[1]["input"]
    assert json.loads(first_input)["context"]["persona"]["display_name"] == "芽芽"
    assert json.loads(second_input)["context"]["persona"]["display_name"] == "阿木"
    assert "SECRET-YAYA" in first_input
    assert "SECRET-YAYA" not in second_input
    assert "SECRET-AMU" in second_input
    assert "SECRET-AMU" not in first_input


def test_unknown_conversation_speaker_falls_back_to_mock(
    caplog: pytest.LogCaptureFixture,
) -> None:
    request = _conversation_request(max_lines=4)
    invalid_script = ConversationScriptSpec(
        resident_id=request.resident_id,
        lines=[
            ConversationLineSpec(
                speaker_id="resident-999",
                mood=NpcMood.HAPPY,
                emoji="💬",
                text="这句话来自不存在的参与者。",
            ),
            ConversationLineSpec(
                speaker_id="resident-002",
                mood=NpcMood.FOCUSED,
                emoji="🙂",
                text="这句话本身合法。",
            ),
        ],
        outcome=ConversationOutcome.NEUTRAL,
        provider="openai",
    )
    provider, _ = _provider(_completed_response(invalid_script, "response-bad-speaker"))

    with caplog.at_level(logging.INFO, logger="uvicorn.error"):
        result = provider.generate_conversation_script(request)

    assert result.provider == "mock"
    assert {line.speaker_id for line in result.lines} <= set(request.participant_ids)
    assert "fallback_reason=ProviderResponseError" in caplog.text


def test_illegal_decision_intent_and_target_each_fall_back_to_mock() -> None:
    request = _decision_request(
        "resident-001",
        "芽芽",
        "芽芽的记忆",
        allowed_intents=["Idle"],
        allowed_targets=["resident-002"],
    )
    invalid_intent = ResidentDecisionSpec(
        resident_id=request.resident_id,
        intent="StartCombat",
        target_resident_id=None,
        reason="非法意图。",
        provider="openai",
    )
    invalid_target = ResidentDecisionSpec(
        resident_id=request.resident_id,
        intent="Idle",
        target_resident_id="resident-004",
        reason="非法目标。",
        provider="openai",
    )
    provider, _ = _provider(
        _completed_response(invalid_intent, "response-bad-intent"),
        _completed_response(invalid_target, "response-bad-target"),
    )

    intent_result = provider.decide_resident(request)
    target_result = provider.decide_resident(request)

    assert intent_result.provider == "mock"
    assert intent_result.intent == "Idle"
    assert target_result.provider == "mock"
    assert target_result.target_resident_id is None


def test_conversation_over_six_lines_fails_validation_and_falls_back() -> None:
    request = _conversation_request(max_lines=6)
    invalid_payload = {
        "resident_id": request.resident_id,
        "lines": [
            {
                "speaker_id": request.participant_ids[index % 2],
                "mood": "Happy",
                "emoji": "💬",
                "text": f"第 {index + 1} 句话。",
            }
            for index in range(7)
        ],
        "outcome": "Neutral",
        "provider": "openai",
    }
    provider, _ = _provider(
        _completed_response(
            json.dumps(invalid_payload, ensure_ascii=False),
            "response-too-long",
        )
    )

    result = provider.generate_conversation_script(request)

    assert result.provider == "mock"
    assert len(result.lines) == 6


def test_timeout_and_auth_failure_do_not_block_other_residents() -> None:
    third_result = ResidentDecisionSpec(
        resident_id="resident-003",
        intent="Idle",
        target_resident_id=None,
        reason="第三名居民的请求仍然正常完成。",
        provider="openai",
    )
    provider, client = _provider(
        _timeout_error(),
        _authentication_error(),
        _completed_response(third_result, "response-resident-003"),
    )

    first = provider.decide_resident(
        _decision_request("resident-001", "芽芽", "芽芽记忆")
    )
    second = provider.decide_resident(
        _decision_request("resident-002", "阿木", "阿木记忆")
    )
    third = provider.decide_resident(
        _decision_request("resident-003", "小穗", "小穗记忆")
    )

    assert first.provider == "mock"
    assert second.provider == "mock"
    assert third == third_result
    assert len(client.responses.calls) == 3


def test_environment_selection_uses_configured_model_and_never_exposes_key(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured_client_options: dict[str, Any] = {}
    fake_client = FakeClient()

    def fake_openai(**kwargs: Any) -> FakeClient:
        captured_client_options.update(kwargs)
        return fake_client

    monkeypatch.setattr(providers, "OpenAI", fake_openai)
    monkeypatch.setenv("AIFARM_PROVIDER", "openai")
    monkeypatch.setenv("OPENAI_MODEL", TEST_MODEL)
    monkeypatch.setenv("OPENAI_API_KEY", TEST_API_KEY)

    gateway = _default_provider()

    assert isinstance(gateway, OpenAIProvider)
    assert gateway.model == TEST_MODEL
    assert captured_client_options["api_key"] == TEST_API_KEY
    assert captured_client_options["base_url"] == providers.DEEPSEEK_BASE_URL
    assert captured_client_options["max_retries"] == 0
    assert TEST_API_KEY not in repr(gateway)

    with TestClient(create_app(gateway)) as client:
        response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "provider": "openai",
        "api_key_required": True,
        "api_key_configured": True,
    }
    assert TEST_API_KEY not in response.text


def test_default_provider_never_constructs_a_network_client(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    def fail_if_constructed(**kwargs: Any) -> None:
        del kwargs
        raise AssertionError("Default tests must not construct the real client.")

    monkeypatch.delenv("AIFARM_PROVIDER", raising=False)
    monkeypatch.setenv(
        "AIFARM_LOCAL_CONFIG_PATH",
        str(tmp_path / "unconfigured-gateway.json"),
    )
    monkeypatch.setattr(providers, "OpenAI", fail_if_constructed)

    gateway = _default_provider()

    assert isinstance(gateway, MockProvider)


@pytest.mark.skipif(
    os.getenv("RUN_DEEPSEEK_INTEGRATION") != "1",
    reason="Set RUN_DEEPSEEK_INTEGRATION=1 to call the real DeepSeek API.",
)
def test_live_deepseek_v4_flash_structured_output() -> None:
    if not os.getenv("OPENAI_API_KEY", "").strip():
        pytest.fail("OPENAI_API_KEY must be set for the live DeepSeek test.")
    if os.getenv("OPENAI_MODEL") != TEST_MODEL:
        pytest.fail(f"OPENAI_MODEL must be {TEST_MODEL} for this live test.")

    provider = OpenAIProvider.from_environment()
    result = provider.generate_utterance(
        GenerateUtteranceRequest(
            resident_id="resident-001",
            trigger="CommandAccepted",
            context="真实 API 冒烟测试。",
        )
    )

    assert isinstance(result, UtteranceSpec)
    assert result.provider == "openai"
