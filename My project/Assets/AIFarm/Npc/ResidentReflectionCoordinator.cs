using System;
using System.Collections.Generic;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class ResidentReflectionCoordinator
    {
        public const double GameSecondsPerDay = 24d * 60d * 60d;

        private readonly ResidentRegistry residentRegistry;
        private readonly Dictionary<ResidentId, int> lastReflectedDayByResident =
            new Dictionary<ResidentId, int>();
        private long observedDayIndex;

        public ResidentReflectionCoordinator(
            ResidentRegistry registry,
            double initialGameSeconds)
        {
            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            Synchronize(initialGameSeconds);
        }

        public long ObservedDayIndex => observedDayIndex;

        public void Synchronize(double gameSeconds)
        {
            if (!IsValidTime(gameSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            }

            observedDayIndex = (long)Math.Floor(gameSeconds / GameSecondsPerDay);
            lastReflectedDayByResident.Clear();
        }

        public ActionResult TickDayBoundaries(
            double gameSeconds,
            WorldEventLog eventLog,
            out int reflectionCount)
        {
            reflectionCount = 0;
            if (!IsValidTime(gameSeconds) || eventLog == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Daily reflection requires valid game time and an event log.");
            }

            long currentDayIndex = (long)Math.Floor(gameSeconds / GameSecondsPerDay);
            if (currentDayIndex < observedDayIndex)
            {
                observedDayIndex = currentDayIndex;
                lastReflectedDayByResident.Clear();
                return ActionResult.Success("Game time moved backward; reflection guards reset.");
            }

            if (currentDayIndex == observedDayIndex)
            {
                return ActionResult.Success("No game-day boundary crossed.");
            }

            while (observedDayIndex < currentDayIndex)
            {
                if (observedDayIndex >= int.MaxValue)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        "Daily reflection day index exceeds the supported save range.");
                }

                int completedDay = checked((int)observedDayIndex + 1);
                double boundaryTime = (observedDayIndex + 1) * GameSecondsPerDay;
                ActionResult reflected = ReflectAllResidents(
                    completedDay,
                    boundaryTime,
                    out int dayReflectionCount);
                if (reflected.Failed)
                {
                    return reflected;
                }

                reflectionCount += dayReflectionCount;
                ActionResult recorded = eventLog.RecordPublicTownEvent(
                    boundaryTime,
                    WorldEventKind.DayEnded,
                    $"第 {completedDay} 天结束，居民已完成各自的本地日终反思。",
                    tags: new[] { "day-end", "reflection", "major-event" });
                if (recorded.Failed)
                {
                    return recorded;
                }

                observedDayIndex++;
            }

            return ActionResult.Success(
                $"Recorded {reflectionCount} owner-scoped day-end reflections.");
        }

        private ActionResult ReflectAllResidents(
            int completedDay,
            double gameSeconds,
            out int reflectionCount)
        {
            reflectionCount = 0;
            foreach (ResidentId residentId in residentRegistry.ResidentIds)
            {
                if (lastReflectedDayByResident.TryGetValue(
                        residentId,
                        out int lastReflectedDay) &&
                    lastReflectedDay >= completedDay)
                {
                    continue;
                }

                ActionResult resolved = residentRegistry.TryGetRuntimeState(
                    residentId,
                    out ResidentRuntimeState runtimeState);
                if (resolved.Failed)
                {
                    return resolved;
                }

                var reflectionService = new ReflectionService(runtimeState);
                ActionResult reflected = reflectionService.CreateAndStoreLocalDayEndReflection(
                    completedDay,
                    gameSeconds,
                    out _);
                if (reflected.Failed)
                {
                    return reflected;
                }

                lastReflectedDayByResident[residentId] = completedDay;
                reflectionCount++;
            }

            return ActionResult.Success("All resident day-end reflections recorded locally.");
        }

        private static bool IsValidTime(double gameSeconds)
        {
            return !double.IsNaN(gameSeconds) &&
                !double.IsInfinity(gameSeconds) &&
                gameSeconds >= 0d;
        }
    }
}
