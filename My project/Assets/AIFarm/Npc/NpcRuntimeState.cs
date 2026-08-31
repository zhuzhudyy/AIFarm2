using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class NpcRuntimeState
    {
        public const int ReflectionHistoryCapacity = 6;

        private readonly HashSet<int> reflectedCycleNumbers = new HashSet<int>();
        private readonly List<NpcReflection> recentReflections = new List<NpcReflection>();
        private readonly ReadOnlyCollection<NpcReflection> readOnlyReflections;
        private int startedCycleCount;

        public NpcRuntimeState(
            NpcPersonaDefinition persona = null,
            MemoryStore memoryStore = null)
        {
            Persona = persona ?? NpcPersonaDefinition.Yaya;
            Memories = memoryStore ?? new MemoryStore();
            readOnlyReflections = new ReadOnlyCollection<NpcReflection>(recentReflections);
        }

        public NpcPersonaDefinition Persona { get; }

        public MemoryStore Memories { get; }

        public int CurrentCycleNumber { get; private set; }

        public int CompletedCycleCount { get; private set; }

        public NpcReflection LatestReflection { get; private set; }

        public IReadOnlyList<NpcReflection> RecentReflections => readOnlyReflections;

        public int BeginCycle()
        {
            CurrentCycleNumber = ++startedCycleCount;
            return CurrentCycleNumber;
        }

        public bool HasReflectionForCycle(int cycleNumber)
        {
            return reflectedCycleNumbers.Contains(cycleNumber);
        }

        public ActionResult RecordCompletedCycleReflection(
            int cycleNumber,
            NpcReflection reflection,
            double gameSeconds)
        {
            if (cycleNumber <= 0 || cycleNumber > startedCycleCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Reflection cycle number is not an active or completed NPC cycle.");
            }

            if (reflection == null || reflection.Outcome != NpcReflectionOutcome.Completed)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A completed cycle requires a completed NPC reflection.");
            }

            if (reflectedCycleNumbers.Contains(cycleNumber))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Cycle {cycleNumber} already has a reflection.");
            }

            ActionResult stored = Memories.AddReflection(
                gameSeconds,
                reflection.Text,
                out _);
            if (stored.Failed)
            {
                return stored;
            }

            reflectedCycleNumbers.Add(cycleNumber);
            CompletedCycleCount++;
            LatestReflection = reflection;
            if (recentReflections.Count == ReflectionHistoryCapacity)
            {
                recentReflections.RemoveAt(0);
            }

            recentReflections.Add(reflection);
            return ActionResult.Success($"Reflection recorded for cycle {cycleNumber}.");
        }
    }
}
