import json
from pathlib import Path
from types import SimpleNamespace

import httpx
import pytest
from fastapi.testclient import TestClient
from openai import AuthenticationError

from app.gateway_config import LocalGatewayConfigStore
from app.main import create_app
from app.providers import MockProvider, OpenAIProvider


TEST_MODEL = "deepseek-v4-flash"
TEST_KEY = "unit-test-secret-key"
TEST_INSTANCE_ID = "0123456789abcdef0123456789abcdef"
ORIGIN = "http://testserver"


def _csrf_headers(client: TestClient) -> dict[str, str]:
    response = client.get("/setup")
    assert response.status_code == 200
    return {
        "Origin": ORIGIN,
        "X-AIFarm-CSRF": client.cookies["aifarm_gateway_csrf"],
    }


def test_setup_page_is_self_contained_no_store_and_never_contains_the_key(
    tmp_path: Path,
) -> None:
    store = LocalGatewayConfigStore(tmp_path / "gateway.json")
    with TestClient(create_app(MockProvider(), store)) as client:
        headers = _csrf_headers(client)
        configured = client.post(
            "/v1/gateway-config",
            headers=headers,
            json={
                "provider": "openai",
                "model": TEST_MODEL,
                "api_key": TEST_KEY,
                "persist": True,
            },
        )
        page = client.get("/setup")

    assert configured.status_code == 200
    assert page.status_code == 200
    assert "AIFarm API 设置" in page.text
    assert "测试连接" in page.text
    assert TEST_KEY not in page.text
    assert page.headers["cache-control"] == "no-store"
    assert page.headers["x-frame-options"] == "DENY"
    assert "default-src 'none'" in page.headers["content-security-policy"]
    assert "script-src 'nonce-" in page.headers["content-security-policy"]
    assert "https://" not in page.text


def test_configuration_requires_loopback_same_origin_and_csrf(tmp_path: Path) -> None:
    store = LocalGatewayConfigStore(tmp_path / "gateway.json")
    application = create_app(MockProvider(), store)
    payload = {
        "provider": "mock",
        "model": TEST_MODEL,
        "api_key": None,
        "persist": True,
    }

    with TestClient(application) as client:
        assert client.post("/v1/gateway-config", json=payload).status_code == 403
        headers = _csrf_headers(client)
        assert client.post(
            "/v1/gateway-config",
            headers={**headers, "Origin": "https://attacker.invalid"},
            json=payload,
        ).status_code == 403

    with TestClient(
        application,
        client=("198.51.100.20", 52000),
    ) as remote_client:
        assert remote_client.get("/setup").status_code == 403
        assert remote_client.get("/v1/gateway-config").status_code == 403


def test_openai_configuration_hot_swaps_shared_provider_and_persists_secret(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    config_path = tmp_path / "gateway.json"
    store = LocalGatewayConfigStore(config_path)
    application = create_app(MockProvider(), store)

    with TestClient(application) as client:
        headers = _csrf_headers(client)
        response = client.post(
            "/v1/gateway-config",
            headers=headers,
            json={
                "provider": "openai",
                "model": TEST_MODEL,
                "api_key": TEST_KEY,
                "persist": True,
            },
        )
        health = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "provider": "openai",
        "model": TEST_MODEL,
        "api_key_required": False,
        "api_key_configured": True,
        "persisted": True,
        "source": "runtime",
        "base_url": "https://api.deepseek.com",
        "protocol": "chat_completions",
        "output_mode": "text",
        "endpoint": "https://api.deepseek.com/v1/chat/completions",
        "config_version": 2,
        "upstream_status": "connecting",
        "last_error_code": "",
        "last_error": "",
    }
    assert TEST_KEY not in response.text
    assert health.json()["provider"] == "openai"
    assert isinstance(application.state.provider, OpenAIProvider)
    assert application.state.provider.model == TEST_MODEL

    saved = json.loads(config_path.read_text(encoding="utf-8"))
    assert saved["api_key"] == TEST_KEY
    assert config_path.resolve().is_relative_to(tmp_path.resolve())

    monkeypatch.delenv("AIFARM_PROVIDER", raising=False)
    restarted = create_app(config_store=store)
    with TestClient(restarted) as client:
        restarted_status = client.get("/v1/gateway-config")
    assert restarted_status.json()["source"] == "local_config"
    assert restarted_status.json()["api_key_configured"] is True
    assert TEST_KEY not in restarted_status.text


def test_gateway_instance_id_is_visible_without_exposing_the_api_key(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AIFARM_GATEWAY_INSTANCE_ID", TEST_INSTANCE_ID)
    application = create_app(
        MockProvider(),
        LocalGatewayConfigStore(tmp_path / "gateway.json"),
    )

    with TestClient(application) as client:
        headers = _csrf_headers(client)
        configured = client.post(
            "/v1/gateway-config",
            headers=headers,
            json={
                "provider": "openai",
                "model": TEST_MODEL,
                "api_key": TEST_KEY,
                "persist": False,
            },
        )
        status = client.get("/v1/gateway-config")

    assert configured.status_code == 200
    assert status.status_code == 200
    assert configured.json()["instance_id"] == TEST_INSTANCE_ID
    assert status.json()["instance_id"] == TEST_INSTANCE_ID
    assert TEST_KEY not in configured.text
    assert TEST_KEY not in status.text


def test_unconfigured_gateway_instance_id_is_omitted_from_status(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("AIFARM_GATEWAY_INSTANCE_ID", raising=False)
    application = create_app(
        MockProvider(),
        LocalGatewayConfigStore(tmp_path / "gateway.json"),
    )

    with TestClient(application) as client:
        status = client.get("/v1/gateway-config")

    assert status.status_code == 200
    assert "instance_id" not in status.json()


@pytest.mark.parametrize(
    "invalid_instance_id",
    [
        "",
        "0123456789abcdef0123456789abcde",
        "0123456789abcdef0123456789abcdef0",
        "0123456789ABCDEF0123456789ABCDEF",
        "0123456789abcdef-123456789abcdef",
    ],
)
def test_invalid_gateway_instance_id_fails_at_startup(
    invalid_instance_id: str,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("AIFARM_GATEWAY_INSTANCE_ID", invalid_instance_id)

    with pytest.raises(
        RuntimeError,
        match="AIFARM_GATEWAY_INSTANCE_ID must be exactly 32 lowercase hexadecimal",
    ):
        create_app(
            MockProvider(),
            LocalGatewayConfigStore(tmp_path / "gateway.json"),
        )


def test_blank_key_reuses_current_secret_and_clear_removes_it(tmp_path: Path) -> None:
    config_path = tmp_path / "gateway.json"
    application = create_app(
        MockProvider(),
        LocalGatewayConfigStore(config_path),
    )

    with TestClient(application) as client:
        headers = _csrf_headers(client)
        first = client.post(
            "/v1/gateway-config",
            headers=headers,
            json={
                "provider": "openai",
                "model": TEST_MODEL,
                "api_key": TEST_KEY,
                "persist": True,
            },
        )
        second = client.post(
            "/v1/gateway-config",
            headers=headers,
            json={
                "provider": "openai",
                "model": "deepseek-v4-flash-next",
                "api_key": None,
                "persist": True,
            },
        )
        cleared = client.post(
            "/v1/gateway-config/clear",
            headers=headers,
        )

    assert first.status_code == 200
    assert second.status_code == 200
    assert second.json()["model"] == "deepseek-v4-flash-next"
    assert cleared.json()["provider"] == "mock"
    assert cleared.json()["api_key_configured"] is False
    assert not config_path.exists()


def test_openai_probe_requires_three_inferences_and_redacts_auth_error(
    caplog: pytest.LogCaptureFixture,
) -> None:
    calls = []
    def infer(**kwargs):
        calls.append(kwargs)
        if "text" not in kwargs:
            text = "你好。"
        else:
            snapshot = json.loads(kwargs["input"])
            if "allowed_intents" in snapshot:
                text = json.dumps({"resident_id":snapshot["resident_id"], "intent":"Walk", "target_resident_id":None, "reason":"出去走走", "provider":"openai"})
            else:
                text = json.dumps({"resident_id":snapshot["resident_id"], "lines":[{"speaker_id":owner,"mood":"Happy","emoji":"🙂","text":"农田刚浇过水，去果园看看。","shared_knowledge_id":None} for owner in snapshot["participant_ids"]], "outcome":"Neutral", "provider":"openai"})
        return SimpleNamespace(status="completed", output_text=text, output=[])
    successful_client = SimpleNamespace(responses=SimpleNamespace(create=infer))
    provider = OpenAIProvider(
        api_key=TEST_KEY,
        model=TEST_MODEL,
        client=successful_client,
    )

    success = provider.probe()

    request = httpx.Request("GET", "https://example.invalid/models")
    response = httpx.Response(401, request=request)

    def fail_authentication(**kwargs) -> None:
        raise AuthenticationError(
            f"Invalid credential {TEST_KEY}",
            response=response,
            body=None,
        )

    failing_client = SimpleNamespace(
        responses=SimpleNamespace(create=fail_authentication),
    )
    failing_provider = OpenAIProvider(
        api_key=TEST_KEY,
        model=TEST_MODEL,
        client=failing_client,
    )
    with caplog.at_level("INFO", logger="uvicorn.error"):
        failure = failing_provider.probe()

    assert success.ok is True
    assert success.code == "ok"
    assert len(calls) == 3
    assert [check["name"] for check in success.checks] == ["text", "resident_decision", "conversation"]
    assert failure.ok is False
    assert failure.code == "authentication_failed"
    assert TEST_KEY not in caplog.text


def test_offline_probe_completes_without_network(tmp_path: Path) -> None:
    application = create_app(
        MockProvider(),
        LocalGatewayConfigStore(tmp_path / "gateway.json"),
    )
    with TestClient(application) as client:
        response = client.post(
            "/v1/gateway-config/probe",
            headers=_csrf_headers(client),
        )

    assert response.status_code == 200
    assert response.json()["ok"] is False
    assert response.json()["code"] == "offline"
    assert response.json()["provider"] == "mock"
