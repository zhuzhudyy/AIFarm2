using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class TownResidentScheduleController : MonoBehaviour
    {
        [SerializeField]
        private ResidentDefinitionAsset definitionAsset;

        [SerializeField]
        private TownResidentNavigator navigator;

        [SerializeField]
        private ResidentBlockoutView residentView;

        [SerializeField]
        private NpcPlanExecutor farmingExecutor;

        private bool scheduleSuspendedForFarming;

        public ResidentId ResidentId => definitionAsset == null
            ? default
            : definitionAsset.ResidentId;

        public ResidentDefinitionAsset DefinitionAsset => definitionAsset;

        public ResidentScheduleRuntime Runtime { get; private set; }

        public bool IsInitialized => Runtime != null;

        public ActionResult Configure(
            ResidentDefinitionAsset residentDefinition,
            TownResidentNavigator locationNavigator,
            ResidentBlockoutView view,
            NpcPlanExecutor optionalFarmingExecutor = null)
        {
            if (residentDefinition == null || !residentDefinition.ResidentId.IsValid ||
                locationNavigator == null || view == null ||
                view.ResidentId != residentDefinition.ResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Schedule controller requires matching resident, navigator, and view references.");
            }

            definitionAsset = residentDefinition;
            navigator = locationNavigator;
            residentView = view;
            farmingExecutor = optionalFarmingExecutor;
            return ActionResult.Success($"Schedule controller configured for {ResidentId}.");
        }

        public ActionResult Initialize(
            TownScheduler scheduler,
            InteractionPointReservationService reservations,
            TownLocationPointDirectory pointDirectory,
            float arrivalTimeoutSeconds = 8f,
            float retryDelaySeconds = 1f)
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident schedule controller is already initialized.");
            }

            if (definitionAsset == null || !definitionAsset.ResidentId.IsValid ||
                navigator == null || residentView == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident schedule controller is not fully configured.");
            }

            Runtime = new ResidentScheduleRuntime(
                definitionAsset.ResidentId,
                scheduler,
                reservations,
                pointDirectory,
                navigator,
                arrivalTimeoutSeconds,
                retryDelaySeconds,
                reservationLeaseSeconds: arrivalTimeoutSeconds + 4d);
            return ActionResult.Success($"Schedule runtime initialized for {ResidentId}.");
        }

        public ActionResult TickSchedule(
            int minuteOfDay,
            double elapsedSeconds,
            float deltaTime)
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Resident schedule controller is not initialized.");
            }

            if (farmingExecutor != null && farmingExecutor.IsBusy)
            {
                if (!scheduleSuspendedForFarming)
                {
                    Runtime.Suspend();
                    scheduleSuspendedForFarming = true;
                    residentView.SetScheduleState(ResidentScheduleState.Suspended, null);
                }

                return ActionResult.Success("Town schedule paused for the resident's farm action.");
            }

            if (scheduleSuspendedForFarming)
            {
                Runtime.Resume();
                scheduleSuspendedForFarming = false;
            }

            ActionResult result = Runtime.Tick(minuteOfDay, elapsedSeconds, deltaTime);
            residentView.SetScheduleState(
                Runtime.State,
                Runtime.ActiveEntry == null
                    ? (ResidentActivityKind?)null
                    : Runtime.ActiveEntry.Activity);
            return result;
        }

        private void OnDisable()
        {
            if (Runtime != null)
            {
                Runtime.Suspend();
            }
        }
    }
}
