using System;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public enum MemoryEntryKind
    {
        Observation,
        Reflection
    }

    public sealed class MemoryEntry
    {
        public const int MinimumImportance = 1;
        public const int MaximumImportance = 10;
        public const int HighImportanceThreshold = 7;

        public MemoryEntry(
            long sequence,
            double gameSeconds,
            MemoryEntryKind kind,
            string text,
            int importance,
            WorldEventKind? sourceEventKind = null)
        {
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

            Sequence = sequence;
            GameSeconds = gameSeconds;
            Kind = kind;
            Text = text.Trim();
            Importance = importance;
            SourceEventKind = sourceEventKind;
        }

        public long Sequence { get; }

        public double GameSeconds { get; }

        public MemoryEntryKind Kind { get; }

        public string Text { get; }

        public int Importance { get; }

        public WorldEventKind? SourceEventKind { get; }

        public bool IsHighImportance => Importance >= HighImportanceThreshold;
    }
}
