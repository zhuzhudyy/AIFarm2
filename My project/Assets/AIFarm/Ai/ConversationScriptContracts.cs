using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Npc;
using AIFarm.Social;

namespace AIFarm.Ai
{
    public sealed class ResidentPersonaSnapshot
    {
        private readonly ReadOnlyCollection<string> personalityTraits;

        public ResidentPersonaSnapshot(
            string displayName,
            string role,
            IEnumerable<string> traits,
            string speakingStyle,
            string workHabit,
            string preference,
            string dislike)
        {
            DisplayName = RequireBounded(displayName, 1, 80, nameof(displayName));
            Role = RequireBounded(role, 1, 80, nameof(role));
            var validatedTraits = new List<string>();
            if (traits == null)
            {
                throw new ArgumentNullException(nameof(traits));
            }

            foreach (string trait in traits)
            {
                validatedTraits.Add(RequireBounded(trait, 1, 80, nameof(traits)));
            }

            if (validatedTraits.Count < 1 || validatedTraits.Count > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(traits),
                    "A resident persona requires 1-8 personality traits.");
            }

            personalityTraits = new ReadOnlyCollection<string>(validatedTraits);
            SpeakingStyle = BoundOptional(speakingStyle, 200);
            WorkHabit = BoundOptional(workHabit, 200);
            Preference = BoundOptional(preference, 200);
            Dislike = BoundOptional(dislike, 200);
        }

        public string DisplayName { get; }

        public string Role { get; }

        public IReadOnlyList<string> PersonalityTraits => personalityTraits;

        public string SpeakingStyle { get; }

        public string WorkHabit { get; }

        public string Preference { get; }

        public string Dislike { get; }

        public static ResidentPersonaSnapshot FromDefinition(ResidentDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            NpcPersonaDefinition persona = definition.Persona;
            return new ResidentPersonaSnapshot(
                definition.DisplayName,
                persona.Role,
                persona.PersonalityTraits,
                persona.SpeakingStyle,
                persona.WorkHabit,
                persona.Preference,
                persona.Dislike);
        }

        private static string RequireBounded(
            string value,
            int minimumLength,
            int maximumLength,
            string parameterName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length < minimumLength || normalized.Length > maximumLength)
            {
                throw new ArgumentException(
                    $"Text must contain {minimumLength}-{maximumLength} characters.",
                    parameterName);
            }

            return normalized;
        }

        private static string BoundOptional(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }

    public sealed class RelationshipSnapshot
    {
        public RelationshipSnapshot(
            ResidentId ownerResidentId,
            ResidentId targetResidentId,
            int familiarity,
            int trust)
        {
            if (!ownerResidentId.IsValid || !targetResidentId.IsValid ||
                ownerResidentId == targetResidentId)
            {
                throw new ArgumentException(
                    "A relationship snapshot requires two different resident IDs.");
            }

            if (familiarity < RelationshipState.MinimumValue ||
                familiarity > RelationshipState.MaximumValue ||
                trust < RelationshipState.MinimumValue ||
                trust > RelationshipState.MaximumValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(familiarity),
                    "Relationship values must be between -100 and 100.");
            }

            OwnerResidentId = ownerResidentId;
            TargetResidentId = targetResidentId;
            Familiarity = familiarity;
            Trust = trust;
        }

        public ResidentId OwnerResidentId { get; }

        public ResidentId TargetResidentId { get; }

        public int Familiarity { get; }

        public int Trust { get; }
    }

    public sealed class ResidentMemorySnapshot
    {
        private readonly ReadOnlyCollection<string> tags;

        public ResidentMemorySnapshot(
            ResidentId ownerResidentId,
            string text,
            int importance,
            string knowledgeId = null,
            string rootFactId = null,
            IEnumerable<string> memoryTags = null,
            bool isShareable = false,
            ResidentId? immediateSourceResidentId = null)
        {
            string normalized = (text ?? string.Empty).Trim();
            string normalizedKnowledgeId = (knowledgeId ?? string.Empty).Trim();
            string normalizedRootFactId = (rootFactId ?? string.Empty).Trim();
            if (!ownerResidentId.IsValid || normalized.Length < 1 ||
                normalized.Length > 500 || importance < MemoryEntry.MinimumImportance ||
                importance > MemoryEntry.MaximumImportance ||
                normalizedKnowledgeId.Length > MemoryEntry.MaximumIdentifierLength ||
                normalizedRootFactId.Length > MemoryEntry.MaximumIdentifierLength ||
                (isShareable && (normalizedKnowledgeId.Length == 0 ||
                    normalizedRootFactId.Length == 0)) ||
                (immediateSourceResidentId.HasValue &&
                    (!immediateSourceResidentId.Value.IsValid ||
                        immediateSourceResidentId.Value == ownerResidentId)))
            {
                throw new ArgumentException(
                    "A memory snapshot requires bounded owner-specific content and provenance.");
            }

            var normalizedTags = new List<string>();
            var uniqueTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string memoryTag in memoryTags ?? Array.Empty<string>())
            {
                string tag = (memoryTag ?? string.Empty).Trim();
                if (tag.Length < 1 || tag.Length > MemoryEntry.MaximumTagLength ||
                    !uniqueTags.Add(tag) ||
                    normalizedTags.Count >= MemoryEntry.MaximumTagCount)
                {
                    throw new ArgumentException(
                        "Memory snapshot tags must be unique and bounded.",
                        nameof(memoryTags));
                }

                normalizedTags.Add(tag);
            }

            OwnerResidentId = ownerResidentId;
            Text = normalized;
            Importance = importance;
            KnowledgeId = normalizedKnowledgeId;
            RootFactId = normalizedRootFactId;
            tags = new ReadOnlyCollection<string>(normalizedTags);
            IsShareable = isShareable;
            ImmediateSourceResidentId = immediateSourceResidentId;
        }

        public ResidentId OwnerResidentId { get; }

        public string Text { get; }

        public int Importance { get; }

        public string KnowledgeId { get; }

        public string RootFactId { get; }

        public IReadOnlyList<string> Tags => tags;

        public bool IsShareable { get; }

        public ResidentId? ImmediateSourceResidentId { get; }
    }

    public sealed class ResidentContext
    {
        private readonly ReadOnlyCollection<RelationshipSnapshot> relationshipSnapshots;
        private readonly ReadOnlyCollection<ResidentMemorySnapshot> relevantMemories;

        public ResidentContext(
            ResidentId residentId,
            ResidentPersonaSnapshot persona,
            string currentState,
            IEnumerable<RelationshipSnapshot> relationships,
            IEnumerable<ResidentMemorySnapshot> memories)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "ResidentContext requires a valid ResidentId.",
                    nameof(residentId));
            }

            ResidentId = residentId;
            Persona = persona ?? throw new ArgumentNullException(nameof(persona));
            CurrentState = BoundOptional(currentState, 200);
            relationshipSnapshots = new ReadOnlyCollection<RelationshipSnapshot>(
                ValidateRelationships(residentId, relationships));
            relevantMemories = new ReadOnlyCollection<ResidentMemorySnapshot>(
                ValidateMemories(residentId, memories));
        }

        public ResidentId ResidentId { get; }

        public ResidentPersonaSnapshot Persona { get; }

        public string CurrentState { get; }

        public IReadOnlyList<RelationshipSnapshot> RelationshipSnapshots =>
            relationshipSnapshots;

        public IReadOnlyList<ResidentMemorySnapshot> RelevantMemories => relevantMemories;

        private static List<RelationshipSnapshot> ValidateRelationships(
            ResidentId ownerResidentId,
            IEnumerable<RelationshipSnapshot> relationships)
        {
            if (relationships == null)
            {
                throw new ArgumentNullException(nameof(relationships));
            }

            var validated = new List<RelationshipSnapshot>();
            var targets = new HashSet<ResidentId>();
            foreach (RelationshipSnapshot relationship in relationships)
            {
                if (relationship == null || relationship.OwnerResidentId != ownerResidentId ||
                    !targets.Add(relationship.TargetResidentId))
                {
                    throw new ArgumentException(
                        "Relationship snapshots must be unique and owned by the context resident.",
                        nameof(relationships));
                }

                validated.Add(relationship);
            }

            if (validated.Count > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(relationships));
            }

            return validated;
        }

        private static List<ResidentMemorySnapshot> ValidateMemories(
            ResidentId ownerResidentId,
            IEnumerable<ResidentMemorySnapshot> memories)
        {
            if (memories == null)
            {
                throw new ArgumentNullException(nameof(memories));
            }

            var validated = new List<ResidentMemorySnapshot>();
            foreach (ResidentMemorySnapshot memory in memories)
            {
                if (memory == null || memory.OwnerResidentId != ownerResidentId)
                {
                    throw new ArgumentException(
                        "Private memories must be owned by the context resident.",
                        nameof(memories));
                }

                validated.Add(memory);
            }

            if (validated.Count > 24)
            {
                throw new ArgumentOutOfRangeException(nameof(memories));
            }

            return validated;
        }

        private static string BoundOptional(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }

    public sealed class ConversationScriptRequest
    {
        private readonly ReadOnlyCollection<ResidentId> participantIds;
        private readonly ReadOnlyCollection<ResidentContext> participants;

        public ConversationScriptRequest(
            ResidentId residentId,
            IEnumerable<ResidentId> conversationParticipantIds,
            IEnumerable<ResidentContext> participantContexts,
            string topic,
            int maxLines)
        {
            if (!residentId.IsValid || maxLines < ConversationSession.MinimumSentenceCount ||
                maxLines > ConversationSession.MaximumSentenceCount)
            {
                throw new ArgumentException(
                    "A conversation request requires an owner and a 2-6 line limit.");
            }

            var ids = new List<ResidentId>(conversationParticipantIds ??
                throw new ArgumentNullException(nameof(conversationParticipantIds)));
            var contexts = new List<ResidentContext>(participantContexts ??
                throw new ArgumentNullException(nameof(participantContexts)));
            if (ids.Count != 2 || contexts.Count != 2 || ids[0] == ids[1] ||
                !ids.Contains(residentId))
            {
                throw new ArgumentException(
                    "A conversation request requires exactly two different participants.");
            }

            var contextIds = new HashSet<ResidentId>();
            foreach (ResidentContext context in contexts)
            {
                if (context == null || !ids.Contains(context.ResidentId) ||
                    !contextIds.Add(context.ResidentId) ||
                    context.RelationshipSnapshots.Count != 1 ||
                    context.RelationshipSnapshots[0].TargetResidentId == context.ResidentId ||
                    !ids.Contains(context.RelationshipSnapshots[0].TargetResidentId))
                {
                    throw new ArgumentException(
                        "Participant contexts must be isolated and exactly match the two participants.");
                }
            }

            if (contextIds.Count != 2)
            {
                throw new ArgumentException(
                    "Participant contexts must exactly match participant IDs.");
            }

            ResidentId = residentId;
            participantIds = new ReadOnlyCollection<ResidentId>(ids);
            participants = new ReadOnlyCollection<ResidentContext>(contexts);
            Topic = BoundOptional(topic, 200);
            MaxLines = maxLines;
        }

        public ResidentId ResidentId { get; }

        public IReadOnlyList<ResidentId> ParticipantIds => participantIds;

        public IReadOnlyList<ResidentContext> Participants => participants;

        public string Topic { get; }

        public int MaxLines { get; }

        public bool IsParticipant(ResidentId residentId)
        {
            return participantIds.Contains(residentId);
        }

        private static string BoundOptional(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }

    public sealed class ConversationLineSpec
    {
        public ConversationLineSpec(
            ResidentId speakerId,
            NpcMood mood,
            string emoji,
            string text,
            string sharedKnowledgeId = null)
        {
            string normalizedEmoji = (emoji ?? string.Empty).Trim();
            string normalizedText = (text ?? string.Empty).Trim();
            string normalizedKnowledgeId = (sharedKnowledgeId ?? string.Empty).Trim();
            if (!speakerId.IsValid || !Enum.IsDefined(typeof(NpcMood), mood) ||
                normalizedEmoji.Length < 1 || normalizedEmoji.Length > 8 ||
                normalizedText.Length < 1 || normalizedText.Length > 300 ||
                normalizedKnowledgeId.Length > 160)
            {
                throw new ArgumentException(
                    "A conversation line requires a speaker, mood, emoji, bounded text, and an optional bounded knowledge ID.");
            }

            SpeakerId = speakerId;
            Mood = mood;
            Emoji = normalizedEmoji;
            Text = normalizedText;
            SharedKnowledgeId = normalizedKnowledgeId;
        }

        public ResidentId SpeakerId { get; }

        public NpcMood Mood { get; }

        public string Emoji { get; }

        public string Text { get; }

        public string SharedKnowledgeId { get; }
    }

    public sealed class ConversationScriptSpec
    {
        private readonly ReadOnlyCollection<ConversationLineSpec> lines;

        public ConversationScriptSpec(
            ResidentId residentId,
            IEnumerable<ConversationLineSpec> conversationLines,
            ConversationOutcome outcome,
            string provider)
        {
            var validatedLines = new List<ConversationLineSpec>(conversationLines ??
                throw new ArgumentNullException(nameof(conversationLines)));
            string normalizedProvider = (provider ?? string.Empty).Trim();
            if (!residentId.IsValid ||
                validatedLines.Count < ConversationSession.MinimumSentenceCount ||
                validatedLines.Count > ConversationSession.MaximumSentenceCount ||
                !Enum.IsDefined(typeof(ConversationOutcome), outcome) ||
                normalizedProvider.Length < 1 || normalizedProvider.Length > 32)
            {
                throw new ArgumentException(
                    "ConversationScriptSpec requires an owner, 2-6 lines, outcome, and provider.");
            }

            if (validatedLines.Exists(line => line == null))
            {
                throw new ArgumentException(
                    "Conversation script lines cannot contain null values.",
                    nameof(conversationLines));
            }

            ResidentId = residentId;
            lines = new ReadOnlyCollection<ConversationLineSpec>(validatedLines);
            Outcome = outcome;
            Provider = normalizedProvider;
        }

        public ResidentId ResidentId { get; }

        public IReadOnlyList<ConversationLineSpec> Lines => lines;

        public ConversationOutcome Outcome { get; }

        public string Provider { get; }
    }
}
