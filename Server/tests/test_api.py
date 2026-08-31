import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from Server.app.main import create_app
from Server.app.schemas import FarmGoalSpec


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


@pytest.fixture
def client() -> TestClient:
    with TestClient(create_app()) as test_client:
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
    for schema_name in ("FarmGoalSpec", "UtteranceSpec", "ReflectionSpec"):
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
    response = client.post("/v1/interpret-command", json={"command": command})

    assert response.status_code == 200
    assert response.json() == FULL_GOAL


def test_interpret_command_rejects_unsupported_intent(client: TestClient) -> None:
    response = client.post("/v1/interpret-command", json={"command": "帮我种土豆"})

    assert response.status_code == 422
    assert response.json()["error"] == {
        "code": "unsupported_intent",
        "message": "MockProvider only supports the full 3×3 carrot lifecycle goal.",
        "details": [],
    }


@pytest.mark.parametrize(
    ("payload", "expected_location"),
    [
        ({"command": "   "}, "body.command"),
        ({"command": 123}, "body.command"),
        ({"command": "把这块地照顾好。", "actions": []}, "body.actions"),
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
        json={"trigger": trigger, "context": "测试上下文"},
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
        json={"trigger": "Dancing", "context": ""},
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
