using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public enum MemoryEntryKind
    {
        Observation = 0,
        Reflection = 1,
        ConversationSummary = 2
    }

    public enum MemorySourceKind
    {
        LegacyImported = 0,
        Perception = 1,
        Conversation = 2,
        PlayerInput = 3,
        PublicTownEvent = 4,
        Reflection = 5
    }

    public sealed class MemoryEntry
    {
        public const int MinimumImportance = 1;
        public const int MaximumImportance = 10;
        public const int HighImportanceThreshold = 7;
        public const int MaximumIdentifierLength = 160;
        public const int MaximumTagLength = 48;
        public const int MaximumTagCount = 16;

        public MemoryEntry(
            ResidentId ownerResidentId,
            long sequence,
            double gameSeconds,
            MemoryEntryKind kind,
            string text,
            int importance,
            WorldEventKind? sourceEventKind = null,
            MemorySourceKind sourceKind = MemorySourceKind.LegacyImported,
            string sourceEventId = null,
            string knowledgeId = null,
            string rootFactId = null,
            string parentKnowledgeId = null,
            ResidentId immediateSourceResidentId = default,
            IEnumerable<string> tags = null,
            bool isShareable = true)
        {
            if (!ownerResidentId.IsValid)
            {
                throw new ArgumentException("A memory entry requires a valid owner ResidentId.", nameof(ownerResidentId));
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("A memory entry requires text.", nameof(text));
            }

            if (importance < MinimumImportance || importance > MaximumImportance)
            {
                throw new ArgumentOutOfRangeException(nameof(importance));
            }

            if (!Enum.IsDefined(typeof(MemoryEntryKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (!Enum.IsDefined(typeof(MemorySourceKind), sourceKind))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
            }

            if (sourceEventKind.HasValue &&
                !Enum.IsDefined(typeof(WorldEventKind), sourceEventKind.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(sourceEventKind));
            }

            OwnerResidentId = ownerResidentId;
            Sequence = sequence;
            GameSeconds = gameSeconds;
            Kind = kind;
            Text = text.Trim();
            Importance = importance;
            SourceEventKind = sourceEventKind;
            SourceKind = sourceKind;
            SourceEventId = NormalizeOptionalIdentifier(sourceEventId, nameof(sourceEventId));
            KnowledgeId = string.IsNullOrWhiteSpace(knowledgeId)
                ? CreateKnowledgeId(ownerResidentId, sequence)
                : NormalizeRequiredIdentifier(knowledgeId, nameof(knowledgeId));
            RootFactId = string.IsNullOrWhiteSpace(rootFactId)
                ? KnowledgeId
                : NormalizeRequiredIdentifier(rootFactId, nameof(rootFactId));
            ParentKnowledgeId = NormalizeOptionalIdentifier(
                parentKnowledgeId,
                nameof(parentKnowledgeId));
            ImmediateSourceResidentId = immediateSourceResidentId;
            Tags = NormalizeTags(tags);
            IsShareable = isShareable;
        }

        public MemoryEntry(
            long sequence,
            double gameSeconds,
            MemoryEntryKind kind,
            string text,
            int importance,
            WorldEventKind? sourceEventKind = null,
            MemorySourceKind sourceKind = MemorySourceKind.LegacyImported,
            string sourceEventId = null,
            string knowledgeId = null,
            string rootFactId = null,
            string parentKnowledgeId = null,
            ResidentId immediateSourceResidentId = default,
            IEnumerable<string> tags = null,
            bool isShareable = true)
            : this(
                ResidentIds.Yaya,
                sequence,
                gameSeconds,
                kind,
                text,
                importance,
                sourceEventKind,
                sourceKind,
                sourceEventId,
                knowledgeId,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable)
        {
        }

        public ResidentId OwnerResidentId { get; }

        public long Sequence { get; }

        public double GameSeconds { get; }

        public MemoryEntryKind Kind { get; }

        public string Text { get; }

        public int Importance { get; }

        public WorldEventKind? SourceEventKind { get; }

        public MemorySourceKind SourceKind { get; }

        public string SourceEventId { get; }

        public string KnowledgeId { get; }

        public string RootFactId { get; }

        public string ParentKnowledgeId { get; }

        public ResidentId ImmediateSourceResidentId { get; }

        public IReadOnlyList<string> Tags { get; }

        public bool IsShareable { get; }

        public bool IsHighImportance => Importance >= HighImportanceThreshold;

        private static string CreateKnowledgeId(ResidentId ownerResidentId, long sequence)
        {
            return $"knowledge:{ownerResidentId.Value}:{sequence:D12}";
        }

        private static string NormalizeRequiredIdentifier(string value, string parameterName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumIdentifierLength)
            {
                throw new ArgumentException(
                    $"Memory identifiers must contain 1-{MaximumIdentifierLength} characters.",
                    parameterName);
            }

            return normalized;
        }

        private static string NormalizeOptionalIdentifier(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : NormalizeRequiredIdentifier(value, parameterName);
        }

        private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> values)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            var normalized = new List<string>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                string tag = (value ?? string.Empty).Trim().ToLowerInvariant();
                if (tag.Length == 0 || tag.Length > MaximumTagLength)
                {
                    throw new ArgumentException(
                        $"Memory tags must contain 1-{MaximumTagLength} characters.",
                        nameof(values));
                }

                if (unique.Add(tag))
                {
                    normalized.Add(tag);
                }

                if (normalized.Count > MaximumTagCount)
                {
                    throw new ArgumentException(
                        $"A memory may contain at most {MaximumTagCount} tags.",
                        nameof(values));
                }
            }

            return new ReadOnlyCollection<string>(normalized);
        }
    }
}
