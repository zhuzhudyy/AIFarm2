using System;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
using UnityEngine;

namespace AIFarm.Presentation
{
    public static class AiGatewayJsonCodec
    {
        public const int MaximumCommandLength = 500;
        public const int MaximumContextLength = 200;
        public const int MaximumEventSummaryLength = 240;
        public const int MaximumResponseLength = 16 * 1024;

        public static string SerializeInterpretCommandRequest(string command)
        {
            return SerializeInterpretCommandRequest(ResidentIds.Yaya, command);
        }

        public static string SerializeInterpretCommandRequest(
            ResidentId residentId,
            string command)
        {
            EnsureResidentId(residentId);
            return JsonUtility.ToJson(new InterpretCommandRequestDto(
                residentId.Value,
                command));
        }

        public static string SerializeGenerateUtteranceRequest(
            NpcExpressionTrigger trigger,
            string context)
        {
            return SerializeGenerateUtteranceRequest(
                ResidentIds.Yaya,
                trigger,
                context);
        }

        public static string SerializeGenerateUtteranceRequest(
            ResidentId residentId,
            NpcExpressionTrigger trigger,
            string context)
        {
            EnsureResidentId(residentId);
            return JsonUtility.ToJson(new GenerateUtteranceRequestDto(
                residentId.Value,
                trigger.ToString(),
                context));
        }

        public static string SerializeReflectRequest(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary)
        {
            return SerializeReflectRequest(
                ResidentIds.Yaya,
                goal,
                outcome,
                eventSummary);
        }

        public static string SerializeReflectRequest(
            ResidentId residentId,
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary)
        {
            EnsureResidentId(residentId);
            if (goal == null)
            {
                throw new ArgumentNullException(nameof(goal));
            }

            return JsonUtility.ToJson(new ReflectRequestDto(
                residentId.Value,
                FarmGoalDto.FromGoal(goal),
                outcome.ToString(),
                eventSummary));
        }

        public static string SerializeConversationScriptRequest(
            ConversationScriptRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return JsonUtility.ToJson(ConversationScriptRequestDto.FromRequest(request));
        }

        public static ActionResult TryParseFarmGoal(
            string json,
            out FarmGoalSpec goal)
        {
            goal = null;
            if (!HasExactTopLevelProperties(
                    json,
                    "goal_id",
                    "crop",
                    "target_plot_numbers",
                    "requires_sowing",
                    "requires_watering",
                    "requires_fertilizing",
                    "requires_weeding",
                    "requires_harvesting",
                    "summary") ||
                !TryDeserialize(json, out FarmGoalDto dto))
            {
                return InvalidResponse("Farm goal response is not a valid JSON object.");
            }

            if (dto.GoalId != FarmGoalSpec.FullFieldCarrotLifecycleId ||
                dto.Crop != "carrot" ||
                dto.Summary != "完成 3×3 农田的胡萝卜全周期" ||
                !dto.RequiresSowing ||
                !dto.RequiresWatering ||
                !dto.RequiresFertilizing ||
                !dto.RequiresWeeding ||
                !dto.RequiresHarvesting ||
                !ContainsFullField(dto.TargetPlotNumbers))
            {
                return InvalidResponse("Farm goal response failed semantic validation.");
            }

            goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            return ActionResult.Success("Remote FarmGoalSpec validated.");
        }

        public static ActionResult TryParseUtterance(
            string json,
            NpcExpressionTrigger expectedTrigger,
            out NpcExpression expression)
        {
            expression = null;
            if (!HasExactTopLevelProperties(
                    json,
                    "trigger",
                    "mood",
                    "emoji",
                    "text",
                    "provider") ||
                !TryDeserialize(json, out UtteranceDto dto))
            {
                return InvalidResponse("Utterance response is not a valid JSON object.");
            }

            if (!TryParseEnum(dto.Trigger, out NpcExpressionTrigger trigger) ||
                trigger != expectedTrigger ||
                !TryParseEnum(dto.Mood, out NpcMood mood) ||
                !IsProvider(dto.Provider) ||
                !IsBoundedText(dto.Emoji, 1, 8) ||
                !IsBoundedText(dto.Text, 1, 300))
            {
                return InvalidResponse("Utterance response failed semantic validation.");
            }

            expression = new NpcExpression(trigger, mood, dto.Emoji.Trim(), dto.Text.Trim());
            return ActionResult.Success("Remote UtteranceSpec validated.");
        }

        public static ActionResult TryParseReflection(
            string json,
            FarmGoalSpec expectedGoal,
            NpcReflectionOutcome expectedOutcome,
            out NpcReflection reflection)
        {
            reflection = null;
            if (expectedGoal == null ||
                !HasExactTopLevelProperties(
                    json,
                    "goal_id",
                    "outcome",
                    "mood",
                    "emoji",
                    "text",
                    "provider") ||
                !TryDeserialize(json, out ReflectionDto dto))
            {
                return InvalidResponse("Reflection response is not a valid JSON object.");
            }

            if (dto.GoalId != expectedGoal.GoalId ||
                !TryParseEnum(dto.Outcome, out NpcReflectionOutcome outcome) ||
                outcome != expectedOutcome ||
                !TryParseEnum(dto.Mood, out NpcMood mood) ||
                !IsProvider(dto.Provider) ||
                !IsBoundedText(dto.Emoji, 1, 8) ||
                !IsBoundedText(dto.Text, 1, 300))
            {
                return InvalidResponse("Reflection response failed semantic validation.");
            }

            reflection = new NpcReflection(
                dto.GoalId,
                outcome,
                mood,
                dto.Emoji.Trim(),
                dto.Text.Trim());
            return ActionResult.Success("Remote ReflectionSpec validated.");
        }

        public static ActionResult TryParseConversationScript(
            string json,
            ConversationScriptRequest request,
            out ConversationScriptSpec script)
        {
            script = null;
            if (request == null ||
                !HasExactTopLevelProperties(
                    json,
                    "resident_id",
                    "lines",
                    "outcome",
                    "provider") ||
                !TryDeserialize(json, out ConversationScriptResponseDto dto) ||
                dto.ResidentId != request.ResidentId.Value ||
                !IsProvider(dto.Provider) ||
                !TryParseEnum(dto.Outcome, out ConversationOutcome outcome) ||
                dto.Lines == null ||
                dto.Lines.Length < ConversationSession.MinimumSentenceCount ||
                dto.Lines.Length > request.MaxLines ||
                dto.Lines.Length > ConversationSession.MaximumSentenceCount)
            {
                return InvalidResponse(
                    "Conversation script response failed its owner, shape, or line-limit validation.");
            }

            var lines = new List<ConversationLineSpec>(dto.Lines.Length);
            try
            {
                foreach (ConversationLineDto line in dto.Lines)
                {
                    if (line == null ||
                        !ResidentId.TryCreate(line.SpeakerId, out ResidentId speakerId) ||
                        !request.IsParticipant(speakerId) ||
                        !TryParseEnum(line.Mood, out NpcMood mood) ||
                        !IsBoundedText(line.Emoji, 1, 8) ||
                        !IsBoundedText(line.Text, 1, 300) ||
                        !IsAllowedSharedKnowledge(
                            request,
                            speakerId,
                            line.SharedKnowledgeId))
                    {
                        return InvalidResponse(
                            "Every conversation line must belong to a participant and include valid presentation data.");
                    }

                    lines.Add(new ConversationLineSpec(
                        speakerId,
                        mood,
                        line.Emoji.Trim(),
                        line.Text.Trim(),
                        line.SharedKnowledgeId));
                }

                script = new ConversationScriptSpec(
                    request.ResidentId,
                    lines,
                    outcome,
                    dto.Provider);
            }
            catch (ArgumentException)
            {
                script = null;
                return InvalidResponse("Conversation script failed semantic validation.");
            }

            return ActionResult.Success("Remote ConversationScriptSpec validated.");
        }

        private static bool IsAllowedSharedKnowledge(
            ConversationScriptRequest request,
            ResidentId speakerId,
            string sharedKnowledgeId)
        {
            string normalized = (sharedKnowledgeId ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return true;
            }

            foreach (ResidentContext context in request.Participants)
            {
                if (context.ResidentId != speakerId)
                {
                    continue;
                }

                foreach (ResidentMemorySnapshot memory in context.RelevantMemories)
                {
                    if (memory.IsShareable && memory.KnowledgeId == normalized)
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        public static string BoundText(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }

        private static string[] CopyStrings(IReadOnlyList<string> values)
        {
            var result = new string[values?.Count ?? 0];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = values[index];
            }

            return result;
        }

        private static bool TryDeserialize<T>(string json, out T value)
            where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumResponseLength)
            {
                return false;
            }

            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
                !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                value = JsonUtility.FromJson<T>(trimmed);
                return value != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool HasExactTopLevelProperties(
            string json,
            params string[] expectedProperties)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumResponseLength)
            {
                return false;
            }

            var remaining = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
            int index = 0;
            SkipWhitespace(json, ref index);
            if (!Consume(json, ref index, '{'))
            {
                return false;
            }

            SkipWhitespace(json, ref index);
            if (Consume(json, ref index, '}'))
            {
                SkipWhitespace(json, ref index);
                return index == json.Length && remaining.Count == 0;
            }

            while (index < json.Length)
            {
                if (!TryReadPropertyName(json, ref index, out string propertyName) ||
                    !remaining.Remove(propertyName))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (!Consume(json, ref index, ':'))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (!SkipJsonValue(json, ref index))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, '}'))
                {
                    SkipWhitespace(json, ref index);
                    return index == json.Length && remaining.Count == 0;
                }

                if (!Consume(json, ref index, ','))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
            }

            return false;
        }

        private static bool TryReadPropertyName(
            string json,
            ref int index,
            out string propertyName)
        {
            propertyName = null;
            SkipWhitespace(json, ref index);
            if (!Consume(json, ref index, '"'))
            {
                return false;
            }

            int start = index;
            while (index < json.Length)
            {
                char current = json[index++];
                if (current == '\\')
                {
                    return false;
                }

                if (current == '"')
                {
                    propertyName = json.Substring(start, index - start - 1);
                    return propertyName.Length > 0;
                }

                if (current < ' ')
                {
                    return false;
                }
            }

            return false;
        }

        private static bool SkipJsonValue(string json, ref int index)
        {
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == '"')
            {
                return SkipJsonString(json, ref index);
            }

            if (json[index] == '{' || json[index] == '[')
            {
                return SkipJsonContainer(json, ref index);
            }

            int start = index;
            while (index < json.Length && json[index] != ',' && json[index] != '}')
            {
                index++;
            }

            int end = index;
            while (end > start && char.IsWhiteSpace(json[end - 1]))
            {
                end--;
            }

            return end > start;
        }

        private static bool SkipJsonContainer(string json, ref int index)
        {
            var expectedClosings = new Stack<char>();
            expectedClosings.Push(json[index++] == '{' ? '}' : ']');
            while (index < json.Length && expectedClosings.Count > 0)
            {
                char current = json[index];
                if (current == '"')
                {
                    if (!SkipJsonString(json, ref index))
                    {
                        return false;
                    }

                    continue;
                }

                index++;
                if (current == '{')
                {
                    expectedClosings.Push('}');
                }
                else if (current == '[')
                {
                    expectedClosings.Push(']');
                }
                else if (current == '}' || current == ']')
                {
                    if (expectedClosings.Pop() != current)
                    {
                        return false;
                    }
                }
            }

            return expectedClosings.Count == 0;
        }

        private static bool SkipJsonString(string json, ref int index)
        {
            if (!Consume(json, ref index, '"'))
            {
                return false;
            }

            while (index < json.Length)
            {
                char current = json[index++];
                if (current == '"')
                {
                    return true;
                }

                if (current == '\\')
                {
                    if (index >= json.Length)
                    {
                        return false;
                    }

                    char escaped = json[index++];
                    if (escaped == 'u')
                    {
                        for (int digit = 0; digit < 4; digit++)
                        {
                            if (index >= json.Length || !IsHexDigit(json[index++]))
                            {
                                return false;
                            }
                        }
                    }
                    else if (escaped != '"' && escaped != '\\' && escaped != '/' &&
                        escaped != 'b' && escaped != 'f' && escaped != 'n' &&
                        escaped != 'r' && escaped != 't')
                    {
                        return false;
                    }
                }
                else if (current < ' ')
                {
                    return false;
                }
            }

            return false;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }
        }

        private static bool Consume(string json, ref int index, char expected)
        {
            if (index >= json.Length || json[index] != expected)
            {
                return false;
            }

            index++;
            return true;
        }

        private static bool IsHexDigit(char value)
        {
            return (value >= '0' && value <= '9') ||
                (value >= 'a' && value <= 'f') ||
                (value >= 'A' && value <= 'F');
        }

        private static bool ContainsFullField(int[] plotNumbers)
        {
            if (plotNumbers == null || plotNumbers.Length != 9)
            {
                return false;
            }

            for (int index = 0; index < plotNumbers.Length; index++)
            {
                if (plotNumbers[index] != index + 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseEnum<T>(string value, out T parsed)
            where T : struct
        {
            return Enum.TryParse(value, false, out parsed) &&
                Enum.IsDefined(typeof(T), parsed);
        }

        private static bool IsProvider(string provider)
        {
            return provider == "mock" || provider == "openai";
        }

        private static bool IsBoundedText(string value, int minimumLength, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int length = value.Trim().Length;
            return length >= minimumLength && length <= maximumLength;
        }

        private static ActionResult InvalidResponse(string message)
        {
            return ActionResult.Failure(ActionFailureReason.InvalidResponse, message);
        }

        private static void EnsureResidentId(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "AI gateway JSON requires a valid ResidentId.",
                    nameof(residentId));
            }
        }

        [Serializable]
        private sealed class InterpretCommandRequestDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private string command;

            public InterpretCommandRequestDto(
                string residentId,
                string commandText)
            {
                resident_id = residentId;
                command = commandText;
            }
        }

        [Serializable]
        private sealed class GenerateUtteranceRequestDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private string trigger;

            [SerializeField]
            private string context;

            public GenerateUtteranceRequestDto(
                string residentId,
                string triggerName,
                string boundedContext)
            {
                resident_id = residentId;
                trigger = triggerName;
                context = boundedContext;
            }
        }

        [Serializable]
        private sealed class ReflectRequestDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private FarmGoalDto goal;

            [SerializeField]
            private string outcome;

            [SerializeField]
            private string event_summary;

            public ReflectRequestDto(
                string residentId,
                FarmGoalDto farmGoal,
                string reflectionOutcome,
                string boundedEventSummary)
            {
                resident_id = residentId;
                goal = farmGoal;
                outcome = reflectionOutcome;
                event_summary = boundedEventSummary;
            }
        }

        [Serializable]
        private sealed class ConversationScriptRequestDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private string[] participant_ids;

            [SerializeField]
            private ResidentContextDto[] participants;

            [SerializeField]
            private string topic;

            [SerializeField]
            private int max_lines;

            public static ConversationScriptRequestDto FromRequest(
                ConversationScriptRequest request)
            {
                var ids = new string[request.ParticipantIds.Count];
                for (int index = 0; index < ids.Length; index++)
                {
                    ids[index] = request.ParticipantIds[index].Value;
                }

                var contexts = new ResidentContextDto[request.Participants.Count];
                for (int index = 0; index < contexts.Length; index++)
                {
                    contexts[index] = ResidentContextDto.FromContext(
                        request.Participants[index]);
                }

                return new ConversationScriptRequestDto
                {
                    resident_id = request.ResidentId.Value,
                    participant_ids = ids,
                    participants = contexts,
                    topic = request.Topic,
                    max_lines = request.MaxLines
                };
            }
        }

        [Serializable]
        private sealed class ResidentContextDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private ResidentPersonaDto persona;

            [SerializeField]
            private string current_state;

            [SerializeField]
            private RelationshipSnapshotDto[] relationship_snapshots;

            [SerializeField]
            private ResidentMemorySnapshotDto[] relevant_memories;

            public static ResidentContextDto FromContext(ResidentContext context)
            {
                var relationships = new RelationshipSnapshotDto[
                    context.RelationshipSnapshots.Count];
                for (int index = 0; index < relationships.Length; index++)
                {
                    RelationshipSnapshot relationship =
                        context.RelationshipSnapshots[index];
                    relationships[index] = new RelationshipSnapshotDto
                    {
                        owner_resident_id = relationship.OwnerResidentId.Value,
                        target_resident_id = relationship.TargetResidentId.Value,
                        familiarity = relationship.Familiarity,
                        trust = relationship.Trust
                    };
                }

                var memories = new ResidentMemorySnapshotDto[
                    context.RelevantMemories.Count];
                for (int index = 0; index < memories.Length; index++)
                {
                    ResidentMemorySnapshot memory = context.RelevantMemories[index];
                    memories[index] = new ResidentMemorySnapshotDto
                    {
                        owner_resident_id = memory.OwnerResidentId.Value,
                        text = memory.Text,
                        importance = memory.Importance,
                        knowledge_id = string.IsNullOrEmpty(memory.KnowledgeId)
                            ? null
                            : memory.KnowledgeId,
                        root_fact_id = string.IsNullOrEmpty(memory.RootFactId)
                            ? null
                            : memory.RootFactId,
                        tags = CopyStrings(memory.Tags),
                        is_shareable = memory.IsShareable,
                        immediate_source_resident_id =
                            memory.ImmediateSourceResidentId.HasValue
                                ? memory.ImmediateSourceResidentId.Value.Value
                                : null
                    };
                }

                return new ResidentContextDto
                {
                    resident_id = context.ResidentId.Value,
                    persona = ResidentPersonaDto.FromSnapshot(context.Persona),
                    current_state = context.CurrentState,
                    relationship_snapshots = relationships,
                    relevant_memories = memories
                };
            }
        }

        [Serializable]
        private sealed class ResidentPersonaDto
        {
            [SerializeField]
            private string display_name;

            [SerializeField]
            private string role;

            [SerializeField]
            private string[] personality_traits;

            [SerializeField]
            private string speaking_style;

            [SerializeField]
            private string work_habit;

            [SerializeField]
            private string preference;

            [SerializeField]
            private string dislike;

            public static ResidentPersonaDto FromSnapshot(
                ResidentPersonaSnapshot persona)
            {
                var traits = new string[persona.PersonalityTraits.Count];
                for (int index = 0; index < traits.Length; index++)
                {
                    traits[index] = persona.PersonalityTraits[index];
                }

                return new ResidentPersonaDto
                {
                    display_name = persona.DisplayName,
                    role = persona.Role,
                    personality_traits = traits,
                    speaking_style = persona.SpeakingStyle,
                    work_habit = persona.WorkHabit,
                    preference = persona.Preference,
                    dislike = persona.Dislike
                };
            }
        }

        [Serializable]
        private sealed class RelationshipSnapshotDto
        {
            [SerializeField]
            internal string owner_resident_id;

            [SerializeField]
            internal string target_resident_id;

            [SerializeField]
            internal int familiarity;

            [SerializeField]
            internal int trust;
        }

        [Serializable]
        private sealed class ResidentMemorySnapshotDto
        {
            [SerializeField]
            internal string owner_resident_id;

            [SerializeField]
            internal string text;

            [SerializeField]
            internal int importance;

            [SerializeField]
            internal string knowledge_id;

            [SerializeField]
            internal string root_fact_id;

            [SerializeField]
            internal string[] tags;

            [SerializeField]
            internal bool is_shareable;

            [SerializeField]
            internal string immediate_source_resident_id;
        }

        [Serializable]
        private sealed class FarmGoalDto
        {
            [SerializeField]
            private string goal_id;

            [SerializeField]
            private string crop;

            [SerializeField]
            private int[] target_plot_numbers;

            [SerializeField]
            private bool requires_sowing;

            [SerializeField]
            private bool requires_watering;

            [SerializeField]
            private bool requires_fertilizing;

            [SerializeField]
            private bool requires_weeding;

            [SerializeField]
            private bool requires_harvesting;

            [SerializeField]
            private string summary;

            public string GoalId => goal_id;

            public string Crop => crop;

            public int[] TargetPlotNumbers => target_plot_numbers;

            public bool RequiresSowing => requires_sowing;

            public bool RequiresWatering => requires_watering;

            public bool RequiresFertilizing => requires_fertilizing;

            public bool RequiresWeeding => requires_weeding;

            public bool RequiresHarvesting => requires_harvesting;

            public string Summary => summary;

            public static FarmGoalDto FromGoal(FarmGoalSpec goal)
            {
                return new FarmGoalDto
                {
                    goal_id = goal.GoalId,
                    crop = "carrot",
                    target_plot_numbers = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
                    requires_sowing = true,
                    requires_watering = true,
                    requires_fertilizing = true,
                    requires_weeding = true,
                    requires_harvesting = true,
                    summary = goal.Summary
                };
            }
        }

        [Serializable]
        private sealed class UtteranceDto
        {
            [SerializeField]
            private string trigger;

            [SerializeField]
            private string mood;

            [SerializeField]
            private string emoji;

            [SerializeField]
            private string text;

            [SerializeField]
            private string provider;

            public string Trigger => trigger;

            public string Mood => mood;

            public string Emoji => emoji;

            public string Text => text;

            public string Provider => provider;
        }

        [Serializable]
        private sealed class ReflectionDto
        {
            [SerializeField]
            private string goal_id;

            [SerializeField]
            private string outcome;

            [SerializeField]
            private string mood;

            [SerializeField]
            private string emoji;

            [SerializeField]
            private string text;

            [SerializeField]
            private string provider;

            public string GoalId => goal_id;

            public string Outcome => outcome;

            public string Mood => mood;

            public string Emoji => emoji;

            public string Text => text;

            public string Provider => provider;
        }

        [Serializable]
        private sealed class ConversationScriptResponseDto
        {
            [SerializeField]
            private string resident_id;

            [SerializeField]
            private ConversationLineDto[] lines;

            [SerializeField]
            private string outcome;

            [SerializeField]
            private string provider;

            public string ResidentId => resident_id;

            public ConversationLineDto[] Lines => lines;

            public string Outcome => outcome;

            public string Provider => provider;
        }

        [Serializable]
        private sealed class ConversationLineDto
        {
            [SerializeField]
            private string speaker_id;

            [SerializeField]
            private string mood;

            [SerializeField]
            private string emoji;

            [SerializeField]
            private string text;

            [SerializeField]
            private string shared_knowledge_id;

            public string SpeakerId => speaker_id;

            public string Mood => mood;

            public string Emoji => emoji;

            public string Text => text;

            public string SharedKnowledgeId => shared_knowledge_id;
        }
    }
}
