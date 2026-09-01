using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class MemoryStore
    {
        public const int DefaultCapacity = 32;
        public const int MaximumTextLength = 300;

        private readonly List<MemoryEntry> entries;
        private readonly ReadOnlyCollection<MemoryEntry> readOnlyEntries;
        private long nextSequence = 1;

        public MemoryStore(int capacity = DefaultCapacity)
            : this(ResidentIds.Yaya, capacity)
        {
        }

        public MemoryStore(ResidentId ownerResidentId, int capacity = DefaultCapacity)
        {
            if (!ownerResidentId.IsValid)
            {
                throw new ArgumentException(
                    "A MemoryStore requires a valid owner ResidentId.",
                    nameof(ownerResidentId));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            OwnerResidentId = ownerResidentId;
            Capacity = capacity;
            entries = new List<MemoryEntry>(capacity);
            readOnlyEntries = new ReadOnlyCollection<MemoryEntry>(entries);
        }

        public ResidentId OwnerResidentId { get; }

        public int Capacity { get; }

        public IReadOnlyList<MemoryEntry> Entries => readOnlyEntries;

        public ActionResult AddObservation(
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            out MemoryEntry entry)
        {
            return Add(
                gameSeconds,
                MemoryEntryKind.Observation,
                text,
                importance,
                sourceEventKind,
                MemorySourceKind.Perception,
                sourceEventId: null,
                rootFactId: null,
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: null,
                isShareable: true,
                out entry);
        }

        public ActionResult AddObservation(
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            MemorySourceKind sourceKind,
            string rootFactId,
            string parentKnowledgeId,
            ResidentId immediateSourceResidentId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            return AddObservation(
                gameSeconds,
                text,
                importance,
                sourceEventKind,
                sourceKind,
                sourceEventId: null,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddObservation(
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            MemorySourceKind sourceKind,
            string sourceEventId,
            string rootFactId,
            string parentKnowledgeId,
            ResidentId immediateSourceResidentId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            return Add(
                gameSeconds,
                MemoryEntryKind.Observation,
                text,
                importance,
                sourceEventKind,
                sourceKind,
                sourceEventId,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddObservation(
            ResidentId residentId,
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            out MemoryEntry entry)
        {
            ActionResult access = ValidateOwner(residentId);
            if (access.Failed)
            {
                entry = null;
                return access;
            }

            return AddObservation(
                gameSeconds,
                text,
                importance,
                sourceEventKind,
                out entry);
        }

        public ActionResult AddObservation(
            ResidentId requesterResidentId,
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            MemorySourceKind sourceKind,
            string rootFactId,
            string parentKnowledgeId,
            ResidentId immediateSourceResidentId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                entry = null;
                return access;
            }

            return AddObservation(
                gameSeconds,
                text,
                importance,
                sourceEventKind,
                sourceKind,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddObservation(
            ResidentId requesterResidentId,
            double gameSeconds,
            string text,
            int importance,
            WorldEventKind sourceEventKind,
            MemorySourceKind sourceKind,
            string sourceEventId,
            string rootFactId,
            string parentKnowledgeId,
            ResidentId immediateSourceResidentId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                entry = null;
                return access;
            }

            return AddObservation(
                gameSeconds,
                text,
                importance,
                sourceEventKind,
                sourceKind,
                sourceEventId,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddConversationSummary(
            ResidentId requesterResidentId,
            double gameSeconds,
            string text,
            int importance,
            ResidentId immediateSourceResidentId,
            string rootFactId,
            string parentKnowledgeId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            return AddConversationSummary(
                requesterResidentId,
                gameSeconds,
                text,
                importance,
                immediateSourceResidentId,
                sourceEventId: null,
                rootFactId,
                parentKnowledgeId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddConversationSummary(
            ResidentId requesterResidentId,
            double gameSeconds,
            string text,
            int importance,
            ResidentId immediateSourceResidentId,
            string sourceEventId,
            string rootFactId,
            string parentKnowledgeId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                entry = null;
                return access;
            }

            return Add(
                gameSeconds,
                MemoryEntryKind.ConversationSummary,
                text,
                importance,
                WorldEventKind.ConversationCompleted,
                MemorySourceKind.Conversation,
                sourceEventId,
                rootFactId,
                parentKnowledgeId,
                immediateSourceResidentId,
                tags,
                isShareable,
                out entry);
        }

        public ActionResult AddDayEndReflection(
            ResidentId requesterResidentId,
            int completedDay,
            double gameSeconds,
            string text,
            out MemoryEntry entry)
        {
            entry = null;
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                return access;
            }

            if (completedDay < 1)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A day-end reflection requires a positive completed day.");
            }

            return Add(
                gameSeconds,
                MemoryEntryKind.Reflection,
                text,
                MemoryEntry.MaximumImportance,
                WorldEventKind.DayEnded,
                MemorySourceKind.Reflection,
                sourceEventId: $"day-end:{completedDay:D8}",
                rootFactId: $"day-end:{OwnerResidentId.Value}:{completedDay:D8}",
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: new[] { "reflection", "day-end", $"day-{completedDay}" },
                isShareable: false,
                out entry);
        }

        public ActionResult AddReflection(
            double gameSeconds,
            string text,
            out MemoryEntry entry)
        {
            return Add(
                gameSeconds,
                MemoryEntryKind.Reflection,
                text,
                MemoryEntry.MaximumImportance,
                null,
                MemorySourceKind.Reflection,
                sourceEventId: null,
                rootFactId: null,
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: new[] { "reflection" },
                isShareable: false,
                out entry);
        }

        public ActionResult AddReflection(
            ResidentId residentId,
            double gameSeconds,
            string text,
            out MemoryEntry entry)
        {
            ActionResult access = ValidateOwner(residentId);
            if (access.Failed)
            {
                entry = null;
                return access;
            }

            return AddReflection(gameSeconds, text, out entry);
        }

        public IReadOnlyList<MemoryEntry> GetRecent(int count)
        {
            return SelectRecent(count, null, minimumImportance: null);
        }

        public ActionResult GetRecent(
            ResidentId requesterResidentId,
            int count,
            out IReadOnlyList<MemoryEntry> memories)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            memories = access.Succeeded
                ? GetRecent(count)
                : Array.Empty<MemoryEntry>();
            return access;
        }

        public ActionResult GetRecentObservations(
            ResidentId requesterResidentId,
            int count,
            out IReadOnlyList<MemoryEntry> memories)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            memories = access.Succeeded
                ? GetRecentObservations(count)
                : Array.Empty<MemoryEntry>();
            return access;
        }

        public ActionResult GetRecentReflections(
            ResidentId requesterResidentId,
            int count,
            out IReadOnlyList<MemoryEntry> memories)
        {
            ActionResult access = ValidateOwner(requesterResidentId);
            memories = access.Succeeded
                ? GetRecentReflections(count)
                : Array.Empty<MemoryEntry>();
            return access;
        }

        public ActionResult Query(
            ResidentId requesterResidentId,
            IEnumerable<string> tags,
            int count,
            out IReadOnlyList<MemoryEntry> memories,
            int minimumImportance = MemoryEntry.MinimumImportance)
        {
            memories = Array.Empty<MemoryEntry>();
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                return access;
            }

            if (count <= 0 ||
                minimumImportance < MemoryEntry.MinimumImportance ||
                minimumImportance > MemoryEntry.MaximumImportance)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Memory query count and minimum importance are invalid.");
            }

            ActionResult normalized = TryNormalizeQueryTags(tags, out HashSet<string> requestedTags);
            if (normalized.Failed)
            {
                return normalized;
            }

            var matches = new List<QueryMatch>();
            foreach (MemoryEntry candidate in entries)
            {
                if (candidate.Importance < minimumImportance)
                {
                    continue;
                }

                int tagMatches = CountTagMatches(candidate.Tags, requestedTags);
                if (requestedTags.Count > 0 && tagMatches == 0)
                {
                    continue;
                }

                matches.Add(new QueryMatch(candidate, tagMatches));
            }

            matches.Sort((left, right) =>
            {
                int tagOrder = right.TagMatches.CompareTo(left.TagMatches);
                if (tagOrder != 0)
                {
                    return tagOrder;
                }

                int importanceOrder = right.Entry.Importance.CompareTo(left.Entry.Importance);
                if (importanceOrder != 0)
                {
                    return importanceOrder;
                }

                int timeOrder = right.Entry.GameSeconds.CompareTo(left.Entry.GameSeconds);
                return timeOrder != 0
                    ? timeOrder
                    : right.Entry.Sequence.CompareTo(left.Entry.Sequence);
            });

            var selected = new List<MemoryEntry>(Math.Min(count, matches.Count));
            for (int index = 0; index < matches.Count && selected.Count < count; index++)
            {
                selected.Add(matches[index].Entry);
            }

            memories = new ReadOnlyCollection<MemoryEntry>(selected);
            return ActionResult.Success($"Found {selected.Count} owner-scoped memories.");
        }

        public ActionResult ContainsRootFact(
            ResidentId requesterResidentId,
            string rootFactId,
            out bool contains)
        {
            contains = false;
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                return access;
            }

            string normalized = (rootFactId ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MemoryEntry.MaximumIdentifierLength)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid root fact id is required.");
            }

            foreach (MemoryEntry entry in entries)
            {
                if (string.Equals(entry.RootFactId, normalized, StringComparison.Ordinal))
                {
                    contains = true;
                    break;
                }
            }

            return ActionResult.Success("Owner-scoped root fact lookup completed.");
        }

        public ActionResult TryGetKnowledge(
            ResidentId requesterResidentId,
            string knowledgeId,
            out MemoryEntry memory)
        {
            memory = null;
            ActionResult access = ValidateOwner(requesterResidentId);
            if (access.Failed)
            {
                return access;
            }

            string normalized = (knowledgeId ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MemoryEntry.MaximumIdentifierLength)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid knowledge id is required.");
            }

            foreach (MemoryEntry entry in entries)
            {
                if (string.Equals(entry.KnowledgeId, normalized, StringComparison.Ordinal))
                {
                    memory = entry;
                    return ActionResult.Success("Owner-scoped knowledge found.");
                }
            }

            return ActionResult.Failure(
                ActionFailureReason.InvalidArgument,
                $"Knowledge '{normalized}' does not belong to '{OwnerResidentId}'.");
        }

        public IReadOnlyList<MemoryEntry> GetRecentObservations(int count)
        {
            return SelectRecent(count, MemoryEntryKind.Observation, minimumImportance: null);
        }

        public IReadOnlyList<MemoryEntry> GetRecentReflections(int count)
        {
            return SelectRecent(count, MemoryEntryKind.Reflection, minimumImportance: null);
        }

        public IReadOnlyList<MemoryEntry> GetImportantRecentObservations(int count)
        {
            if (count <= 0)
            {
                return Array.Empty<MemoryEntry>();
            }

            var selected = new List<MemoryEntry>();
            foreach (MemoryEntry entry in entries)
            {
                if (entry.Kind == MemoryEntryKind.Observation && entry.IsHighImportance)
                {
                    selected.Add(entry);
                }
            }

            selected.Sort((left, right) =>
            {
                int importanceOrder = right.Importance.CompareTo(left.Importance);
                return importanceOrder != 0
                    ? importanceOrder
                    : right.Sequence.CompareTo(left.Sequence);
            });
            if (selected.Count > count)
            {
                selected.RemoveRange(count, selected.Count - count);
            }

            return new ReadOnlyCollection<MemoryEntry>(selected);
        }

        public ActionResult Clear()
        {
            entries.Clear();
            nextSequence = 1;
            return ActionResult.Success("NPC memories cleared.");
        }

        public ActionResult Restore(IEnumerable<MemoryEntry> savedEntries)
        {
            if (savedEntries == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Saved NPC memories are required.");
            }

            var validated = new List<MemoryEntry>();
            var knowledgeIds = new HashSet<string>(StringComparer.Ordinal);
            long previousSequence = 0;
            foreach (MemoryEntry entry in savedEntries)
            {
                if (entry == null ||
                    entry.OwnerResidentId != OwnerResidentId ||
                    entry.Sequence <= previousSequence ||
                    entry.Sequence == long.MaxValue ||
                    entry.Text.Length > MaximumTextLength ||
                    !Enum.IsDefined(typeof(MemoryEntryKind), entry.Kind) ||
                    !Enum.IsDefined(typeof(MemorySourceKind), entry.SourceKind) ||
                    string.IsNullOrWhiteSpace(entry.KnowledgeId) ||
                    string.IsNullOrWhiteSpace(entry.RootFactId) ||
                    entry.Tags == null ||
                    entry.Tags.Count > MemoryEntry.MaximumTagCount ||
                    !knowledgeIds.Add(entry.KnowledgeId) ||
                    !HasValidProvenance(entry) ||
                    (entry.SourceEventKind.HasValue &&
                        !Enum.IsDefined(typeof(WorldEventKind), entry.SourceEventKind.Value)))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Saved NPC memories contain an invalid entry.");
                }

                validated.Add(entry);
                previousSequence = entry.Sequence;
            }

            if (validated.Count > Capacity)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Saved NPC memories exceed the {Capacity}-entry limit.");
            }

            entries.Clear();
            entries.AddRange(validated);
            nextSequence = validated.Count == 0
                ? 1
                : validated[validated.Count - 1].Sequence + 1;
            return ActionResult.Success("NPC memories restored.");
        }

        private ActionResult Add(
            double gameSeconds,
            MemoryEntryKind kind,
            string text,
            int importance,
            WorldEventKind? sourceEventKind,
            MemorySourceKind sourceKind,
            string sourceEventId,
            string rootFactId,
            string parentKnowledgeId,
            ResidentId immediateSourceResidentId,
            IEnumerable<string> tags,
            bool isShareable,
            out MemoryEntry entry)
        {
            entry = null;
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Memory time must be finite and non-negative.");
            }

            string normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > MaximumTextLength)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Memory text must contain 1-{MaximumTextLength} characters.");
            }

            if (importance < MemoryEntry.MinimumImportance ||
                importance > MemoryEntry.MaximumImportance)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Memory importance must be between 1 and 10.");
            }

            if (!Enum.IsDefined(typeof(MemoryEntryKind), kind) ||
                !Enum.IsDefined(typeof(MemorySourceKind), sourceKind) ||
                (sourceEventKind.HasValue &&
                    !Enum.IsDefined(typeof(WorldEventKind), sourceEventKind.Value)))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Memory kind or source kind is invalid.");
            }

            if (sourceKind == MemorySourceKind.Conversation &&
                (!immediateSourceResidentId.IsValid ||
                    immediateSourceResidentId == OwnerResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Conversation memories require a different valid immediate source resident.");
            }

            if (sourceKind != MemorySourceKind.Conversation &&
                immediateSourceResidentId.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Only conversation memories may name an immediate source resident.");
            }

            if (kind == MemoryEntryKind.Reflection &&
                (sourceKind != MemorySourceKind.Reflection || isShareable))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Reflection memories must use the reflection source and stay private.");
            }

            if (kind == MemoryEntryKind.ConversationSummary &&
                (sourceKind != MemorySourceKind.Conversation || isShareable))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Conversation summaries must stay private and retain their speaker source.");
            }

            if (sourceKind == MemorySourceKind.LegacyImported && isShareable)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Legacy imported memories cannot be propagated without provenance.");
            }

            if (sourceKind == MemorySourceKind.Conversation && isShareable &&
                string.IsNullOrWhiteSpace(parentKnowledgeId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A shareable received fact requires its source knowledge id.");
            }

            try
            {
                entry = new MemoryEntry(
                    OwnerResidentId,
                    nextSequence,
                    gameSeconds,
                    kind,
                    normalized,
                    importance,
                    sourceEventKind,
                    sourceKind,
                    sourceEventId,
                    knowledgeId: null,
                    rootFactId,
                    parentKnowledgeId,
                    immediateSourceResidentId,
                    tags,
                    isShareable);
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    exception.Message);
            }

            if (entries.Count == Capacity)
            {
                RemoveLeastImportantOldestEntry();
            }

            nextSequence++;
            entries.Add(entry);
            return ActionResult.Success("NPC memory stored.");
        }

        private bool HasValidProvenance(MemoryEntry entry)
        {
            bool hasImmediateSource = entry.ImmediateSourceResidentId.IsValid;
            if (entry.SourceKind == MemorySourceKind.Conversation)
            {
                if (!hasImmediateSource ||
                    entry.ImmediateSourceResidentId == OwnerResidentId ||
                    (entry.IsShareable &&
                        string.IsNullOrWhiteSpace(entry.ParentKnowledgeId)))
                {
                    return false;
                }
            }
            else if (hasImmediateSource)
            {
                return false;
            }

            if (entry.Kind == MemoryEntryKind.Reflection &&
                (entry.SourceKind != MemorySourceKind.Reflection || entry.IsShareable))
            {
                return false;
            }

            if (entry.Kind == MemoryEntryKind.ConversationSummary &&
                (entry.SourceKind != MemorySourceKind.Conversation || entry.IsShareable))
            {
                return false;
            }

            return entry.SourceKind != MemorySourceKind.LegacyImported ||
                !entry.IsShareable;
        }

        private IReadOnlyList<MemoryEntry> SelectRecent(
            int count,
            MemoryEntryKind? kind,
            int? minimumImportance)
        {
            if (count <= 0)
            {
                return Array.Empty<MemoryEntry>();
            }

            var selected = new List<MemoryEntry>(Math.Min(count, entries.Count));
            for (int index = entries.Count - 1; index >= 0 && selected.Count < count; index--)
            {
                MemoryEntry candidate = entries[index];
                if (kind.HasValue && candidate.Kind != kind.Value)
                {
                    continue;
                }

                if (minimumImportance.HasValue && candidate.Importance < minimumImportance.Value)
                {
                    continue;
                }

                selected.Add(candidate);
            }

            return new ReadOnlyCollection<MemoryEntry>(selected);
        }

        private void RemoveLeastImportantOldestEntry()
        {
            int removalIndex = 0;
            int lowestImportance = entries[0].Importance;
            for (int index = 1; index < entries.Count; index++)
            {
                if (entries[index].Importance < lowestImportance)
                {
                    removalIndex = index;
                    lowestImportance = entries[index].Importance;
                }
            }

            entries.RemoveAt(removalIndex);
        }

        private ActionResult ValidateOwner(ResidentId residentId)
        {
            if (!residentId.IsValid || residentId != OwnerResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"MemoryStore '{OwnerResidentId}' cannot be accessed as '{residentId}'.");
            }

            return ActionResult.Success($"MemoryStore owner '{OwnerResidentId}' verified.");
        }

        private static ActionResult TryNormalizeQueryTags(
            IEnumerable<string> tags,
            out HashSet<string> normalized)
        {
            normalized = new HashSet<string>(StringComparer.Ordinal);
            if (tags == null)
            {
                return ActionResult.Success("No memory tags requested.");
            }

            foreach (string value in tags)
            {
                string tag = (value ?? string.Empty).Trim().ToLowerInvariant();
                if (tag.Length == 0 || tag.Length > MemoryEntry.MaximumTagLength)
                {
                    normalized.Clear();
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"Memory query tags must contain 1-{MemoryEntry.MaximumTagLength} characters.");
                }

                normalized.Add(tag);
                if (normalized.Count > MemoryEntry.MaximumTagCount)
                {
                    normalized.Clear();
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"A memory query may contain at most {MemoryEntry.MaximumTagCount} tags.");
                }
            }

            return ActionResult.Success("Memory query tags normalized.");
        }

        private static int CountTagMatches(
            IReadOnlyList<string> candidateTags,
            HashSet<string> requestedTags)
        {
            if (requestedTags.Count == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (string tag in candidateTags)
            {
                if (requestedTags.Contains(tag))
                {
                    count++;
                }
            }

            return count;
        }

        private readonly struct QueryMatch
        {
            public QueryMatch(MemoryEntry entry, int tagMatches)
            {
                Entry = entry;
                TagMatches = tagMatches;
            }

            public MemoryEntry Entry { get; }

            public int TagMatches { get; }
        }
    }
}
