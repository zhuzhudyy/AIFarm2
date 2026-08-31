import json
import logging
import os
import re
from collections.abc import Callable, Mapping
from time import perf_counter
from typing import Any, Protocol, TypeVar
from uuid import uuid4

from openai import OpenAI, OpenAIError
from pydantic import BaseModel, ValidationError

from app.schemas import (
    FarmGoalSpec,
    GenerateUtteranceRequest,
    NpcExpressionTrigger,
    NpcMood,
    ReflectRequest,
    ReflectionOutcome,
    ReflectionSpec,
    UtteranceSpec,
)


DEEPSEEK_BASE_URL = "https://api.deepseek.com"
DEFAULT_TIMEOUT_SECONDS = 15.0

_logger = logging.getLogger("uvicorn.error")
_OutputModel = TypeVar("_OutputModel", bound=BaseModel)


class FarmProvider(Protocol):
    name: str
    requires_api_key: bool
    api_key_configured: bool

    def interpret_command(self, command: str) -> FarmGoalSpec: ...

    def generate_utterance(
        self,
        request: GenerateUtteranceRequest,
    ) -> UtteranceSpec: ...

    def reflect(self, request: ReflectRequest) -> ReflectionSpec: ...


class ProviderInputError(ValueError):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


class ProviderResponseError(RuntimeError):
    """Raised internally when an upstream response cannot be used safely."""


class ProviderRefusalError(ProviderResponseError):
    """Raised internally when the upstream model refuses a request."""


class MockProvider:
    name = "mock"
    requires_api_key = False
    api_key_configured = False

    _supported_commands = (
        "把地种满胡萝卜并照顾到收获",
        "帮我种胡萝卜记得浇水施肥除草成熟后收掉",
        "今天把所有空地种上并全部收成",
        "种满之后全部收掉",
        "把这块地照顾好",
        "请把这块3×3农田全部种上胡萝卜并完成浇水施肥除草和收获",
        "麻烦你把这九块地照料成可以收获的胡萝卜吧",
    )

    _utterances = {
        NpcExpressionTrigger.COMMAND_ACCEPTED: (
            NpcMood.HAPPY,
            "🙂",
            "收到！我会把九块地照顾到全部收获。",
        ),
        NpcExpressionTrigger.SOWING_STARTED: (
            NpcMood.FOCUSED,
            "🌱",
            "种子准备好了，我开始逐块播种。",
        ),
        NpcExpressionTrigger.WATER_NEEDED: (
            NpcMood.WORRIED,
            "💧",
            "发现土壤缺水，我马上去补水。",
        ),
        NpcExpressionTrigger.WEEDS_FOUND: (
            NpcMood.WORRIED,
            "🌿",
            "发现杂草，我来清理，不能让它们抢养分。",
        ),
        NpcExpressionTrigger.ACTION_FAILED: (
            NpcMood.WORRIED,
            "⚠",
            "这一步没有成功，我会说明原因并停止错误操作。",
        ),
        NpcExpressionTrigger.WAITING_FOR_GROWTH: (
            NpcMood.TIRED,
            "⏳",
            "现在需要给胡萝卜一点成长时间。",
        ),
        NpcExpressionTrigger.HARVEST_STARTED: (
            NpcMood.HAPPY,
            "🥕",
            "胡萝卜成熟了，我开始收获。",
        ),
        NpcExpressionTrigger.GOAL_COMPLETED: (
            NpcMood.PROUD,
            "★",
            "九块地全部完成，胡萝卜已经收好！",
        ),
    }

    def interpret_command(self, command: str) -> FarmGoalSpec:
        normalized = self._normalize_command(command)
        if normalized not in self._supported_commands:
            raise ProviderInputError(
                "unsupported_intent",
                "MockProvider only supports the full 3×3 carrot lifecycle goal.",
            )

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

    def generate_utterance(self, request: GenerateUtteranceRequest) -> UtteranceSpec:
        trigger = NpcExpressionTrigger(request.trigger)
        mood, emoji, template = self._utterances[trigger]
        text = template
        if trigger is NpcExpressionTrigger.ACTION_FAILED and request.context:
            text = f"这一步没有成功：{request.context}"

        return UtteranceSpec(
            trigger=trigger,
            mood=mood,
            emoji=emoji,
            text=text,
            provider="mock",
        )

    def reflect(self, request: ReflectRequest) -> ReflectionSpec:
        outcome = ReflectionOutcome(request.outcome)
        if outcome is ReflectionOutcome.COMPLETED:
            mood = NpcMood.PROUD
            emoji = "★"
            text = f"任务已经完成：{request.event_summary}"
        elif outcome is ReflectionOutcome.FAILED:
            mood = NpcMood.WORRIED
            emoji = "⚠"
            text = f"任务遇到问题：{request.event_summary}"
        else:
            mood = NpcMood.FOCUSED
            emoji = "🌱"
            text = f"任务仍在进行：{request.event_summary}"

        return ReflectionSpec(
            goal_id=request.goal.goal_id,
            outcome=outcome,
            mood=mood,
            emoji=emoji,
            text=text,
            provider="mock",
        )

    @staticmethod
    def _normalize_command(command: str) -> str:
        return re.sub(r"[\s，。！？、,!.?：:；;]", "", command)


class OpenAIProvider:
    """DeepSeek provider implemented through its OpenAI-compatible Responses API."""

    name = "openai"
    requires_api_key = True
    api_key_configured = True

    _interpret_instructions = (
        "Interpret the bounded user command as a FarmGoalSpec. Return only JSON that "
        "matches the supplied schema. The only supported goal is the complete 3x3 "
        "carrot lifecycle represented by that schema. Refuse if the command cannot be "
        "represented without changing or adding fields."
    )
    _utterance_instructions = (
        "Generate exactly one short Chinese NPC utterance as an UtteranceSpec. Return "
        "only JSON that matches the supplied schema, preserve the supplied trigger, "
        "and set provider to openai. Do not include plans, actions, or world state."
    )
    _reflection_instructions = (
        "Generate exactly one short Chinese NPC reflection as a ReflectionSpec. Return "
        "only JSON that matches the supplied schema, preserve goal_id and outcome, and "
        "set provider to openai. Use only the bounded event summary supplied."
    )

    def __init__(
        self,
        *,
        api_key: str,
        model: str,
        client: Any | None = None,
        fallback: MockProvider | None = None,
        timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
        clock: Callable[[], float] = perf_counter,
        request_id_factory: Callable[[], str] | None = None,
    ) -> None:
        normalized_api_key = api_key.strip()
        normalized_model = model.strip()
        if not normalized_api_key:
            raise RuntimeError("OPENAI_API_KEY is required for AIFARM_PROVIDER=openai.")
        if not normalized_model:
            raise RuntimeError("OPENAI_MODEL is required for AIFARM_PROVIDER=openai.")

        self._model = normalized_model
        self._fallback = fallback or MockProvider()
        self._clock = clock
        self._request_id_factory = request_id_factory or (lambda: uuid4().hex)
        self._client = client or OpenAI(
            api_key=normalized_api_key,
            base_url=DEEPSEEK_BASE_URL,
            timeout=timeout_seconds,
            max_retries=0,
        )

    @property
    def model(self) -> str:
        return self._model

    @classmethod
    def from_environment(cls) -> "OpenAIProvider":
        return cls(
            api_key=os.getenv("OPENAI_API_KEY", ""),
            model=os.getenv("OPENAI_MODEL", ""),
        )

    def interpret_command(self, command: str) -> FarmGoalSpec:
        return self._request_structured_output(
            operation="interpret-command",
            instructions=self._interpret_instructions,
            snapshot={"command": command},
            response_model=FarmGoalSpec,
            schema_name="farm_goal_spec",
            fallback_call=lambda: self._fallback.interpret_command(command),
        )

    def generate_utterance(self, request: GenerateUtteranceRequest) -> UtteranceSpec:
        return self._request_structured_output(
            operation="generate-utterance",
            instructions=self._utterance_instructions,
            snapshot={
                "trigger": request.trigger,
                "context": request.context,
            },
            response_model=UtteranceSpec,
            schema_name="utterance_spec",
            fallback_call=lambda: self._fallback.generate_utterance(request),
        )

    def reflect(self, request: ReflectRequest) -> ReflectionSpec:
        return self._request_structured_output(
            operation="reflect",
            instructions=self._reflection_instructions,
            snapshot={
                "goal_id": request.goal.goal_id,
                "outcome": request.outcome,
                "event_summary": request.event_summary,
            },
            response_model=ReflectionSpec,
            schema_name="reflection_spec",
            fallback_call=lambda: self._fallback.reflect(request),
        )

    def _request_structured_output(
        self,
        *,
        operation: str,
        instructions: str,
        snapshot: Mapping[str, object],
        response_model: type[_OutputModel],
        schema_name: str,
        fallback_call: Callable[[], _OutputModel],
    ) -> _OutputModel:
        request_id = self._request_id_factory()
        started_at = self._clock()

        try:
            response = self._client.responses.create(
                model=self._model,
                instructions=instructions,
                input=json.dumps(
                    snapshot,
                    ensure_ascii=False,
                    separators=(",", ":"),
                ),
                text={
                    "format": {
                        "type": "json_schema",
                        "name": schema_name,
                        "schema": response_model.model_json_schema(),
                    }
                },
                max_output_tokens=512,
            )
            output_text = self._extract_output_text(response)
            result = response_model.model_validate_json(output_text)
            self._require_openai_provider_marker(result)
        except (
            ConnectionError,
            OpenAIError,
            ProviderResponseError,
            TimeoutError,
            ValidationError,
        ) as error:
            return self._use_fallback(
                operation=operation,
                request_id=request_id,
                started_at=started_at,
                reason=type(error).__name__,
                fallback_call=fallback_call,
            )

        self._log_result(
            operation=operation,
            request_id=request_id,
            started_at=started_at,
            result_type=type(result).__name__,
            source="openai",
            upstream_request_id=str(getattr(response, "id", "-")),
        )
        return result

    def _use_fallback(
        self,
        *,
        operation: str,
        request_id: str,
        started_at: float,
        reason: str,
        fallback_call: Callable[[], _OutputModel],
    ) -> _OutputModel:
        try:
            result = fallback_call()
        except Exception as fallback_error:
            self._log_result(
                operation=operation,
                request_id=request_id,
                started_at=started_at,
                result_type=type(fallback_error).__name__,
                source="error",
                fallback_reason=reason,
            )
            raise

        self._log_result(
            operation=operation,
            request_id=request_id,
            started_at=started_at,
            result_type=type(result).__name__,
            source="mock",
            fallback_reason=reason,
        )
        return result

    def _log_result(
        self,
        *,
        operation: str,
        request_id: str,
        started_at: float,
        result_type: str,
        source: str,
        fallback_reason: str = "-",
        upstream_request_id: str = "-",
    ) -> None:
        elapsed_ms = max(0.0, (self._clock() - started_at) * 1000)
        _logger.info(
            "provider_request request_id=%s operation=%s elapsed_ms=%.2f "
            "result_type=%s source=%s fallback_reason=%s upstream_request_id=%s",
            request_id,
            operation,
            elapsed_ms,
            result_type,
            source,
            fallback_reason,
            upstream_request_id,
        )

    @staticmethod
    def _extract_output_text(response: Any) -> str:
        status = OpenAIProvider._read_value(response, "status")
        if status == "failed":
            raise ProviderResponseError("The upstream response failed.")
        if status == "incomplete":
            details = OpenAIProvider._read_value(response, "incomplete_details")
            reason = OpenAIProvider._read_value(details, "reason")
            if reason == "content_filter":
                raise ProviderRefusalError("The upstream model refused the request.")
            raise ProviderResponseError("The upstream response was incomplete.")

        for item in OpenAIProvider._read_value(response, "output") or ():
            for content in OpenAIProvider._read_value(item, "content") or ():
                content_type = OpenAIProvider._read_value(content, "type")
                refusal = OpenAIProvider._read_value(content, "refusal")
                if content_type == "refusal" or refusal:
                    raise ProviderRefusalError("The upstream model refused the request.")

        output_text = OpenAIProvider._read_value(response, "output_text")
        if not isinstance(output_text, str) or not output_text.strip():
            raise ProviderResponseError("The upstream response contained no output.")
        return output_text

    @staticmethod
    def _read_value(value: Any, key: str) -> Any:
        if isinstance(value, Mapping):
            return value.get(key)
        return getattr(value, key, None)

    @staticmethod
    def _require_openai_provider_marker(result: BaseModel) -> None:
        if isinstance(result, (UtteranceSpec, ReflectionSpec)):
            if result.provider != OpenAIProvider.name:
                raise ProviderResponseError(
                    "The upstream response used an invalid provider marker."
                )
