from enum import Enum
from typing import Annotated, Literal, Self

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    SecretStr,
    StrictBool,
    StrictInt,
    StrictStr,
    StringConstraints,
    field_validator,
    model_validator,
)


CommandText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=500),
]
ContextText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, max_length=200),
]
ShortText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=300),
]
EventSummaryText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=240),
]
EmojiText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=8),
]
ResidentIdText = Annotated[
    StrictStr,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=64,
        pattern=r"^[a-z0-9]+(?:-[a-z0-9]+)*$",
    ),
]
LabelText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=80),
]
PersonaDetailText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, max_length=200),
]
MemoryText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=500),
]
KnowledgeIdText = Annotated[
    StrictStr,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=160,
        pattern=r"^[A-Za-z0-9][A-Za-z0-9._:/-]*$",
    ),
]
MemoryTagText = Annotated[
    StrictStr,
    StringConstraints(strip_whitespace=True, min_length=1, max_length=48),
]
IntentText = Annotated[
    StrictStr,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=64,
        pattern=r"^(?:[A-Z][A-Za-z0-9]*|propose_town_event)$",
    ),
]
BoundedRelationshipValue = Annotated[StrictInt, Field(ge=-100, le=100)]
MemoryImportance = Annotated[StrictInt, Field(ge=1, le=10)]
ConversationLineLimit = Annotated[StrictInt, Field(ge=2, le=6)]
ModelIdText = Annotated[
    StrictStr,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=128,
        pattern=r"^[A-Za-z0-9][A-Za-z0-9._:/-]*$",
    ),
]
GatewayInstanceIdText = Annotated[
    StrictStr,
    StringConstraints(
        min_length=32,
        max_length=32,
        pattern=r"^[0-9a-f]{32}$",
    ),
]
ApiKeySecret = Annotated[SecretStr, Field(max_length=512)]


class StrictSchema(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        frozen=True,
        strict=True,
        str_strip_whitespace=True,
    )


class NpcMood(str, Enum):
    HAPPY = "Happy"
    FOCUSED = "Focused"
    WORRIED = "Worried"
    TIRED = "Tired"
    PROUD = "Proud"


class NpcExpressionTrigger(str, Enum):
    COMMAND_ACCEPTED = "CommandAccepted"
    SOWING_STARTED = "SowingStarted"
    WATER_NEEDED = "WaterNeeded"
    WEEDS_FOUND = "WeedsFound"
    ACTION_FAILED = "ActionFailed"
    WAITING_FOR_GROWTH = "WaitingForGrowth"
    HARVEST_STARTED = "HarvestStarted"
    GOAL_COMPLETED = "GoalCompleted"


class ReflectionOutcome(str, Enum):
    IN_PROGRESS = "InProgress"
    COMPLETED = "Completed"
    FAILED = "Failed"


class ConversationOutcome(str, Enum):
    POSITIVE = "Positive"
    NEUTRAL = "Neutral"
    HELPFUL = "Helpful"
    AWKWARD = "Awkward"
    CONFLICT = "Conflict"


ExpressionTriggerValue = Literal[
    "CommandAccepted",
    "SowingStarted",
    "WaterNeeded",
    "WeedsFound",
    "ActionFailed",
    "WaitingForGrowth",
    "HarvestStarted",
    "GoalCompleted",
]
ReflectionOutcomeValue = Literal["InProgress", "Completed", "Failed"]
ProviderName = Literal["mock", "openai"]


class FarmGoalSpec(StrictSchema):
    goal_id: Literal["full_field_carrot_lifecycle"]
    crop: Literal["carrot"]
    target_plot_numbers: list[StrictInt]
    requires_sowing: Literal[True]
    requires_watering: Literal[True]
    requires_fertilizing: Literal[True]
    requires_weeding: Literal[True]
    requires_harvesting: Literal[True]
    summary: Literal["完成 3×3 农田的胡萝卜全周期"]

    @field_validator("target_plot_numbers")
    @classmethod
    def require_full_field(cls, value: list[int]) -> list[int]:
        expected = list(range(1, 10))
        if value != expected:
            raise ValueError("target_plot_numbers must contain the ordered plots 1 through 9")
        return value


class UtteranceSpec(StrictSchema):
    trigger: NpcExpressionTrigger
    mood: NpcMood
    emoji: EmojiText
    text: ShortText
    provider: ProviderName


class ReflectionSpec(StrictSchema):
    goal_id: Literal["full_field_carrot_lifecycle"]
    outcome: ReflectionOutcome
    mood: NpcMood
    emoji: EmojiText
    text: ShortText
    provider: ProviderName


class ResidentPersonaSnapshot(StrictSchema):
    display_name: LabelText
    role: LabelText
    personality_traits: Annotated[list[LabelText], Field(min_length=1, max_length=8)]
    speaking_style: PersonaDetailText
    work_habit: PersonaDetailText = ""
    preference: PersonaDetailText = ""
    dislike: PersonaDetailText = ""


class RelationshipSnapshot(StrictSchema):
    owner_resident_id: ResidentIdText
    target_resident_id: ResidentIdText
    familiarity: BoundedRelationshipValue
    trust: BoundedRelationshipValue

    @model_validator(mode="after")
    def require_distinct_residents(self) -> Self:
        if self.owner_resident_id == self.target_resident_id:
            raise ValueError("a relationship target must differ from its owner")
        return self


class ResidentMemorySnapshot(StrictSchema):
    owner_resident_id: ResidentIdText
    text: MemoryText
    importance: MemoryImportance
    knowledge_id: KnowledgeIdText | None = None
    root_fact_id: KnowledgeIdText | None = None
    tags: Annotated[list[MemoryTagText], Field(max_length=16)] = Field(
        default_factory=list
    )
    is_shareable: StrictBool = False
    immediate_source_resident_id: ResidentIdText | None = None

    @model_validator(mode="after")
    def require_valid_provenance(self) -> Self:
        if len(set(self.tags)) != len(self.tags):
            raise ValueError("memory tags must not contain duplicates")
        if self.immediate_source_resident_id == self.owner_resident_id:
            raise ValueError("memory source resident must differ from its owner")
        if self.is_shareable and (
            self.knowledge_id is None or self.root_fact_id is None
        ):
            raise ValueError(
                "a shareable memory requires knowledge_id and root_fact_id"
            )
        return self


class ResidentContext(StrictSchema):
    resident_id: ResidentIdText
    persona: ResidentPersonaSnapshot
    current_state: ContextText
    relationship_snapshots: Annotated[
        list[RelationshipSnapshot],
        Field(max_length=16),
    ]
    relevant_memories: Annotated[
        list[ResidentMemorySnapshot],
        Field(max_length=24),
    ]

    @model_validator(mode="after")
    def require_owned_private_context(self) -> Self:
        relationship_targets: set[str] = set()
        for relationship in self.relationship_snapshots:
            if relationship.owner_resident_id != self.resident_id:
                raise ValueError(
                    "relationship snapshot owner must match context resident_id"
                )
            if relationship.target_resident_id in relationship_targets:
                raise ValueError(
                    "relationship snapshots must have unique target_resident_id values"
                )
            relationship_targets.add(relationship.target_resident_id)

        for memory in self.relevant_memories:
            if memory.owner_resident_id != self.resident_id:
                raise ValueError(
                    "private memory owner must match context resident_id"
                )
        return self


class ResidentDecisionRequest(StrictSchema):
    resident_id: ResidentIdText
    context: ResidentContext
    situation: ContextText = ""
    allowed_intents: Annotated[list[IntentText], Field(min_length=1, max_length=16)]
    allowed_target_resident_ids: Annotated[
        list[ResidentIdText],
        Field(max_length=16),
    ]

    @model_validator(mode="after")
    def require_matching_owner_and_unique_allowlists(self) -> Self:
        if self.context.resident_id != self.resident_id:
            raise ValueError("context resident_id must match request resident_id")
        if len(set(self.allowed_intents)) != len(self.allowed_intents):
            raise ValueError("allowed_intents must not contain duplicates")
        if len(set(self.allowed_target_resident_ids)) != len(
            self.allowed_target_resident_ids
        ):
            raise ValueError(
                "allowed_target_resident_ids must not contain duplicates"
            )
        return self


class ResidentDecisionSpec(StrictSchema):
    resident_id: ResidentIdText
    intent: IntentText
    target_resident_id: ResidentIdText | None
    reason: ShortText
    provider: ProviderName


class ConversationLineSpec(StrictSchema):
    speaker_id: ResidentIdText
    mood: NpcMood
    emoji: EmojiText
    text: ShortText
    shared_knowledge_id: KnowledgeIdText | None


class ConversationScriptRequest(StrictSchema):
    resident_id: ResidentIdText
    participant_ids: Annotated[
        list[ResidentIdText],
        Field(min_length=2, max_length=2),
    ]
    participants: Annotated[
        list[ResidentContext],
        Field(min_length=2, max_length=2),
    ]
    topic: ContextText = ""
    max_lines: ConversationLineLimit = 6

    @model_validator(mode="after")
    def require_two_isolated_participant_contexts(self) -> Self:
        participant_ids = set(self.participant_ids)
        if len(participant_ids) != 2:
            raise ValueError("participant_ids must contain two different residents")
        if self.resident_id not in participant_ids:
            raise ValueError("request resident_id must be a conversation participant")

        context_ids = [participant.resident_id for participant in self.participants]
        if len(set(context_ids)) != len(context_ids):
            raise ValueError("participant contexts must have unique resident_id values")
        if set(context_ids) != participant_ids:
            raise ValueError(
                "participant contexts must exactly match participant_ids"
            )

        for participant in self.participants:
            other_ids = participant_ids - {participant.resident_id}
            relationship_targets = {
                relationship.target_resident_id
                for relationship in participant.relationship_snapshots
            }
            if relationship_targets != other_ids:
                raise ValueError(
                    "each participant must carry only its relationship snapshot "
                    "for the other participant"
                )
        return self


class ConversationScriptSpec(StrictSchema):
    resident_id: ResidentIdText
    lines: Annotated[
        list[ConversationLineSpec],
        Field(min_length=2, max_length=6),
    ]
    outcome: ConversationOutcome
    provider: ProviderName


class ResidentReflectionRequest(StrictSchema):
    resident_id: ResidentIdText
    context: ResidentContext
    outcome: ReflectionOutcomeValue
    event_summary: EventSummaryText

    @model_validator(mode="after")
    def require_matching_owner(self) -> Self:
        if self.context.resident_id != self.resident_id:
            raise ValueError("context resident_id must match request resident_id")
        return self


class ResidentReflectionSpec(StrictSchema):
    resident_id: ResidentIdText
    outcome: ReflectionOutcome
    mood: NpcMood
    emoji: EmojiText
    text: ShortText
    provider: ProviderName


class InterpretCommandRequest(StrictSchema):
    resident_id: ResidentIdText
    command: CommandText


class GenerateUtteranceRequest(StrictSchema):
    resident_id: ResidentIdText
    trigger: ExpressionTriggerValue
    context: ContextText = ""


class ReflectRequest(StrictSchema):
    resident_id: ResidentIdText
    goal: FarmGoalSpec
    outcome: ReflectionOutcomeValue
    event_summary: EventSummaryText


class HealthSpec(StrictSchema):
    status: Literal["ok"]
    provider: ProviderName
    api_key_required: StrictBool
    api_key_configured: StrictBool


class GatewayConfigureRequest(StrictSchema):
    provider: ProviderName
    model: ModelIdText = "deepseek-v4-flash"
    api_key: ApiKeySecret | None = None
    persist: StrictBool = True


class GatewayConfigSpec(StrictSchema):
    provider: ProviderName
    model: ModelIdText | None
    api_key_required: StrictBool
    api_key_configured: StrictBool
    persisted: StrictBool
    source: Literal["environment", "local_config", "runtime", "injected"]
    instance_id: GatewayInstanceIdText | None = None


class GatewayProbeSpec(StrictSchema):
    ok: StrictBool
    provider: ProviderName
    model: ModelIdText | None
    code: Literal[
        "ok",
        "offline",
        "authentication_failed",
        "timeout",
        "connection_failed",
        "rate_limited",
        "model_not_found",
        "upstream_error",
    ]
    message: ShortText
