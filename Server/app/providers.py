import json
import logging
import os
import re
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from time import perf_counter
from typing import Any, Protocol, TypeVar
from uuid import uuid4

from openai import OpenAI, OpenAIError
from pydantic import BaseModel, ValidationError

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
    ResidentDecisionRequest,
    ResidentDecisionSpec,
    ResidentReflectionRequest,
    ResidentReflectionSpec,
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

    def interpret_command(
        self,
        request: InterpretCommandRequest,
    ) -> FarmGoalSpec: ...

    def generate_utterance(
        self,
        request: GenerateUtteranceRequest,
    ) -> UtteranceSpec: ...

    def reflect(self, request: ReflectRequest) -> ReflectionSpec: ...

    def decide_resident(
        self,
        request: ResidentDecisionRequest,
    ) -> ResidentDecisionSpec: ...

    def generate_conversation_script(
        self,
        request: ConversationScriptRequest,
    ) -> ConversationScriptSpec: ...

    def reflect_resident(
        self,
        request: ResidentReflectionRequest,
    ) -> ResidentReflectionSpec: ...

    def probe(self) -> "ProviderProbeResult": ...


@dataclass(frozen=True)
class ProviderProbeResult:
    ok: bool
    code: str
    message: str


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

    _propose_town_event_intent = "propose_town_event"
    _harvest_dinner_tags = frozenset({"harvest", "carrot"})

    def probe(self) -> ProviderProbeResult:
        return ProviderProbeResult(
            ok=True,
            code="offline",
            message="离线 Mock 已就绪，不会访问网络。",
        )

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

    def interpret_command(
        self,
        request: InterpretCommandRequest,
    ) -> FarmGoalSpec:
        normalized = self._normalize_command(request.command)
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

    def decide_resident(
        self,
        request: ResidentDecisionRequest,
    ) -> ResidentDecisionSpec:
        can_propose_harvest_dinner = (
            self._propose_town_event_intent in request.allowed_intents
            and self._has_harvest_dinner_memory(request)
        )
        if can_propose_harvest_dinner:
            intent = self._propose_town_event_intent
            reason = (
                "我从对话中得知了胡萝卜收获消息，可以提议全镇参加收获晚餐。"
            )
        else:
            intent = next(
                (
                    candidate
                    for candidate in request.allowed_intents
                    if candidate != self._propose_town_event_intent
                ),
                None,
            )
            if intent is None:
                raise ProviderInputError(
                    "no_safe_intent",
                    "propose_town_event requires a shareable carrot-harvest "
                    "fact learned in conversation.",
                )
            reason = "本地规则从本次请求的允许集合中选择安全的高层意图。"

        target_required = intent in {
            "RequestConversation",
            "ContinueConversation",
            "ShareKnownFact",
        }
        return ResidentDecisionSpec(
            resident_id=request.resident_id,
            intent=intent,
            target_resident_id=(
                request.allowed_target_resident_ids[0]
                if target_required and request.allowed_target_resident_ids
                else None
            ),
            reason=reason,
            provider="mock",
        )

    @classmethod
    def _has_harvest_dinner_memory(
        cls,
        request: ResidentDecisionRequest,
    ) -> bool:
        for memory in request.context.relevant_memories:
            # The cross-gateway provenance contract permits an immediate source only
            # for conversation-derived memory. Pydantic has already checked that the
            # source ID is syntactically valid and differs from this memory's owner.
            if (
                memory.is_shareable
                and memory.immediate_source_resident_id is not None
                and cls._harvest_dinner_tags.issubset(memory.tags)
            ):
                return True
        return False

    def generate_conversation_script(
        self,
        request: ConversationScriptRequest,
    ) -> ConversationScriptSpec:
        contexts = {
            participant.resident_id: participant
            for participant in request.participants
        }
        topic = request.topic or "今天的小镇生活"
        lines: list[ConversationLineSpec] = []
        shared_by_speaker: set[str] = set()
        for index in range(request.max_lines):
            speaker_id = request.participant_ids[index % 2]
            listener_id = request.participant_ids[(index + 1) % 2]
            speaker = contexts[speaker_id]
            listener = contexts[listener_id]
            shareable_memory = next(
                (
                    memory
                    for memory in speaker.relevant_memories
                    if memory.is_shareable
                    and memory.knowledge_id is not None
                    and memory.knowledge_id not in shared_by_speaker
                ),
                None,
            )
            shared_knowledge_id = None
            if shareable_memory is not None:
                shared_knowledge_id = shareable_memory.knowledge_id
                shared_by_speaker.add(shared_knowledge_id)
                bounded_memory = shareable_memory.text[:220]
                text = (
                    f"{listener.persona.display_name}，"
                    f"我想告诉你：{bounded_memory}"
                )
            elif index % 3 == 0:
                text = (
                    f"{listener.persona.display_name}，我是"
                    f"{speaker.persona.display_name}。作为{speaker.persona.role}，"
                    f"想和你聊聊{topic}。"
                )
            elif index % 3 == 1:
                style = speaker.persona.speaking_style or "自然"
                text = (
                    f"{speaker.persona.display_name}，我会用{style}的方式回应；"
                    f"也想听听你作为{listener.persona.role}的看法。"
                )
            else:
                preference = speaker.persona.preference or "小镇近况"
                text = (
                    f"{listener.persona.display_name}，我最近在留意{preference}，"
                    "谢谢你愿意交换消息。"
                )
            moods = (NpcMood.HAPPY, NpcMood.FOCUSED, NpcMood.PROUD)
            emojis = ("💬", "🙂", "🌱")
            lines.append(
                ConversationLineSpec(
                    speaker_id=speaker_id,
                    mood=moods[index % len(moods)],
                    emoji=emojis[index % len(emojis)],
                    text=text,
                    shared_knowledge_id=shared_knowledge_id,
                )
            )

        return ConversationScriptSpec(
            resident_id=request.resident_id,
            lines=lines,
            outcome=ConversationOutcome.NEUTRAL,
            provider="mock",
        )

    def reflect_resident(
        self,
        request: ResidentReflectionRequest,
    ) -> ResidentReflectionSpec:
        outcome = ReflectionOutcome(request.outcome)
        if outcome is ReflectionOutcome.COMPLETED:
            mood = NpcMood.PROUD
            emoji = "★"
            prefix = "已经完成"
        elif outcome is ReflectionOutcome.FAILED:
            mood = NpcMood.WORRIED
            emoji = "⚠"
            prefix = "遇到问题"
        else:
            mood = NpcMood.FOCUSED
            emoji = "🌱"
            prefix = "仍在进行"

        return ResidentReflectionSpec(
            resident_id=request.resident_id,
            outcome=outcome,
            mood=mood,
            emoji=emoji,
            text=(
                f"{request.context.persona.display_name}想到：{prefix}，"
                f"{request.event_summary}"
            ),
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
    _resident_decision_instructions = (
        "Choose exactly one safe high-level resident intent as a ResidentDecisionSpec. "
        "Use only this request's resident context. Preserve resident_id, choose intent "
        "only from allowed_intents, choose target_resident_id only from "
        "allowed_target_resident_ids (or null), and set provider to openai. Never emit "
        "positions, low-level actions, schedules, relationship changes, memory writes, "
        "or world-state mutations."
    )
    _conversation_script_instructions = (
        "Generate a bounded two-resident Chinese ConversationScriptSpec. Use each "
        "participant's persona, relationship snapshot, and only that participant's "
        "current state and owned relevant memories. Preserve resident_id. Every line "
        "must contain speaker_id, mood, emoji, text, and shared_knowledge_id; the last "
        "field must be null or a shareable knowledge_id from that exact speaker's "
        "relevant_memories. Return no more than max_lines and never more than six "
        "lines, and set provider to openai. Never infer, merge, or expose another "
        "resident's private memory."
    )
    _resident_reflection_instructions = (
        "Generate one short Chinese ResidentReflectionSpec for the request owner. Use "
        "only that ResidentContext and bounded event summary. Preserve resident_id and "
        "outcome, set provider to openai, and do not mutate memory or world state."
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

    def interpret_command(
        self,
        request: InterpretCommandRequest,
    ) -> FarmGoalSpec:
        return self._request_structured_output(
            operation="interpret-command",
            resident_id=request.resident_id,
            instructions=self._interpret_instructions,
            snapshot={
                "resident_id": request.resident_id,
                "command": request.command,
            },
            response_model=FarmGoalSpec,
            schema_name="farm_goal_spec",
            fallback_call=lambda: self._fallback.interpret_command(request),
        )

    def generate_utterance(self, request: GenerateUtteranceRequest) -> UtteranceSpec:
        return self._request_structured_output(
            operation="generate-utterance",
            resident_id=request.resident_id,
            instructions=self._utterance_instructions,
            snapshot={
                "resident_id": request.resident_id,
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
            resident_id=request.resident_id,
            instructions=self._reflection_instructions,
            snapshot={
                "resident_id": request.resident_id,
                "goal_id": request.goal.goal_id,
                "outcome": request.outcome,
                "event_summary": request.event_summary,
            },
            response_model=ReflectionSpec,
            schema_name="reflection_spec",
            fallback_call=lambda: self._fallback.reflect(request),
        )

    def decide_resident(
        self,
        request: ResidentDecisionRequest,
    ) -> ResidentDecisionSpec:
        return self._request_structured_output(
            operation="resident-decision",
            resident_id=request.resident_id,
            instructions=self._resident_decision_instructions,
            snapshot=request.model_dump(mode="json"),
            response_model=ResidentDecisionSpec,
            schema_name="resident_decision_spec",
            fallback_call=lambda: self._fallback.decide_resident(request),
            validate_result=lambda result: self._validate_resident_decision(
                request,
                result,
            ),
        )

    def generate_conversation_script(
        self,
        request: ConversationScriptRequest,
    ) -> ConversationScriptSpec:
        return self._request_structured_output(
            operation="conversation-script",
            resident_id=request.resident_id,
            instructions=self._conversation_script_instructions,
            snapshot=request.model_dump(mode="json"),
            response_model=ConversationScriptSpec,
            schema_name="conversation_script_spec",
            fallback_call=lambda: self._fallback.generate_conversation_script(request),
            validate_result=lambda result: self._validate_conversation_script(
                request,
                result,
            ),
            max_output_tokens=1024,
        )

    def reflect_resident(
        self,
        request: ResidentReflectionRequest,
    ) -> ResidentReflectionSpec:
        return self._request_structured_output(
            operation="resident-reflection",
            resident_id=request.resident_id,
            instructions=self._resident_reflection_instructions,
            snapshot=request.model_dump(mode="json"),
            response_model=ResidentReflectionSpec,
            schema_name="resident_reflection_spec",
            fallback_call=lambda: self._fallback.reflect_resident(request),
            validate_result=lambda result: self._validate_resident_reflection(
                request,
                result,
            ),
        )

    def probe(self) -> ProviderProbeResult:
        started_at = self._clock()
        try:
            response = self._client.models.list()
            model_ids = {
                str(self._read_value(item, "id"))
                for item in self._read_value(response, "data") or ()
                if self._read_value(item, "id")
            }
            if self._model not in model_ids:
                result = ProviderProbeResult(
                    ok=False,
                    code="model_not_found",
                    message="连接成功，但当前账号未返回所选模型。",
                )
            else:
                result = ProviderProbeResult(
                    ok=True,
                    code="ok",
                    message="鉴权成功，所选模型可用。",
                )
        except (ConnectionError, OpenAIError, TimeoutError) as error:
            error_name = type(error).__name__
            code_by_error = {
                "AuthenticationError": "authentication_failed",
                "APITimeoutError": "timeout",
                "APIConnectionError": "connection_failed",
                "RateLimitError": "rate_limited",
                "NotFoundError": "model_not_found",
            }
            code = code_by_error.get(error_name, "upstream_error")
            message_by_code = {
                "authentication_failed": "鉴权失败，请检查 API Key。",
                "timeout": "连接测试超时，请稍后重试。",
                "connection_failed": "无法连接到模型服务。",
                "rate_limited": "服务当前限流，请稍后重试。",
                "model_not_found": "所选模型不存在或当前账号无权访问。",
                "upstream_error": "模型服务拒绝了连接测试。",
            }
            result = ProviderProbeResult(
                ok=False,
                code=code,
                message=message_by_code[code],
            )

        elapsed_ms = max(0.0, (self._clock() - started_at) * 1000)
        _logger.info(
            "provider_probe model=%s elapsed_ms=%.2f ok=%s code=%s",
            self._model,
            elapsed_ms,
            result.ok,
            result.code,
        )
        return result

    def _request_structured_output(
        self,
        *,
        operation: str,
        resident_id: str,
        instructions: str,
        snapshot: Mapping[str, object],
        response_model: type[_OutputModel],
        schema_name: str,
        fallback_call: Callable[[], _OutputModel],
        validate_result: Callable[[_OutputModel], None] | None = None,
        max_output_tokens: int = 512,
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
                max_output_tokens=max_output_tokens,
            )
            output_text = self._extract_output_text(response)
            result = response_model.model_validate_json(output_text)
            self._require_openai_provider_marker(result)
            if validate_result is not None:
                validate_result(result)
        except (
            ConnectionError,
            OpenAIError,
            ProviderResponseError,
            TimeoutError,
            ValidationError,
        ) as error:
            return self._use_fallback(
                operation=operation,
                resident_id=resident_id,
                request_id=request_id,
                started_at=started_at,
                reason=type(error).__name__,
                fallback_call=fallback_call,
            )

        self._log_result(
            operation=operation,
            resident_id=resident_id,
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
        resident_id: str,
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
                resident_id=resident_id,
                request_id=request_id,
                started_at=started_at,
                result_type=type(fallback_error).__name__,
                source="error",
                fallback_reason=reason,
            )
            raise

        self._log_result(
            operation=operation,
            resident_id=resident_id,
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
        resident_id: str,
        request_id: str,
        started_at: float,
        result_type: str,
        source: str,
        fallback_reason: str = "-",
        upstream_request_id: str = "-",
    ) -> None:
        elapsed_ms = max(0.0, (self._clock() - started_at) * 1000)
        _logger.info(
            "provider_request request_id=%s resident_id=%s operation=%s elapsed_ms=%.2f "
            "result_type=%s source=%s fallback_reason=%s upstream_request_id=%s",
            request_id,
            resident_id,
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
        if isinstance(
            result,
            (
                UtteranceSpec,
                ReflectionSpec,
                ResidentDecisionSpec,
                ConversationScriptSpec,
                ResidentReflectionSpec,
            ),
        ):
            if result.provider != OpenAIProvider.name:
                raise ProviderResponseError(
                    "The upstream response used an invalid provider marker."
                )

    @staticmethod
    def _validate_resident_decision(
        request: ResidentDecisionRequest,
        result: ResidentDecisionSpec,
    ) -> None:
        if result.resident_id != request.resident_id:
            raise ProviderResponseError(
                "The decision resident_id does not match the request owner."
            )
        if result.intent not in request.allowed_intents:
            raise ProviderResponseError(
                "The decision intent is not in allowed_intents."
            )
        if (
            result.target_resident_id is not None
            and result.target_resident_id
            not in request.allowed_target_resident_ids
        ):
            raise ProviderResponseError(
                "The decision target_resident_id is not in the allowed target set."
            )

    @staticmethod
    def _validate_conversation_script(
        request: ConversationScriptRequest,
        result: ConversationScriptSpec,
    ) -> None:
        if result.resident_id != request.resident_id:
            raise ProviderResponseError(
                "The conversation resident_id does not match the request owner."
            )
        if len(result.lines) > request.max_lines:
            raise ProviderResponseError(
                "The conversation script exceeds the request max_lines."
            )

        participant_ids = set(request.participant_ids)
        allowed_knowledge_ids = {
            participant.resident_id: {
                memory.knowledge_id
                for memory in participant.relevant_memories
                if memory.is_shareable and memory.knowledge_id is not None
            }
            for participant in request.participants
        }
        for line in result.lines:
            if line.speaker_id not in participant_ids:
                raise ProviderResponseError(
                    "Every conversation speaker_id must belong to participant_ids."
                )
            if (
                line.shared_knowledge_id is not None
                and line.shared_knowledge_id
                not in allowed_knowledge_ids[line.speaker_id]
            ):
                raise ProviderResponseError(
                    "A conversation line referenced knowledge that is not in the "
                    "speaker's shareable allowlist."
                )

    @staticmethod
    def _validate_resident_reflection(
        request: ResidentReflectionRequest,
        result: ResidentReflectionSpec,
    ) -> None:
        if result.resident_id != request.resident_id:
            raise ProviderResponseError(
                "The reflection resident_id does not match the request owner."
            )
        if result.outcome.value != request.outcome:
            raise ProviderResponseError(
                "The reflection outcome does not match the request."
            )
