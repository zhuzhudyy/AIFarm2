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
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
            entries = new List<MemoryEntry>(capacity);
            readOnlyEntries = new ReadOnlyCollection<MemoryEntry>(entries);
        }

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

        public IReadOnlyList<MemoryEntry> GetRecent(int count)
        {
            return SelectRecent(count, null, minimumImportance: null);
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
            return ActionResult.Success("NPC memories cleared.");
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
    }
}
