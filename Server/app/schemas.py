from enum import Enum
from typing import Annotated, Literal

from pydantic import (
    BaseModel,
    ConfigDict,
    StrictBool,
    StrictInt,
    StrictStr,
    StringConstraints,
    field_validator,
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


class InterpretCommandRequest(StrictSchema):
    command: CommandText


class GenerateUtteranceRequest(StrictSchema):
    trigger: ExpressionTriggerValue
    context: ContextText = ""


class ReflectRequest(StrictSchema):
    goal: FarmGoalSpec
    outcome: ReflectionOutcomeValue
    event_summary: EventSummaryText


class HealthSpec(StrictSchema):
    status: Literal["ok"]
    provider: ProviderName
    api_key_required: StrictBool
    api_key_configured: StrictBool
