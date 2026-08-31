using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIFarm.Core
{
    public enum WorldEventKind
    {
        System,
        CommandAccepted,
        PlanDecision,
        ActionStarted,
        ActionCompleted,
        ActionFailed,
        MoistureChanged,
        WeedsAppeared,
        CropMatured,
        TimeControlChanged,
        GoalCompleted
    }

    public readonly struct WorldEventEntry
    {
        public WorldEventEntry(
            long sequence,
            double gameSeconds,
            WorldEventKind kind,
            string message,
            int? plotNumber)
        {
            Sequence = sequence;
            GameSeconds = gameSeconds;
            Kind = kind;
            Message = message ?? string.Empty;
            PlotNumber = plotNumber;
        }

        public long Sequence { get; }

        public double GameSeconds { get; }

        public WorldEventKind Kind { get; }

        public string Message { get; }

        public int? PlotNumber { get; }
    }

    public sealed class WorldEventLog
    {
        public const int DefaultCapacity = 10;

        private readonly List<WorldEventEntry> entries;
        private readonly ReadOnlyCollection<WorldEventEntry> readOnlyEntries;
        private long nextSequence = 1;

        public event Action<WorldEventEntry> EntryRecorded;

        public WorldEventLog(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Event capacity must be positive.");
            }

            Capacity = capacity;
            entries = new List<WorldEventEntry>(capacity);
            readOnlyEntries = new ReadOnlyCollection<WorldEventEntry>(entries);
        }

        public int Capacity { get; }

        public IReadOnlyList<WorldEventEntry> Entries => readOnlyEntries;

        public ActionResult Record(
            double gameSeconds,
            WorldEventKind kind,
            string message,
            int? plotNumber = null)
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

            if (entries.Count == Capacity)
            {
                entries.RemoveAt(0);
            }

            var entry = new WorldEventEntry(
                nextSequence++,
                gameSeconds,
                kind,
                message,
                plotNumber);
            entries.Add(entry);
            EntryRecorded?.Invoke(entry);
            return ActionResult.Success("World event recorded.");
        }

        public ActionResult Clear()
        {
            entries.Clear();
            return ActionResult.Success("World events cleared.");
        }
    }
}
