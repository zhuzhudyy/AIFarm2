import os
from collections.abc import Sequence

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from Server.app.providers import (
    FarmProvider,
    MockProvider,
    OpenAIProvider,
    ProviderInputError,
)
from Server.app.schemas import (
    FarmGoalSpec,
    GenerateUtteranceRequest,
    HealthSpec,
    InterpretCommandRequest,
    ReflectRequest,
    ReflectionSpec,
    UtteranceSpec,
)


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
    provider_name = os.getenv("AIFARM_PROVIDER", "mock").strip().lower()
    if provider_name == "mock":
        return MockProvider()
    if provider_name == "openai":
        return OpenAIProvider.from_environment()
    raise RuntimeError("AIFARM_PROVIDER must be either 'mock' or 'openai'.")


def create_app(provider: FarmProvider | None = None) -> FastAPI:
    gateway = provider or _default_provider()
    application = FastAPI(
        title="AIFarm Local AI Gateway",
        version="0.1.0",
        description=(
            "Strict AI gateway with validated outputs; it never mutates Unity state."
        ),
    )

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
        return HealthSpec(
            status="ok",
            provider=gateway.name,
            api_key_required=gateway.requires_api_key,
            api_key_configured=gateway.api_key_configured,
        )

    @application.post("/v1/interpret-command", response_model=FarmGoalSpec)
    def interpret_command(request: InterpretCommandRequest) -> FarmGoalSpec:
        return gateway.interpret_command(request.command)

    @application.post("/v1/generate-utterance", response_model=UtteranceSpec)
    def generate_utterance(request: GenerateUtteranceRequest) -> UtteranceSpec:
        return gateway.generate_utterance(request)

    @application.post("/v1/reflect", response_model=ReflectionSpec)
    def reflect(request: ReflectRequest) -> ReflectionSpec:
        return gateway.reflect(request)

    return application


app = create_app()
