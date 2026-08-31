using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Town
{
    public sealed class TownScheduler
    {
        private readonly Dictionary<ResidentId, DailyScheduleDefinition> schedules =
            new Dictionary<ResidentId, DailyScheduleDefinition>();
        private readonly Dictionary<ResidentId, DailyScheduleEntry> activeEntries =
            new Dictionary<ResidentId, DailyScheduleEntry>();

        public int ScheduleCount => schedules.Count;

        public ActionResult RegisterSchedule(DailyScheduleDefinition schedule)
        {
            if (schedule == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A daily schedule is required.");
            }

            if (schedules.ContainsKey(schedule.ResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Resident '{schedule.ResidentId}' already has a daily schedule.");
            }

            schedules.Add(schedule.ResidentId, schedule);
            return ActionResult.Success($"Schedule registered for {schedule.ResidentId}.");
        }

        public ActionResult TryResolve(
            ResidentId residentId,
            int minuteOfDay,
            out DailyScheduleEntry entry)
        {
            entry = null;
            if (!residentId.IsValid || minuteOfDay < 0 ||
                minuteOfDay >= DailyScheduleDefinition.MinutesPerDay)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid resident and minute of day are required.");
            }

            if (!schedules.TryGetValue(residentId, out DailyScheduleDefinition schedule))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Resident '{residentId}' has no registered schedule.");
            }

            if (!schedule.TryResolve(minuteOfDay, out entry))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Resident '{residentId}' has no schedule entry at " +
                    $"{DailyScheduleDefinition.FormatTime(minuteOfDay)}.");
            }

            return ActionResult.Success();
        }

        public ActionResult Evaluate(
            ResidentId residentId,
            double elapsedGameSeconds,
            out DailyScheduleEntry entry,
            out bool changed)
        {
            entry = null;
            changed = false;
            if (double.IsNaN(elapsedGameSeconds) || double.IsInfinity(elapsedGameSeconds) ||
                elapsedGameSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Elapsed game time must be finite and non-negative.");
            }

            int minuteOfDay = GetMinuteOfDay(elapsedGameSeconds);
            ActionResult resolved = TryResolve(residentId, minuteOfDay, out entry);
            if (resolved.Failed)
            {
                activeEntries.Remove(residentId);
                return resolved;
            }

            changed = !activeEntries.TryGetValue(residentId, out DailyScheduleEntry previous) ||
                !EntriesMatch(previous, entry);
            activeEntries[residentId] = entry;
            return ActionResult.Success();
        }

        public ActionResult Reset(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid ResidentId is required.");
            }

            activeEntries.Remove(residentId);
            return ActionResult.Success();
        }

        public static int GetMinuteOfDay(double elapsedGameSeconds)
        {
            if (double.IsNaN(elapsedGameSeconds) || double.IsInfinity(elapsedGameSeconds) ||
                elapsedGameSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedGameSeconds));
            }

            long wholeMinutes = (long)Math.Floor(elapsedGameSeconds / 60d);
            return (int)(wholeMinutes % DailyScheduleDefinition.MinutesPerDay);
        }

        private static bool EntriesMatch(DailyScheduleEntry left, DailyScheduleEntry right)
        {
            return left != null && right != null &&
                left.StartMinute == right.StartMinute &&
                left.EndMinute == right.EndMinute &&
                left.TargetLocationId == right.TargetLocationId &&
                left.Activity == right.Activity;
        }
    }
}
