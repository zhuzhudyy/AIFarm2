import logging

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import create_app
from app.providers import MockProvider
from app.schemas import FarmGoalSpec, ResidentContext


FULL_GOAL = {
    "goal_id": "full_field_carrot_lifecycle",
    "crop": "carrot",
    "target_plot_numbers": list(range(1, 10)),
    "requires_sowing": True,
    "requires_watering": True,
    "requires_fertilizing": True,
    "requires_weeding": True,
    "requires_harvesting": True,
    "summary": "完成 3×3 农田的胡萝卜全周期",
}


def _resident_context_payload(
    resident_id: str,
    display_name: str,
    memory_text: str,
    relationship_target: str | None = None,
) -> dict:
    relationships = []
    if relationship_target is not None:
        relationships.append(
            {
                "owner_resident_id": resident_id,
                "target_resident_id": relationship_target,
                "familiarity": 10,
                "trust": 5,
            }
        )
    return {
        "resident_id": resident_id,
        "persona": {
            "display_name": display_name,
            "role": f"{display_name}的角色",
            "personality_traits": ["可靠"],
            "speaking_style": "简短自然",
        },
        "current_state": "schedule=Working;activity=Gather",
        "relationship_snapshots": relationships,
        "relevant_memories": [
            {
                "owner_resident_id": resident_id,
                "text": memory_text,
                "importance": 7,
            }
        ],
    }


@pytest.fixture
def client() -> TestClient:
    with TestClient(create_app(MockProvider())) as test_client:
        yield test_client


def test_health_uses_mock_without_api_key(
    client: TestClient,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "provider": "mock",
        "api_key_required": False,
        "api_key_configured": False,
    }


def test_unknown_provider_mode_fails_with_clear_startup_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AIFARM_PROVIDER", "real")

    with pytest.raises(RuntimeError, match="either 'mock' or 'openai'"):
        create_app()


def test_openapi_marks_core_specs_as_closed_and_fully_required(
    client: TestClient,
) -> None:
    response = client.get("/openapi.json")

    assert response.status_code == 200
    schemas = response.json()["components"]["schemas"]
    for schema_name in (
        "FarmGoalSpec",
        "UtteranceSpec",
        "ReflectionSpec",
        "ResidentDecisionSpec",
        "ConversationScriptSpec",
        "ResidentReflectionSpec",
    ):
        schema = schemas[schema_name]
        assert schema["additionalProperties"] is False
        assert set(schema["required"]) == set(schema["properties"])


@pytest.mark.parametrize(
    "command",
    [
        "把地种满胡萝卜并照顾到收获。",
        "帮我种胡萝卜，记得浇水施肥除草，成熟后收掉。",
        "今天把所有空地种上并全部收成。",
        "种满之后全部收掉。",
        "把这块地照顾好。",
    ],
)
def test_interpret_command_returns_strict_full_field_goal(
    client: TestClient,
    command: str,
) -> None:
    response = client.post(
        "/v1/interpret-command",
        json={"resident_id": "resident-001", "command": command},
    )

    assert response.status_code == 200
    assert response.json() == FULL_GOAL


def test_interpret_command_rejects_unsupported_intent(client: TestClient) -> None:
    response = client.post(
        "/v1/interpret-command",
        json={"resident_id": "resident-001", "command": "帮我种土豆"},
    )

    assert response.status_code == 422
    assert response.json()["error"] == {
        "code": "unsupported_intent",
        "message": "MockProvider only supports the full 3×3 carrot lifecycle goal.",
        "details": [],
    }


@pytest.mark.parametrize(
    ("payload", "expected_location"),
    [
        ({"resident_id": "resident-001", "command": "   "}, "body.command"),
        ({"resident_id": "resident-001", "command": 123}, "body.command"),
        (
            {
                "resident_id": "resident-001",
                "command": "把这块地照顾好。",
                "actions": [],
            },
            "body.actions",
        ),
    ],
)
def test_interpret_command_returns_clear_validation_errors(
    client: TestClient,
    payload: dict,
    expected_location: str,
) -> None:
    response = client.post("/v1/interpret-command", json=payload)

    assert response.status_code == 422
    body = response.json()["error"]
    assert body["code"] == "validation_error"
    assert body["message"] == "Request validation failed."
    assert any(detail["location"] == expected_location for detail in body["details"])


def test_ai_request_and_response_logs_include_resident_id(
    client: TestClient,
    caplog: pytest.LogCaptureFixture,
) -> None:
    with caplog.at_level(logging.INFO, logger="aifarm.ai_gateway"):
        response = client.post(
            "/v1/interpret-command",
            json={
                "resident_id": "resident-test-a",
                "command": "把地种满胡萝卜并照顾到收获。",
            },
        )

    assert response.status_code == 200
    messages = [record.getMessage() for record in caplog.records]
    assert any(
        "ai_request resident_id=resident-test-a" in message
        for message in messages
    )
    assert any(
        "ai_response resident_id=resident-test-a" in message
        for message in messages
    )


def test_ai_request_rejects_invalid_resident_id(client: TestClient) -> None:
    response = client.post(
        "/v1/interpret-command",
        json={
            "resident_id": "芽芽",
            "command": "把地种满胡萝卜并照顾到收获。",
        },
    )

    assert response.status_code == 422
    assert any(
        detail["location"] == "body.resident_id"
        for detail in response.json()["error"]["details"]
    )


@pytest.mark.parametrize(
    ("trigger", "mood", "emoji"),
    [
        ("CommandAccepted", "Happy", "🙂"),
        ("SowingStarted", "Focused", "🌱"),
        ("WaterNeeded", "Worried", "💧"),
        ("WeedsFound", "Worried", "🌿"),
        ("ActionFailed", "Worried", "⚠"),
        ("WaitingForGrowth", "Tired", "⏳"),
        ("HarvestStarted", "Happy", "🥕"),
        ("GoalCompleted", "Proud", "★"),
    ],
)
def test_generate_utterance_covers_all_required_events(
    client: TestClient,
    trigger: str,
    mood: str,
    emoji: str,
) -> None:
    response = client.post(
        "/v1/generate-utterance",
        json={
            "resident_id": "resident-001",
            "trigger": trigger,
            "context": "测试上下文",
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["trigger"] == trigger
    assert body["mood"] == mood
    assert body["emoji"] == emoji
    assert body["text"].strip()
    assert body["provider"] == "mock"


def test_generate_utterance_rejects_unknown_trigger(client: TestClient) -> None:
    response = client.post(
        "/v1/generate-utterance",
        json={
            "resident_id": "resident-001",
            "trigger": "Dancing",
            "context": "",
        },
    )

    assert response.status_code == 422
    assert response.json()["error"]["code"] == "validation_error"


@pytest.mark.parametrize(
    ("outcome", "mood", "emoji", "prefix"),
    [
        ("InProgress", "Focused", "🌱", "任务仍在进行"),
        ("Completed", "Proud", "★", "任务已经完成"),
        ("Failed", "Worried", "⚠", "任务遇到问题"),
    ],
)
def test_reflect_returns_bounded_mock_reflection(
    client: TestClient,
    outcome: str,
    mood: str,
    emoji: str,
    prefix: str,
) -> None:
    response = client.post(
        "/v1/reflect",
        json={
            "resident_id": "resident-001",
            "goal": FULL_GOAL,
            "outcome": outcome,
            "event_summary": "已完成确定性世界状态检查。",
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["goal_id"] == FULL_GOAL["goal_id"]
    assert body["outcome"] == outcome
    assert body["mood"] == mood
    assert body["emoji"] == emoji
    assert body["text"].startswith(prefix)
    assert body["provider"] == "mock"


def test_reflect_rejects_non_full_field_goal(client: TestClient) -> None:
    invalid_goal = {**FULL_GOAL, "target_plot_numbers": list(range(1, 9))}

    response = client.post(
        "/v1/reflect",
        json={
            "resident_id": "resident-001",
            "goal": invalid_goal,
            "outcome": "InProgress",
            "event_summary": "仍在执行。",
        },
    )

    assert response.status_code == 422
    error = response.json()["error"]
    assert error["code"] == "validation_error"
    assert any(
        detail["location"] == "body.goal.target_plot_numbers"
        for detail in error["details"]
    )


def test_reflect_rejects_overlong_event_summary(client: TestClient) -> None:
    response = client.post(
        "/v1/reflect",
        json={
            "resident_id": "resident-001",
            "goal": FULL_GOAL,
            "outcome": "InProgress",
            "event_summary": "事" * 241,
        },
    )

    assert response.status_code == 422
    error = response.json()["error"]
    assert error["code"] == "validation_error"
    assert any(
        detail["location"] == "body.event_summary"
        for detail in error["details"]
    )


def test_resident_decision_uses_explicit_owner_and_allowed_sets(
    client: TestClient,
) -> None:
    response = client.post(
        "/v1/resident-decision",
        json={
            "resident_id": "resident-001",
            "context": _resident_context_payload(
                "resident-001",
                "芽芽",
                "芽芽的私有记忆",
            ),
            "situation": "当前没有紧急农务。",
            "allowed_intents": ["Idle"],
            "allowed_target_resident_ids": [],
        },
    )

    assert response.status_code == 200
    assert response.json() == {
        "resident_id": "resident-001",
        "intent": "Idle",
        "target_resident_id": None,
        "reason": "本地规则从本次请求的允许集合中选择安全的高层意图。",
        "provider": "mock",
    }


def test_conversation_script_carries_two_isolated_participant_contexts(
    client: TestClient,
) -> None:
    response = client.post(
        "/v1/conversation-script",
        json={
            "resident_id": "resident-001",
            "participant_ids": ["resident-001", "resident-002"],
            "participants": [
                _resident_context_payload(
                    "resident-001",
                    "芽芽",
                    "芽芽的私有记忆",
                    "resident-002",
                ),
                _resident_context_payload(
                    "resident-002",
                    "阿木",
                    "阿木的私有记忆",
                    "resident-001",
                ),
            ],
            "topic": "水井维护",
            "max_lines": 4,
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["resident_id"] == "resident-001"
    assert body["provider"] == "mock"
    assert body["outcome"] == "Neutral"
    assert len(body["lines"]) == 4
    assert all(line["mood"] for line in body["lines"])
    assert all(line["emoji"] for line in body["lines"])
    assert {line["speaker_id"] for line in body["lines"]} <= {
        "resident-001",
        "resident-002",
    }
    assert "芽芽" in body["lines"][0]["text"]
    assert "阿木" in body["lines"][0]["text"]


def test_mock_conversation_shares_only_the_speakers_allowlisted_knowledge(
    client: TestClient,
) -> None:
    yaya = _resident_context_payload(
        "resident-001",
        "芽芽",
        "芽芽完成了胡萝卜收获。",
        "resident-003",
    )
    yaya["relevant_memories"][0].update(
        {
            "knowledge_id": "resident-001:memory-1",
            "root_fact_id": "fact-carrot-harvest",
            "tags": ["carrot", "harvest"],
            "is_shareable": True,
            "immediate_source_resident_id": None,
        }
    )
    xiaosui = _resident_context_payload(
        "resident-003",
        "小穗",
        "小穗在图书馆整理记录。",
        "resident-001",
    )

    response = client.post(
        "/v1/conversation-script",
        json={
            "resident_id": "resident-001",
            "participant_ids": ["resident-001", "resident-003"],
            "participants": [yaya, xiaosui],
            "topic": "今天的收获",
            "max_lines": 2,
        },
    )

    assert response.status_code == 200
    lines = response.json()["lines"]
    assert lines[0]["speaker_id"] == "resident-001"
    assert lines[0]["shared_knowledge_id"] == "resident-001:memory-1"
    assert "胡萝卜收获" in lines[0]["text"]
    assert lines[1]["shared_knowledge_id"] is None


def test_resident_reflection_uses_only_the_owner_context(client: TestClient) -> None:
    response = client.post(
        "/v1/resident-reflection",
        json={
            "resident_id": "resident-003",
            "context": _resident_context_payload(
                "resident-003",
                "小穗",
                "小穗的私有记忆",
            ),
            "outcome": "Completed",
            "event_summary": "图书馆的记录已经整理完毕。",
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert body["resident_id"] == "resident-003"
    assert body["outcome"] == "Completed"
    assert body["provider"] == "mock"
    assert body["text"].startswith("小穗想到")


def test_resident_context_rejects_another_residents_private_memory() -> None:
    payload = _resident_context_payload(
        "resident-001",
        "芽芽",
        "不应出现在芽芽上下文中的记忆",
    )
    payload["relevant_memories"][0]["owner_resident_id"] = "resident-002"

    with pytest.raises(ValidationError, match="private memory owner"):
        ResidentContext.model_validate(payload)


@pytest.mark.parametrize(
    ("path", "payload"),
    [
        (
            "/v1/interpret-command",
            {"command": "把地种满胡萝卜并照顾到收获。"},
        ),
        (
            "/v1/generate-utterance",
            {"trigger": "CommandAccepted", "context": ""},
        ),
        (
            "/v1/reflect",
            {
                "goal": FULL_GOAL,
                "outcome": "Completed",
                "event_summary": "已完成。",
            },
        ),
        (
            "/v1/resident-decision",
            {
                "context": _resident_context_payload(
                    "resident-001",
                    "芽芽",
                    "芽芽的记忆",
                ),
                "allowed_intents": ["Idle"],
                "allowed_target_resident_ids": [],
            },
        ),
        (
            "/v1/conversation-script",
            {
                "participant_ids": ["resident-001", "resident-002"],
                "participants": [
                    _resident_context_payload(
                        "resident-001",
                        "芽芽",
                        "芽芽的记忆",
                        "resident-002",
                    ),
                    _resident_context_payload(
                        "resident-002",
                        "阿木",
                        "阿木的记忆",
                        "resident-001",
                    ),
                ],
            },
        ),
        (
            "/v1/resident-reflection",
            {
                "context": _resident_context_payload(
                    "resident-001",
                    "芽芽",
                    "芽芽的记忆",
                ),
                "outcome": "Completed",
                "event_summary": "已完成。",
            },
        ),
    ],
)
def test_all_ai_requests_require_resident_id(
    client: TestClient,
    path: str,
    payload: dict,
) -> None:
    response = client.post(path, json=payload)

    assert response.status_code == 422
    assert any(
        detail["location"] == "body.resident_id"
        for detail in response.json()["error"]["details"]
    )


def test_farm_goal_schema_forbids_type_coercion_and_extra_fields() -> None:
    with pytest.raises(ValidationError):
        FarmGoalSpec.model_validate(
            {
                **FULL_GOAL,
                "target_plot_numbers": [str(number) for number in range(1, 10)],
            }
        )

    with pytest.raises(ValidationError):
        FarmGoalSpec.model_validate({**FULL_GOAL, "low_level_actions": ["Sow"]})
