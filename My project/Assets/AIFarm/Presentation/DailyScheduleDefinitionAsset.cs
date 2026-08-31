using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [Serializable]
    public sealed class DailyScheduleSlotAsset
    {
        [SerializeField]
        private string startTime = "08:00";

        [SerializeField]
        private string endTime = "12:00";

        [SerializeField]
        private TownLocationDefinitionAsset targetLocation;

        [SerializeField]
        private ResidentActivityKind activity = ResidentActivityKind.Work;

        public string StartTime => startTime ?? string.Empty;

        public string EndTime => endTime ?? string.Empty;

        public TownLocationDefinitionAsset TargetLocation => targetLocation;

        public ResidentActivityKind Activity => activity;

        public DailyScheduleSlotAsset()
        {
        }

        public DailyScheduleSlotAsset(
            string start,
            string end,
            TownLocationDefinitionAsset location,
            ResidentActivityKind activityKind)
        {
            startTime = start;
            endTime = end;
            targetLocation = location;
            activity = activityKind;
        }
    }

    [CreateAssetMenu(
        fileName = "DailyScheduleDefinition",
        menuName = "AIFarm/Town/Daily Schedule Definition")]
    public sealed class DailyScheduleDefinitionAsset : ScriptableObject
    {
        [SerializeField]
        private string residentIdValue = ResidentIds.YayaValue;

        [SerializeField]
        private DailyScheduleSlotAsset[] slots = Array.Empty<DailyScheduleSlotAsset>();

        public ResidentId ResidentId => AIFarm.Npc.ResidentId.TryCreate(
            residentIdValue,
            out ResidentId parsed)
            ? parsed
            : default;

        public IReadOnlyList<DailyScheduleSlotAsset> Slots => slots;

        public ActionResult Configure(
            string stableResidentId,
            DailyScheduleSlotAsset[] scheduleSlots)
        {
            if (!AIFarm.Npc.ResidentId.TryCreate(stableResidentId, out ResidentId residentId) ||
                scheduleSlots == null || scheduleSlots.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Schedule asset requires a resident ID and at least one slot.");
            }

            DailyScheduleSlotAsset[] candidateSlots =
                (DailyScheduleSlotAsset[])scheduleSlots.Clone();
            ActionResult validated = TryBuildDefinition(
                residentId,
                candidateSlots,
                out _);
            if (validated.Failed)
            {
                return validated;
            }

            residentIdValue = residentId.Value;
            slots = candidateSlots;
            return ActionResult.Success($"Schedule asset configured for {residentId}.");
        }

        public ActionResult TryCreateDefinition(out DailyScheduleDefinition definition)
        {
            ResidentId residentId = ResidentId;
            if (!residentId.IsValid)
            {
                definition = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Schedule asset contains an invalid ResidentId.");
            }

            return TryBuildDefinition(residentId, slots, out definition);
        }

        private static ActionResult TryBuildDefinition(
            ResidentId residentId,
            IReadOnlyList<DailyScheduleSlotAsset> candidateSlots,
            out DailyScheduleDefinition definition)
        {
            definition = null;
            if (candidateSlots == null || candidateSlots.Count == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Schedule asset has no slots.");
            }

            var entries = new List<DailyScheduleEntry>(candidateSlots.Count);
            try
            {
                foreach (DailyScheduleSlotAsset slot in candidateSlots)
                {
                    if (slot == null || slot.TargetLocation == null ||
                        !slot.TargetLocation.LocationId.IsValid)
                    {
                        return ActionResult.Failure(
                            ActionFailureReason.InvalidResponse,
                            "Schedule asset contains an incomplete slot.");
                    }

                    entries.Add(new DailyScheduleEntry(
                        slot.StartTime,
                        slot.EndTime,
                        slot.TargetLocation.LocationId,
                        slot.Activity));
                }

                definition = new DailyScheduleDefinition(residentId, entries);
                return ActionResult.Success();
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is ArgumentOutOfRangeException)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Schedule asset is invalid: {exception.Message}");
            }
        }
    }
}
