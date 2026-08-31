using AIFarm.Core;
using AIFarm.Npc;
using UnityEngine;

namespace AIFarm.Presentation
{
    [CreateAssetMenu(
        fileName = "ResidentDefinition",
        menuName = "AIFarm/Town/Resident Definition")]
    public sealed class ResidentDefinitionAsset : ScriptableObject
    {
        [SerializeField]
        private string residentIdValue = ResidentIds.YayaValue;

        [SerializeField]
        private string displayName = "芽芽";

        [SerializeField]
        private Color residentColor = new Color(0.35f, 0.78f, 0.32f);

        [SerializeField]
        private string statusIcon = "Y";

        [SerializeField]
        private DailyScheduleDefinitionAsset dailySchedule;

        public ResidentId ResidentId => AIFarm.Npc.ResidentId.TryCreate(
            residentIdValue,
            out ResidentId parsed)
            ? parsed
            : default;

        public string ResidentIdValue => residentIdValue ?? string.Empty;

        public string DisplayName => displayName ?? string.Empty;

        public Color ResidentColor => residentColor;

        public string StatusIcon => string.IsNullOrWhiteSpace(statusIcon)
            ? "?"
            : statusIcon.Trim();

        public DailyScheduleDefinitionAsset DailySchedule => dailySchedule;

        public ActionResult Configure(
            string stableResidentId,
            string residentDisplayName,
            Color color,
            string residentStatusIcon,
            DailyScheduleDefinitionAsset schedule)
        {
            if (!AIFarm.Npc.ResidentId.TryCreate(stableResidentId, out ResidentId residentId) ||
                string.IsNullOrWhiteSpace(residentDisplayName) ||
                string.IsNullOrWhiteSpace(residentStatusIcon) ||
                schedule == null || schedule.ResidentId != residentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident asset requires a stable ID, name, icon, and matching schedule.");
            }

            residentIdValue = residentId.Value;
            displayName = residentDisplayName.Trim();
            residentColor = color;
            statusIcon = residentStatusIcon.Trim();
            dailySchedule = schedule;
            return ActionResult.Success($"Resident asset configured for {residentId}.");
        }

        public ActionResult TryCreateDefinition(out ResidentDefinition definition)
        {
            definition = null;
            ResidentId residentId = ResidentId;
            if (!residentId.IsValid || string.IsNullOrWhiteSpace(displayName))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Resident asset contains an invalid ID or display name.");
            }

            definition = new ResidentDefinition(
                residentId,
                displayName,
                NpcPersonaDefinition.ForResident(residentId));
            return ActionResult.Success();
        }
    }
}
