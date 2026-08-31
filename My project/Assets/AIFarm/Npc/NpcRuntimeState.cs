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

        public int StartedCycleCount => startedCycleCount;

        public IReadOnlyList<int> ReflectedCycleNumbers
        {
            get
            {
                var cycleNumbers = new List<int>(reflectedCycleNumbers);
                cycleNumbers.Sort();
                return new ReadOnlyCollection<int>(cycleNumbers);
            }
        }

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

        public ActionResult Restore(
            int savedStartedCycleCount,
            int savedCurrentCycleNumber,
            int savedCompletedCycleCount,
            IEnumerable<int> savedReflectedCycleNumbers,
            IEnumerable<NpcReflection> savedRecentReflections)
        {
            if (savedStartedCycleCount < 0 ||
                savedCurrentCycleNumber < 0 ||
                savedCurrentCycleNumber > savedStartedCycleCount ||
                savedCompletedCycleCount < 0 ||
                savedCompletedCycleCount > savedStartedCycleCount ||
                savedReflectedCycleNumbers == null ||
                savedRecentReflections == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Saved NPC cycle state is invalid.");
            }

            var validatedCycles = new HashSet<int>();
            foreach (int cycleNumber in savedReflectedCycleNumbers)
            {
                if (cycleNumber <= 0 || cycleNumber > savedStartedCycleCount ||
                    !validatedCycles.Add(cycleNumber))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Saved NPC reflected cycle numbers are invalid.");
                }
            }

            if (validatedCycles.Count != savedCompletedCycleCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Saved NPC reflection count is inconsistent.");
            }

            var validatedReflections = new List<NpcReflection>();
            foreach (NpcReflection reflection in savedRecentReflections)
            {
                if (reflection == null ||
                    reflection.Outcome != NpcReflectionOutcome.Completed ||
                    string.IsNullOrWhiteSpace(reflection.Text) ||
                    reflection.Text.Length > MemoryStore.MaximumTextLength ||
                    !Enum.IsDefined(typeof(NpcMood), reflection.Mood))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Saved NPC reflection history is invalid.");
                }

                validatedReflections.Add(reflection);
            }

            if (validatedReflections.Count > ReflectionHistoryCapacity ||
                validatedReflections.Count > savedCompletedCycleCount ||
                (savedCompletedCycleCount > 0 && validatedReflections.Count == 0))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Saved NPC reflection history has an invalid length.");
            }

            startedCycleCount = savedStartedCycleCount;
            CurrentCycleNumber = savedCurrentCycleNumber;
            CompletedCycleCount = savedCompletedCycleCount;
            reflectedCycleNumbers.Clear();
            foreach (int cycleNumber in validatedCycles)
            {
                reflectedCycleNumbers.Add(cycleNumber);
            }

            recentReflections.Clear();
            recentReflections.AddRange(validatedReflections);
            LatestReflection = recentReflections.Count == 0
                ? null
                : recentReflections[recentReflections.Count - 1];
            return ActionResult.Success("NPC runtime state restored.");
        }
    }
}
