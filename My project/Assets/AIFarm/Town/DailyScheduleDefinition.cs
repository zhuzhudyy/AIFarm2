using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using AIFarm.Npc;

namespace AIFarm.Town
{
    public enum ResidentActivityKind
    {
        Home = 0,
        Meal,
        Work,
        Read,
        Gather,
        FetchWater
    }

    public sealed class DailyScheduleEntry
    {
        public DailyScheduleEntry(
            string startTime,
            string endTime,
            TownLocationId targetLocationId,
            ResidentActivityKind activity)
            : this(
                ParseRequiredTime(startTime, nameof(startTime)),
                ParseRequiredTime(endTime, nameof(endTime)),
                targetLocationId,
                activity)
        {
        }

        public DailyScheduleEntry(
            int startMinute,
            int endMinute,
            TownLocationId targetLocationId,
            ResidentActivityKind activity)
        {
            if (startMinute < 0 || startMinute >= DailyScheduleDefinition.MinutesPerDay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startMinute),
                    "Schedule start minute must be between 0 and 1439.");
            }

            if (endMinute < 0 || endMinute > DailyScheduleDefinition.MinutesPerDay ||
                endMinute == startMinute)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endMinute),
                    "Schedule end minute must be between 0 and 1440 and differ from the start.");
            }

            if (!targetLocationId.IsValid)
            {
                throw new ArgumentException(
                    "A schedule entry requires a valid target location.",
                    nameof(targetLocationId));
            }

            if (!Enum.IsDefined(typeof(ResidentActivityKind), activity))
            {
                throw new ArgumentOutOfRangeException(nameof(activity));
            }

            StartMinute = startMinute;
            EndMinute = endMinute;
            TargetLocationId = targetLocationId;
            Activity = activity;
        }

        public int StartMinute { get; }

        public int EndMinute { get; }

        public TownLocationId TargetLocationId { get; }

        public ResidentActivityKind Activity { get; }

        public bool ContainsMinute(int minuteOfDay)
        {
            if (minuteOfDay < 0 || minuteOfDay >= DailyScheduleDefinition.MinutesPerDay)
            {
                return false;
            }

            if (StartMinute < EndMinute)
            {
                return minuteOfDay >= StartMinute && minuteOfDay < EndMinute;
            }

            return minuteOfDay >= StartMinute || minuteOfDay < EndMinute;
        }

        private static int ParseRequiredTime(string value, string parameterName)
        {
            if (!DailyScheduleDefinition.TryParseTime(value, out int minute))
            {
                throw new ArgumentException(
                    "Schedule time must use 24-hour HH:mm format.",
                    parameterName);
            }

            return minute;
        }
    }

    public sealed class DailyScheduleDefinition
    {
        public const int MinutesPerDay = 24 * 60;

        private readonly ReadOnlyCollection<DailyScheduleEntry> entries;

        public DailyScheduleDefinition(
            ResidentId residentId,
            IEnumerable<DailyScheduleEntry> scheduleEntries)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "A daily schedule requires a valid ResidentId.",
                    nameof(residentId));
            }

            if (scheduleEntries == null)
            {
                throw new ArgumentNullException(nameof(scheduleEntries));
            }

            var validated = new List<DailyScheduleEntry>(scheduleEntries);
            if (validated.Count == 0)
            {
                throw new ArgumentException(
                    "A daily schedule requires at least one entry.",
                    nameof(scheduleEntries));
            }

            bool[] occupiedMinutes = new bool[MinutesPerDay];
            foreach (DailyScheduleEntry entry in validated)
            {
                if (entry == null)
                {
                    throw new ArgumentException(
                        "Schedule entries cannot contain null values.",
                        nameof(scheduleEntries));
                }

                for (int minute = 0; minute < MinutesPerDay; minute++)
                {
                    if (!entry.ContainsMinute(minute))
                    {
                        continue;
                    }

                    if (occupiedMinutes[minute])
                    {
                        throw new ArgumentException(
                            $"Schedule entries overlap at {FormatTime(minute)}.",
                            nameof(scheduleEntries));
                    }

                    occupiedMinutes[minute] = true;
                }
            }

            validated.Sort((left, right) => left.StartMinute.CompareTo(right.StartMinute));
            ResidentId = residentId;
            entries = new ReadOnlyCollection<DailyScheduleEntry>(validated);
        }

        public ResidentId ResidentId { get; }

        public IReadOnlyList<DailyScheduleEntry> Entries => entries;

        public bool TryResolve(int minuteOfDay, out DailyScheduleEntry entry)
        {
            entry = null;
            if (minuteOfDay < 0 || minuteOfDay >= MinutesPerDay)
            {
                return false;
            }

            foreach (DailyScheduleEntry candidate in entries)
            {
                if (candidate.ContainsMinute(minuteOfDay))
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TryParseTime(string value, out int minuteOfDay)
        {
            minuteOfDay = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length != 5 || trimmed[2] != ':')
            {
                return false;
            }

            if (!int.TryParse(
                    trimmed.Substring(0, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int hour) ||
                !int.TryParse(
                    trimmed.Substring(3, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int minute))
            {
                return false;
            }

            if (hour == 24 && minute == 0)
            {
                minuteOfDay = MinutesPerDay;
                return true;
            }

            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
            {
                return false;
            }

            minuteOfDay = hour * 60 + minute;
            return true;
        }

        public static string FormatTime(int minuteOfDay)
        {
            if (minuteOfDay < 0 || minuteOfDay > MinutesPerDay)
            {
                throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
            }

            if (minuteOfDay == MinutesPerDay)
            {
                return "24:00";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}",
                minuteOfDay / 60,
                minuteOfDay % 60);
        }
    }
}
