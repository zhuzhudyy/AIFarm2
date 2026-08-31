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
            long previousSequence = 0;
            foreach (MemoryEntry entry in savedEntries)
            {
                if (entry == null ||
                    entry.OwnerResidentId != OwnerResidentId ||
                    entry.Sequence <= previousSequence ||
                    entry.Sequence == long.MaxValue ||
                    entry.Text.Length > MaximumTextLength ||
                    !Enum.IsDefined(typeof(MemoryEntryKind), entry.Kind) ||
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

            if (entries.Count == Capacity)
            {
                RemoveLeastImportantOldestEntry();
            }

            entry = new MemoryEntry(
                OwnerResidentId,
                nextSequence++,
                gameSeconds,
                kind,
                normalized,
                importance,
                sourceEventKind);
            entries.Add(entry);
            return ActionResult.Success("NPC memory stored.");
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
    }
}
