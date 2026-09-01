import ipaddress
import logging
import secrets
from collections.abc import Sequence

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import HTMLResponse, JSONResponse

from app.gateway_config import GatewayProviderRuntime, LocalGatewayConfigStore

from app.providers import (
    FarmProvider,
    ProviderInputError,
)
from app.schemas import (
    ConversationScriptRequest,
    ConversationScriptSpec,
    FarmGoalSpec,
    GatewayConfigSpec,
    GatewayConfigureRequest,
    GatewayProbeSpec,
    GenerateUtteranceRequest,
    HealthSpec,
    InterpretCommandRequest,
    ReflectRequest,
    ReflectionSpec,
    ResidentDecisionRequest,
    ResidentDecisionSpec,
    ResidentReflectionRequest,
    ResidentReflectionSpec,
    UtteranceSpec,
)
from app.setup_ui import render_setup_page


logger = logging.getLogger("aifarm.ai_gateway")
_CSRF_COOKIE_NAME = "aifarm_gateway_csrf"
_CSRF_HEADER_NAME = "X-AIFarm-CSRF"


def _validation_details(errors: Sequence[dict]) -> list[dict[str, str]]:
    details: list[dict[str, str]] = []
    for error in errors:
        location = ".".join(str(part) for part in error.get("loc", ()))
        details.append(
            {
                "location": location,
                "message": str(error.get("msg", "Invalid value.")),
                "type": str(error.get("type", "validation_error")),
            }
        )
    return details


def _default_provider() -> FarmProvider:
    return GatewayProviderRuntime().provider


def _is_loopback_host(host: str | None) -> bool:
    normalized = (host or "").strip().lower()
    if normalized in {"localhost", "testclient", "testserver"}:
        return True
    try:
        return ipaddress.ip_address(normalized).is_loopback
    except ValueError:
        return False


def _require_loopback(request: Request) -> None:
    client_host = request.client.host if request.client is not None else ""
    if not _is_loopback_host(client_host) or not _is_loopback_host(
        request.url.hostname
    ):
        raise HTTPException(
            status_code=403,
            detail="Gateway configuration is available only from this computer.",
        )


def _require_same_origin_csrf(request: Request) -> None:
    _require_loopback(request)
    cookie_token = request.cookies.get(_CSRF_COOKIE_NAME, "")
    header_token = request.headers.get(_CSRF_HEADER_NAME, "")
    if (
        not cookie_token
        or not header_token
        or not secrets.compare_digest(cookie_token, header_token)
    ):
        raise HTTPException(status_code=403, detail="Invalid configuration token.")

    origin = request.headers.get("origin", "").rstrip("/")
    expected_origin = str(request.base_url).rstrip("/")
    if not origin or origin != expected_origin:
        raise HTTPException(status_code=403, detail="Configuration origin is invalid.")


def create_app(
    provider: FarmProvider | None = None,
    config_store: LocalGatewayConfigStore | None = None,
) -> FastAPI:
    runtime = GatewayProviderRuntime(provider, config_store)
    application = FastAPI(
        title="AIFarm Local AI Gateway",
        version="0.1.0",
        description=(
            "Strict AI gateway with validated outputs; it never mutates Unity state."
        ),
    )
    application.state.provider_runtime = runtime
    application.state.provider = runtime.provider

    @application.exception_handler(RequestValidationError)
    async def handle_validation_error(
        request: Request,
        exception: RequestValidationError,
    ) -> JSONResponse:
        del request
        return JSONResponse(
            status_code=422,
            content={
                "error": {
                    "code": "validation_error",
                    "message": "Request validation failed.",
                    "details": _validation_details(exception.errors()),
                }
            },
        )

    @application.exception_handler(ProviderInputError)
    async def handle_provider_input_error(
        request: Request,
        exception: ProviderInputError,
    ) -> JSONResponse:
        del request
        return JSONResponse(
            status_code=422,
            content={
                "error": {
                    "code": exception.code,
                    "message": exception.message,
                    "details": [],
                }
            },
        )

    @application.get("/health", response_model=HealthSpec)
    async def health() -> HealthSpec:
        gateway = runtime.provider
        return HealthSpec(
            status="ok",
            provider=gateway.name,
            api_key_required=gateway.requires_api_key,
            api_key_configured=gateway.api_key_configured,
        )

    @application.get("/setup", response_class=HTMLResponse, include_in_schema=False)
    async def setup(request: Request) -> HTMLResponse:
        _require_loopback(request)
        csrf_token = secrets.token_urlsafe(32)
        script_nonce = secrets.token_urlsafe(24)
        response = HTMLResponse(render_setup_page(csrf_token, script_nonce))
        response.set_cookie(
            _CSRF_COOKIE_NAME,
            csrf_token,
            httponly=True,
            max_age=15 * 60,
            path="/",
            samesite="strict",
            secure=request.url.scheme == "https",
        )
        response.headers["Cache-Control"] = "no-store"
        response.headers["Pragma"] = "no-cache"
        response.headers["Referrer-Policy"] = "no-referrer"
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["X-Frame-Options"] = "DENY"
        response.headers["Content-Security-Policy"] = (
            "default-src 'none'; "
            "base-uri 'none'; "
            "connect-src 'self'; "
            "form-action 'self'; "
            "frame-ancestors 'none'; "
            "img-src 'self' data:; "
            f"script-src 'nonce-{script_nonce}'; "
            "style-src 'unsafe-inline'"
        )
        return response

    @application.get("/v1/gateway-config", response_model=GatewayConfigSpec)
    async def gateway_config(request: Request) -> GatewayConfigSpec:
        _require_loopback(request)
        return runtime.status()

    @application.post("/v1/gateway-config", response_model=GatewayConfigSpec)
    def configure_gateway(
        request: Request,
        configuration: GatewayConfigureRequest,
    ) -> GatewayConfigSpec:
        _require_same_origin_csrf(request)
        try:
            status = runtime.configure(configuration)
        except OSError as error:
            raise HTTPException(
                status_code=500,
                detail="Unable to save the local gateway configuration.",
            ) from error
        application.state.provider = runtime.provider
        logger.info(
            "gateway_config_updated provider=%s model=%s persisted=%s",
            status.provider,
            status.model or "-",
            status.persisted,
        )
        return status

    @application.post(
        "/v1/gateway-config/probe",
        response_model=GatewayProbeSpec,
    )
    def probe_gateway(request: Request) -> GatewayProbeSpec:
        _require_same_origin_csrf(request)
        return runtime.probe()

    @application.post(
        "/v1/gateway-config/clear",
        response_model=GatewayConfigSpec,
    )
    def clear_gateway_config(request: Request) -> GatewayConfigSpec:
        _require_same_origin_csrf(request)
        try:
            status = runtime.clear()
        except OSError as error:
            raise HTTPException(
                status_code=500,
                detail="Unable to clear the local gateway configuration.",
            ) from error
        application.state.provider = runtime.provider
        logger.info("gateway_config_cleared provider=mock")
        return status

    @application.post("/v1/interpret-command", response_model=FarmGoalSpec)
    def interpret_command(request: InterpretCommandRequest) -> FarmGoalSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=interpret-command",
            request.resident_id,
        )
        response = gateway.interpret_command(request)
        logger.info(
            "ai_response resident_id=%s operation=interpret-command provider=%s",
            request.resident_id,
            gateway.name,
        )
        return response

    @application.post("/v1/generate-utterance", response_model=UtteranceSpec)
    def generate_utterance(request: GenerateUtteranceRequest) -> UtteranceSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=generate-utterance",
            request.resident_id,
        )
        response = gateway.generate_utterance(request)
        logger.info(
            "ai_response resident_id=%s operation=generate-utterance provider=%s",
            request.resident_id,
            gateway.name,
        )
        return response

    @application.post("/v1/reflect", response_model=ReflectionSpec)
    def reflect(request: ReflectRequest) -> ReflectionSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=reflect",
            request.resident_id,
        )
        response = gateway.reflect(request)
        logger.info(
            "ai_response resident_id=%s operation=reflect provider=%s",
            request.resident_id,
            gateway.name,
        )
        return response

    @application.post(
        "/v1/resident-decision",
        response_model=ResidentDecisionSpec,
    )
    def resident_decision(
        request: ResidentDecisionRequest,
    ) -> ResidentDecisionSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=resident-decision",
            request.resident_id,
        )
        response = gateway.decide_resident(request)
        logger.info(
            "ai_response resident_id=%s operation=resident-decision provider=%s",
            request.resident_id,
            response.provider,
        )
        return response

    @application.post(
        "/v1/conversation-script",
        response_model=ConversationScriptSpec,
    )
    def conversation_script(
        request: ConversationScriptRequest,
    ) -> ConversationScriptSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=conversation-script participants=%s",
            request.resident_id,
            ",".join(request.participant_ids),
        )
        response = gateway.generate_conversation_script(request)
        logger.info(
            "ai_response resident_id=%s operation=conversation-script provider=%s",
            request.resident_id,
            response.provider,
        )
        return response

    @application.post(
        "/v1/resident-reflection",
        response_model=ResidentReflectionSpec,
    )
    def resident_reflection(
        request: ResidentReflectionRequest,
    ) -> ResidentReflectionSpec:
        gateway = runtime.provider
        logger.info(
            "ai_request resident_id=%s operation=resident-reflection",
            request.resident_id,
        )
        response = gateway.reflect_resident(request)
        logger.info(
            "ai_response resident_id=%s operation=resident-reflection provider=%s",
            request.resident_id,
            response.provider,
        )
        return response

    return application


app = create_app()
