import json
import logging
import os
import re
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from contextvars import ContextVar
from time import perf_counter
from typing import Any, Protocol, TypeVar
from uuid import uuid4

from openai import OpenAI, OpenAIError
from pydantic import BaseModel, ValidationError
from app.protocols import ADAPTERS, UpstreamError, diagnose, parse_json_output, resolve_endpoint

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
    ResidentTaskRequest,
    ResidentTaskSpec,
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

    def interpret_task(self, request: ResidentTaskRequest) -> ResidentTaskSpec:
        ...

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
    checks: tuple[dict, ...] = ()


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
            ok=False,
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

    def interpret_task(self, request: ResidentTaskRequest) -> ResidentTaskSpec:
        # Keep separators: removing the comma in "1、3号地" changes the actual target.
        command = request.command.strip().lower()
        quantity_pattern = r"([-+]?(?:\d+(?:\.\d*)?|\.\d+)|[负零〇一二两三四五六七八九十百点]+)\s*(?:次|轮|遍|条|尾|个|颗|枚|份|times)(?!\s*(?:钓位|果树|地块|农田))"
        action_command = re.sub(quantity_pattern, "", command)
        explicit_points = re.findall(r"(?:fishing|fruit)-\d+", command)
        if any(point not in request.allowed_target_ids for point in explicit_points):
            raise ProviderInputError("target_not_found", "指定的交互目标不存在或当前不可用。")
        explicit_residents = re.findall(r"resident-[a-z0-9]+(?:-[a-z0-9]+)*", command)
        if any(owner not in request.allowed_target_resident_ids and owner != request.resident_id for owner in explicit_residents):
            raise ProviderInputError("target_not_found", "指定的居民不存在或当前不可交流。")
        if any(word in command for word in ("土豆", "小麦", "玉米", "番茄")):
            raise ProviderInputError("unsupported_intent", "当前只支持胡萝卜种植。")
        actions = [
            ("Stop", ("停止", "取消", "自由活动", "恢复自主", "stop")),
            ("TendFarm", ("照料", "照顾", "照看", "循环种", "持续种", "全周期")),
            ("Fish", ("钓鱼", "捕鱼", "fish")),
            ("PickFruit", ("摘果", "采果", "摘苹果", "采摘", "pickfruit")),
            ("Chat", ("聊天", "交谈", "对话", "chat")),
            ("Water", ("浇水", "water")), ("Fertilize", ("施肥", "fertilize")),
            ("Weed", ("除草", "拔草", "weed")), ("Harvest", ("收获", "收割", "harvest")),
            ("Sow", ("播种", "种植", "种满", "种胡萝卜", "种上", "sow")),
            ("Move", ("移动", "前往", "去", "走到", "move")),
        ]
        task_type = next((kind for kind, words in actions if any(word in action_command for word in words)), None)
        legacy_full_goal = self._normalize_command(command) in self._supported_commands
        if legacy_full_goal or ("种" in command and any(word in command for word in ("收掉", "收成", "收获", "收成", "收掉"))):
            task_type = "TendFarm"
        if task_type is None:
            raise ProviderInputError("unsupported_intent", "未识别此任务。支持移动、农活、钓鱼、摘果、聊天和停止。")
        target = next((item for item in request.allowed_target_ids if item.lower() in command), "")
        aliases = {"池塘": "fishing-1", "河岸": "fishing-1", "果园": "fruit-1", "水井": "well", "广场": "town-square", "农田": "farm", "住宅": "home"}
        if not target:
            target = next((value for key, value in aliases.items() if key in command and (not request.allowed_target_ids or value in request.allowed_target_ids)), "")
        if task_type in {"Fish", "PickFruit"}:
            point_number = r"-?\d+|[零〇一二两三四五六七八九十百]+"
            point = re.search(rf"(?:第\s*)?({point_number})\s*[号个棵]?\s*(?:钓位|果树)|(?:钓位|果树)\s*[#第]?\s*({point_number})", command)
            if point:
                try:
                    number = self._parse_quantity_token(next(group for group in point.groups() if group is not None))
                except ProviderInputError:
                    raise ProviderInputError("target_not_found", "指定的钓位或果树不存在。") from None
                target = ("fishing-" if task_type == "Fish" else "fruit-") + str(number)
                if target not in request.allowed_target_ids:
                    raise ProviderInputError("target_not_found", "指定的钓位或果树不存在或当前不可用。")
        resident_target = next((item for item in request.allowed_target_resident_ids if item in command), None)
        names = {"芽芽": "resident-001", "阿木": "resident-002", "小穗": "resident-003", "墨墨": "resident-004"}
        if not resident_target:
            resident_target = next((value for key, value in names.items() if key in command and value in request.allowed_target_resident_ids), None)
        if task_type == "Chat" and resident_target is None:
            raise ProviderInputError("target_not_found", "请指定存在的聊天居民。")
        if task_type == "Chat" and resident_target == request.resident_id:
            raise ProviderInputError("target_not_found", "不能与自己开启居民会话。")
        if task_type == "Move" and not target:
            raise ProviderInputError("target_not_found", "未找到可达地点，请指定列表中的地点。")
        plots = (list(range(1, 10)) if legacy_full_goal else self._parse_task_plots(command)) if task_type in {"Sow", "Water", "Fertilize", "Weed", "Harvest", "TendFarm"} else []
        explicit_quantity = re.search(quantity_pattern, command) is not None
        quantity = self._parse_activity_quantity(command, quantity_pattern)
        if task_type not in {"Fish", "PickFruit"} and explicit_quantity and quantity > 1:
            raise ProviderInputError("unsupported_quantity", "农事任务目前支持单次操作或持续照料；请勿指定多次操作数量。")
        repeat = any(word in command for word in ("持续", "循环", "一直", "反复"))
        if task_type in {"Fish", "PickFruit"} and explicit_quantity:
            repeat = False
        if task_type == "Chat" and repeat:
            raise ProviderInputError("unsupported_task_mode", "聊天任务目前支持一次有轮次上限的会话，不支持循环聊天。")
        return ResidentTaskSpec(resident_id=request.resident_id, task_id=uuid4().hex, task_type=task_type, target_id=target, target_resident_id=resident_target,
            target_plot_numbers=plots, repeat=repeat, quantity=quantity, summary=f"{request.command[:200]}（本地解析）", provider="mock")

    @staticmethod
    def _parse_task_plots(command: str) -> list[int]:
        plots = []
        covered = []
        for match in re.finditer(r"(-?\d+)\s*(?:到|至|[-~～])\s*(-?\d+)\s*(?:号|块)?(?:地|田|农田)", command):
            first, last = (int(value) for value in match.groups())
            if not 1 <= first <= last <= 9:
                raise ProviderInputError("target_not_found", "地块范围必须在 1 到 9 之间且从小到大。")
            plots.extend(range(first, last + 1))
            covered.append(match.span())
        for match in re.finditer(r"(-?\d+(?:\s*[、，,和及]\s*-?\d+)+)\s*(?:号|块)(?:地|田|农田)?", command):
            plots.extend(int(value) for value in re.findall(r"-?\d+", match.group(1)))
            covered.append(match.span())
        # Negative lookahead keeps activity counts (浇水3次) separate from plot IDs.
        pattern = r"(?:第|地块|田块|plot|播种|浇水|施肥|除草|收获)[ #第]*(-?\d+)(?![\d.])(?!\s*(?:次|轮|遍|条|尾|个|颗|枚|份|times))|(?<![\d.])(-?\d+)\s*(?:号|块)(?:地|田|农田)?"
        for match in re.finditer(pattern, command):
            if not any(first <= match.start() < last for first, last in covered):
                plots.append(int(next(value for value in match.groups() if value is not None)))
        if any(number < 1 or number > 9 for number in plots):
            raise ProviderInputError("target_not_found", "地块编号必须在 1 到 9 之间。")
        if re.search(r"(?:第|地块|田块|plot|播种|浇水|施肥|除草|收获)\s*-?\d+\.\d+|-?\d+\.\d+\s*(?:号|块)(?:地|田)", command):
            raise ProviderInputError("target_not_found", "地块编号必须是 1 到 9 的整数。")
        if re.search(r"第?[零〇一二两三四五六七八九十百]+(?:号|块)(?:地|田)", command):
            raise ProviderInputError("target_not_found", "请用 1 到 9 的数字指定地块编号。")
        return list(dict.fromkeys(plots)) if plots else list(range(1, 10))

    @staticmethod
    def _parse_activity_quantity(command: str, pattern: str) -> int:
        quantities = [MockProvider._parse_quantity_token(match.group(1)) for match in re.finditer(pattern, command)]
        if len(set(quantities)) > 1:
            raise ProviderInputError("invalid_quantity", "一条活动指令请指定一个明确的数量。")
        return quantities[0] if quantities else 1

    @staticmethod
    def _parse_quantity_token(token: str) -> int:
        digits = {"零": 0, "〇": 0, "一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5, "六": 6, "七": 7, "八": 8, "九": 9}
        if re.fullmatch(r"[+]?\d+", token):
            number = int(token)
        elif token in digits:
            number = digits[token]
        elif re.fullmatch(r"[一二两三四五六七八九]?十[一二两三四五六七八九]?", token):
            tens, ones = token.split("十")
            number = digits.get(tens, 1) * 10 + digits.get(ones, 0)
        else:
            raise ProviderInputError("invalid_quantity", "活动数量必须是 1 到 99 的整数。")
        if not 1 <= number <= 99:
            raise ProviderInputError("invalid_quantity", "活动数量必须是 1 到 99 的整数。")
        return number

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
    """Shared high-level AI service backed by one explicitly selected protocol."""

    name = "openai"
    requires_api_key = False

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
        base_url: str = DEEPSEEK_BASE_URL,
        protocol: str | None = None,
        output_mode: str = "text",
        http_client: Any | None = None,
    ) -> None:
        normalized_api_key = api_key.strip()
        normalized_model = model.strip()
        if not normalized_model:
            raise RuntimeError("OPENAI_MODEL is required for AIFARM_PROVIDER=openai.")

        self._model = normalized_model
        self.api_key_configured = bool(normalized_api_key)
        self._api_key = normalized_api_key
        self.base_url = base_url
        self.protocol = protocol or ("responses" if client is not None else "chat_completions")
        self.output_mode = output_mode
        self.endpoint = resolve_endpoint(base_url, self.protocol, self._model)
        self.last_error: ContextVar[UpstreamError | None] = ContextVar("provider_error", default=None)
        self.last_request_id: ContextVar[str] = ContextVar("provider_request_id", default="")
        self._fallback = fallback or MockProvider()
        self._clock = clock
        self._request_id_factory = request_id_factory or (lambda: uuid4().hex)
        self._client = client
        self._adapter = ADAPTERS[self.protocol](endpoint=self.endpoint, model=self._model, api_key=normalized_api_key,
            timeout=timeout_seconds, output_mode=output_mode, client=http_client) if client is None else None

    @property
    def model(self) -> str:
        return self._model

    @classmethod
    def from_environment(cls) -> "OpenAIProvider":
        return cls(
            api_key=os.getenv("OPENAI_API_KEY", ""),
            model=os.getenv("OPENAI_MODEL", ""),
            base_url=os.getenv("OPENAI_BASE_URL", DEEPSEEK_BASE_URL),
            protocol=os.getenv("AIFARM_PROTOCOL", "chat_completions"),
        )

    def interpret_task(self, request: ResidentTaskRequest) -> ResidentTaskSpec:
        return self._request_structured_output(
            operation="resident-task", resident_id=request.resident_id,
            instructions="Interpret the player's command as ResidentTaskSpec. Preserve resident_id. Choose a task_type from the schema, target_id only from allowed_target_ids (or empty for automatic selection), and target_resident_id only from allowed_target_resident_ids. Only carrot farming exists. TendFarm means care from sowing through harvest. repeat is true only for explicitly ongoing/cyclic care. Never turn unrelated commands into farming. Invalid or unknown targets must be refused. Produce a short Chinese summary.",
            snapshot=request.model_dump(mode="json"), response_model=ResidentTaskSpec, schema_name="resident_task_spec",
            fallback_call=lambda: self._fallback.interpret_task(request),
            validate_result=lambda result: self._validate_task(request, result))

    @staticmethod
    def _validate_task(request: ResidentTaskRequest, result: ResidentTaskSpec) -> None:
        if result.resident_id != request.resident_id:
            raise ProviderResponseError("Task resident_id does not match request owner.")
        if result.target_id and result.target_id not in request.allowed_target_ids:
            raise ProviderResponseError("Task target_id is not in allowed_target_ids.")
        if result.target_resident_id and result.target_resident_id not in request.allowed_target_resident_ids:
            raise ProviderResponseError("Task target resident is not allowed.")
        if result.task_type == "Move" and not result.target_id:
            raise ProviderResponseError("Move requires a valid target.")
        if result.task_type == "Chat" and not result.target_resident_id:
            raise ProviderResponseError("Chat requires a valid resident.")

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
        from app.schemas import ResidentContext, ResidentPersonaSnapshot, RelationshipSnapshot

        def context(owner: str, other: str, name: str) -> ResidentContext:
            return ResidentContext(resident_id=owner,
                persona=ResidentPersonaSnapshot(display_name=name, role="居民", personality_traits=["可靠"], speaking_style="自然简短"),
                current_state="白天；刚给农田浇水，正在广场休息。",
                relationship_snapshots=[RelationshipSnapshot(owner_resident_id=owner, target_resident_id=other, familiarity=10, trust=10)], relevant_memories=[])

        first, second = context("probe-001", "probe-002", "测试居民一"), context("probe-002", "probe-001", "测试居民二")
        decision = ResidentDecisionRequest(resident_id=first.resident_id, context=first,
            situation="请选择一项可用活动。", allowed_intents=["Walk", "Rest"], allowed_target_resident_ids=[])
        conversation = ConversationScriptRequest(resident_id=first.resident_id, participant_ids=[first.resident_id, second.resident_id],
            participants=[first, second], topic="刚刚完成浇水，接下来去果园看看。", max_lines=2)
        checks = []
        for name, operation in (("text", self._probe_text), ("resident_decision", lambda: self.decide_resident(decision)),
                ("conversation", lambda: self.generate_conversation_script(conversation))):
            try:
                self.last_error.set(None)
                result = operation()
                failure = self.last_error.get()
                if failure is not None:
                    raise failure
                if getattr(result, "provider", "openai") != "openai":
                    raise UpstreamError("output_parse_error", "测试请求发生本地回退，不能判定为真实模型在线。")
                checks.append({"name": name, "ok": True, "code": "ok", "message": "上游实际推理并通过本地校验。"})
            except (UpstreamError, OpenAIError, ConnectionError, TimeoutError, ValueError, ProviderResponseError) as error:
                failure = diagnose(error, self._api_key)
                checks.append({"name": name, "ok": False, "code": failure.code, "message": failure.message})
        failure = next((check for check in checks if not check["ok"]), None)
        return ProviderProbeResult(ok=failure is None, code=failure["code"] if failure else "ok",
            message=failure["message"] if failure else "文本推理、居民决策和居民对话均已通过上游实际推理。", checks=tuple(checks))

    def _probe_text(self) -> str:
        if self._client is not None:
            response = self._client.responses.create(model=self._model, instructions="Reply with a short greeting.", input="你好", max_output_tokens=64)
            return self._extract_output_text(response)
        return self._adapter.complete("Reply with a short greeting.", "你好", max_tokens=64).text

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
        self.last_request_id.set(request_id)
        started_at = self._clock()
        self.last_error.set(None)

        try:
            if self._client is None:
                result = self._request_adapter(instructions, snapshot, response_model, schema_name, validate_result, max_output_tokens)
                self._log_result(operation=operation, resident_id=resident_id, request_id=request_id, started_at=started_at,
                    result_type=type(result).__name__, source="openai")
                return result
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
            if "provider" in response_model.model_fields:
                result = result.model_copy(update={"provider": "openai"})
            if validate_result is not None:
                validate_result(result)
        except (
            ConnectionError,
            OpenAIError,
            ProviderResponseError,
            TimeoutError,
            ValidationError,
            UpstreamError,
            ValueError,
        ) as error:
            self.last_error.set(diagnose(error, self._api_key))
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

    def _request_adapter(self, instructions, snapshot, response_model, schema_name, validate_result, max_output_tokens):
        from app.schemas import ExecutionSpec

        metadata_fields = set(ExecutionSpec.model_fields)
        schema = response_model.model_json_schema()
        for name in metadata_fields | {"provider", "task_id"}:
            schema["properties"].pop(name, None)
            if name in schema.get("required", []):
                schema["required"].remove(name)
        system = instructions.replace("set provider to openai", "omit provider metadata")
        system += "\nReturn a single JSON object matching this schema. No explanations or world mutations. Schema:\n" + json.dumps(schema, ensure_ascii=False)
        user = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
        for attempt in range(2):
            reply = self._adapter.complete(system, user, schema, schema_name, max_tokens=max(2048, max_output_tokens))
            try:
                payload = parse_json_output(reply.text)
                for field in metadata_fields:
                    payload.pop(field, None)
                if "provider" in response_model.model_fields:
                    payload["provider"] = "openai"
                if "task_id" in response_model.model_fields:
                    payload["task_id"] = uuid4().hex
                result = response_model.model_validate_json(json.dumps(payload, ensure_ascii=False))
                if validate_result is not None:
                    validate_result(result)
                return result
            except (ValueError, ValidationError, ProviderResponseError) as error:
                if attempt:
                    raise ProviderResponseError("输出在一次结构修复后仍未通过本地校验。") from error
                # Send only the original owned snapshot and an error category, not arbitrary
                # malformed model text that could introduce a second set of instructions.
                user += "\nPrevious output failed " + type(error).__name__ + ". Retry once with exactly the required JSON fields and allowed values."

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
