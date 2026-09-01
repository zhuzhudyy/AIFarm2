using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Npc;

namespace AIFarm.Core
{
    public enum WorldEventKind
    {
        System = 0,
        CommandAccepted = 1,
        PlanDecision = 2,
        ActionStarted = 3,
        ActionCompleted = 4,
        ActionFailed = 5,
        MoistureChanged = 6,
        WeedsAppeared = 7,
        CropMatured = 8,
        TimeControlChanged = 9,
        GoalCompleted = 10,
        ConversationCompleted = 11,
        DayEnded = 12,
        ConversationInterrupted = 13
    }

    public enum WorldEventVisibility
    {
        Private = 0,
        Conversation = 1,
        Perceivable = 2,
        PublicTownEvent = 3
    }

    public readonly struct WorldEventEntry
    {
        public WorldEventEntry(
            long sequence,
            double gameSeconds,
            WorldEventKind kind,
            string message,
            int? plotNumber,
            WorldEventVisibility visibility = WorldEventVisibility.PublicTownEvent,
            ResidentId? actorResidentId = null,
            IEnumerable<ResidentId> audienceResidentIds = null,
            IEnumerable<ResidentId> observerResidentIds = null,
            string eventId = null,
            string factId = null,
            IEnumerable<string> tags = null)
        {
            Sequence = sequence;
            GameSeconds = gameSeconds;
            Kind = kind;
            Message = message ?? string.Empty;
            PlotNumber = plotNumber;
            Visibility = visibility;
            ActorResidentId = actorResidentId;
            AudienceResidentIds = NormalizeResidentIds(audienceResidentIds);
            ObserverResidentIds = NormalizeResidentIds(observerResidentIds);
            EventId = string.IsNullOrWhiteSpace(eventId)
                ? CreateEventId(sequence)
                : eventId.Trim();
            FactId = string.IsNullOrWhiteSpace(factId)
                ? EventId
                : factId.Trim();
            Tags = NormalizeTags(tags);
        }

        public long Sequence { get; }

        public double GameSeconds { get; }

        public WorldEventKind Kind { get; }

        public string Message { get; }

        public int? PlotNumber { get; }

        public string EventId { get; }

        public ResidentId? ActorResidentId { get; }

        public WorldEventVisibility Visibility { get; }

        public IReadOnlyList<ResidentId> AudienceResidentIds { get; }

        public IReadOnlyList<ResidentId> AudienceIds => AudienceResidentIds;

        public IReadOnlyList<ResidentId> ObserverResidentIds { get; }

        public IReadOnlyList<ResidentId> ObserverIds => ObserverResidentIds;

        public string FactId { get; }

        public IReadOnlyList<string> Tags { get; }

        public bool IsVisibleTo(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                return false;
            }

            switch (Visibility)
            {
                case WorldEventVisibility.PublicTownEvent:
                    return true;
                case WorldEventVisibility.Private:
                    return ActorResidentId.HasValue && ActorResidentId.Value == residentId ||
                        Contains(AudienceResidentIds, residentId);
                case WorldEventVisibility.Conversation:
                    return Contains(AudienceResidentIds, residentId);
                case WorldEventVisibility.Perceivable:
                    return Contains(ObserverResidentIds, residentId);
                default:
                    return false;
            }
        }

        private static string CreateEventId(long sequence)
        {
            return $"world-event:{sequence:D12}";
        }

        private static IReadOnlyList<ResidentId> NormalizeResidentIds(
            IEnumerable<ResidentId> values)
        {
            if (values == null)
            {
                return Array.Empty<ResidentId>();
            }

            var normalized = new List<ResidentId>();
            var unique = new HashSet<ResidentId>();
            foreach (ResidentId residentId in values)
            {
                if (!residentId.IsValid)
                {
                    throw new ArgumentException("World-event audiences require valid ResidentIds.");
                }

                if (unique.Add(residentId))
                {
                    normalized.Add(residentId);
                }
            }

            return new ReadOnlyCollection<ResidentId>(normalized);
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
                if (tag.Length == 0 || tag.Length > MemoryEntry.MaximumTagLength)
                {
                    throw new ArgumentException(
                        $"World-event tags must contain 1-{MemoryEntry.MaximumTagLength} characters.");
                }

                if (unique.Add(tag))
                {
                    normalized.Add(tag);
                }

                if (normalized.Count > MemoryEntry.MaximumTagCount)
                {
                    throw new ArgumentException(
                        $"A world event may contain at most {MemoryEntry.MaximumTagCount} tags.");
                }
            }

            return new ReadOnlyCollection<string>(normalized);
        }

        private static bool Contains(IReadOnlyList<ResidentId> values, ResidentId residentId)
        {
            if (values == null)
            {
                return false;
            }

            foreach (ResidentId candidate in values)
            {
                if (candidate == residentId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class WorldEventLog
    {
        public const int DefaultCapacity = 10;

        private readonly List<WorldEventEntry> entries;
        private readonly ReadOnlyCollection<WorldEventEntry> readOnlyEntries;
        private readonly string eventNamespace;
        private long nextSequence = 1;

        public event Action<WorldEventEntry> EntryRecorded;

        public WorldEventLog(
            int capacity = DefaultCapacity,
            string stableEventNamespace = null)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Event capacity must be positive.");
            }

            Capacity = capacity;
            eventNamespace = string.IsNullOrWhiteSpace(stableEventNamespace)
                ? Guid.NewGuid().ToString("N")
                : stableEventNamespace.Trim();
            if (eventNamespace.Length > 80)
            {
                throw new ArgumentException(
                    "A world-event namespace may contain at most 80 characters.",
                    nameof(stableEventNamespace));
            }

            entries = new List<WorldEventEntry>(capacity);
            readOnlyEntries = new ReadOnlyCollection<WorldEventEntry>(entries);
        }

        public int Capacity { get; }

        public IReadOnlyList<WorldEventEntry> Entries => readOnlyEntries;

        public ActionResult Record(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            int? plotNumber = null,
            WorldEventVisibility visibility = WorldEventVisibility.PublicTownEvent,
            ResidentId? actorResidentId = null,
            IEnumerable<ResidentId> audienceResidentIds = null,
            IEnumerable<ResidentId> observerResidentIds = null,
            string factId = null,
            IEnumerable<string> tags = null)
        {
            if (double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds) || gameSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "World-event time must be finite and non-negative.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A world event requires a message.");
            }

            if (plotNumber.HasValue && (plotNumber.Value < 1 || plotNumber.Value > 9))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A world-event plot number must be between 1 and 9.");
            }

            if (!Enum.IsDefined(typeof(WorldEventKind), kind) ||
                !Enum.IsDefined(typeof(WorldEventVisibility), visibility) ||
                (actorResidentId.HasValue && !actorResidentId.Value.IsValid))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "World-event kind, visibility, or actor is invalid.");
            }

            var audience = new List<ResidentId>();
            var observers = new List<ResidentId>();
            ActionResult audienceResult = TryNormalizeResidentIds(
                audienceResidentIds,
                audience,
                "audience");
            if (audienceResult.Failed)
            {
                return audienceResult;
            }

            ActionResult observerResult = TryNormalizeResidentIds(
                observerResidentIds,
                observers,
                "observer");
            if (observerResult.Failed)
            {
                return observerResult;
            }

            if (visibility == WorldEventVisibility.Private &&
                audience.Count == 0 && !actorResidentId.HasValue)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Private world events require an actor or audience.");
            }

            if (visibility == WorldEventVisibility.Conversation &&
                (audience.Count != 2 || !actorResidentId.HasValue ||
                    !audience.Contains(actorResidentId.Value)))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Conversation world events require exactly two participants and a participant actor.");
            }

            if (visibility == WorldEventVisibility.Perceivable && observers.Count == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Perceivable world events require actual observers.");
            }

            string normalizedFactId = (factId ?? string.Empty).Trim();
            if (normalizedFactId.Length > MemoryEntry.MaximumIdentifierLength)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "World-event fact id is too long.");
            }

            IReadOnlyList<string> normalizedTags;
            try
            {
                normalizedTags = NormalizeTags(tags);
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    exception.Message);
            }

            if (entries.Count == Capacity)
            {
                entries.RemoveAt(0);
            }

            long sequence = nextSequence++;
            var entry = new WorldEventEntry(
                sequence,
                gameSeconds,
                kind,
                message,
                plotNumber,
                visibility,
                actorResidentId,
                audience,
                observers,
                eventId: CreateEventId(sequence),
                normalizedFactId,
                normalizedTags);
            entries.Add(entry);
            EntryRecorded?.Invoke(entry);
            return ActionResult.Success("World event recorded.");
        }

        private string CreateEventId(long sequence)
        {
            return $"world-event:{eventNamespace}:{sequence:D12}";
        }

        public ActionResult RecordPrivate(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            ResidentId ownerResidentId,
            int? plotNumber = null,
            string factId = null,
            IEnumerable<string> tags = null,
            ResidentId? actorResidentId = null)
        {
            return Record(
                gameSeconds,
                kind,
                message,
                plotNumber,
                WorldEventVisibility.Private,
                actorResidentId ?? ownerResidentId,
                new[] { ownerResidentId },
                observerResidentIds: null,
                factId,
                tags);
        }

        public ActionResult RecordConversation(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            ResidentId actorResidentId,
            IEnumerable<ResidentId> participantResidentIds,
            string factId = null,
            IEnumerable<string> tags = null)
        {
            return Record(
                gameSeconds,
                kind,
                message,
                plotNumber: null,
                WorldEventVisibility.Conversation,
                actorResidentId,
                participantResidentIds,
                observerResidentIds: null,
                factId,
                tags);
        }

        public ActionResult RecordPerceivable(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            IEnumerable<ResidentId> observerResidentIds,
            ResidentId? actorResidentId = null,
            int? plotNumber = null,
            string factId = null,
            IEnumerable<string> tags = null)
        {
            return Record(
                gameSeconds,
                kind,
                message,
                plotNumber,
                WorldEventVisibility.Perceivable,
                actorResidentId,
                audienceResidentIds: null,
                observerResidentIds,
                factId,
                tags);
        }

        public ActionResult RecordPublicTownEvent(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            ResidentId? actorResidentId = null,
            int? plotNumber = null,
            string factId = null,
            IEnumerable<string> tags = null)
        {
            return Record(
                gameSeconds,
                kind,
                message,
                plotNumber,
                WorldEventVisibility.PublicTownEvent,
                actorResidentId,
                audienceResidentIds: null,
                observerResidentIds: null,
                factId,
                tags);
        }

        public IReadOnlyList<WorldEventEntry> GetVisibleEntries(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "Visible world-event queries require a valid ResidentId.",
                    nameof(residentId));
            }

            var visible = new List<WorldEventEntry>();
            foreach (WorldEventEntry entry in entries)
            {
                if (entry.IsVisibleTo(residentId))
                {
                    visible.Add(entry);
                }
            }

            return new ReadOnlyCollection<WorldEventEntry>(visible);
        }

        public ActionResult Clear()
        {
            entries.Clear();
            return ActionResult.Success("World events cleared.");
        }

        private static ActionResult TryNormalizeResidentIds(
            IEnumerable<ResidentId> values,
            List<ResidentId> normalized,
            string label)
        {
            var unique = new HashSet<ResidentId>();
            if (values == null)
            {
                return ActionResult.Success($"No world-event {label}s supplied.");
            }

            foreach (ResidentId residentId in values)
            {
                if (!residentId.IsValid)
                {
                    normalized.Clear();
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"World-event {label}s require valid ResidentIds.");
                }

                if (unique.Add(residentId))
                {
                    normalized.Add(residentId);
                }
            }

            return ActionResult.Success($"World-event {label}s normalized.");
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
                if (tag.Length == 0 || tag.Length > MemoryEntry.MaximumTagLength)
                {
                    throw new ArgumentException(
                        $"World-event tags must contain 1-{MemoryEntry.MaximumTagLength} characters.");
                }

                if (unique.Add(tag))
                {
                    normalized.Add(tag);
                }

                if (normalized.Count > MemoryEntry.MaximumTagCount)
                {
                    throw new ArgumentException(
                        $"A world event may contain at most {MemoryEntry.MaximumTagCount} tags.");
                }
            }

            return new ReadOnlyCollection<string>(normalized);
        }
    }
}
