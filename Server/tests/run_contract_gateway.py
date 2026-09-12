"""Isolated HTTP transport verification for Unity. This is NOT a real model.

Run from Server: .venv/Scripts/python.exe -m tests.run_contract_gateway
The ephemeral fake upstream and port 8011 gateway never load player credentials.
"""

import os
import tempfile
from pathlib import Path

os.environ["AIFARM_PROVIDER"] = "mock"
os.environ["AIFARM_IGNORE_SAVED_CONFIG"] = "1"
os.environ["AIFARM_LOCAL_CONFIG_PATH"] = str(Path(tempfile.mkdtemp(prefix="aifarm-contract-")) / "config.json")

import uvicorn
from fastapi import Request
from app.main import create_app
from app.gateway_config import LocalGatewayConfigStore
from app.providers import OpenAIProvider
from .test_protocol_contracts import upstream


def main():
    fixture = upstream.__wrapped__()
    state = next(fixture)
    provider = OpenAIProvider(api_key="", model="aifarm-contract-test", base_url=state.url)
    application = create_app(provider, LocalGatewayConfigStore(Path(os.environ["AIFARM_LOCAL_CONFIG_PATH"])))

    @application.get("/contract-evidence")
    def evidence():
        # No authentication headers or private context are exposed by this endpoint.
        results = []
        for item in state.requests:
            body = item["body"]
            try:
                import json
                snapshot = json.loads(body.get("messages", [{}])[-1].get("content", "{}"))
            except ValueError:
                snapshot = {}
            results.append({"endpoint": item["path"], "model": body.get("model"), "resident_id": snapshot.get("resident_id"),
                "allowed_intents": snapshot.get("allowed_intents"), "participant_ids": snapshot.get("participant_ids"), "command": snapshot.get("command")})
        return {"test_only": True, "real_model": False, "upstream": state.url, "requests": results}

    @application.middleware("http")
    async def log_validation_errors(request: Request, call_next):
        response = await call_next(request)
        if response.status_code == 422:
            print("CONTRACT HTTP 422 endpoint=" + request.url.path, flush=True)
        return response

    print("CONTRACT TEST ONLY; real_model=false; gateway=http://127.0.0.1:8011; upstream=" + state.url, flush=True)
    try:
        uvicorn.run(application, host="127.0.0.1", port=8011, access_log=False)
    finally:
        fixture.close()


if __name__ == "__main__":
    main()
