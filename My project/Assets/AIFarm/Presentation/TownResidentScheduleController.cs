using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
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
        private bool scheduleSuspendedForConversation;

        public ResidentId ResidentId => definitionAsset == null
            ? default
            : definitionAsset.ResidentId;

        public ResidentDefinitionAsset DefinitionAsset => definitionAsset;

        public ResidentScheduleRuntime Runtime { get; private set; }

        public bool IsInitialized => Runtime != null;

        public bool IsFarmingBusy => farmingExecutor != null && farmingExecutor.IsBusy;

        public bool IsConversationSuspended => scheduleSuspendedForConversation;

        public TownResidentNavigator Navigator => navigator;

        public ResidentActivityKind? CurrentActivity => Runtime?.ActiveEntry == null
            ? (ResidentActivityKind?)null
            : Runtime.ActiveEntry.Activity;

        public TownLocationId CurrentLocationId => Runtime == null
            ? default
            : Runtime.CurrentLocationId;

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

            if (scheduleSuspendedForConversation)
            {
                residentView.SetConversationState(true);
                return ActionResult.Success("Town schedule paused for an active conversation.");
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

        public ActionResult SuspendForConversation()
        {
            if (!IsInitialized || navigator == null || residentView == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident schedule must be initialized before conversation.");
            }

            if (scheduleSuspendedForConversation || scheduleSuspendedForFarming ||
                IsFarmingBusy || Runtime.ActiveEntry == null ||
                Runtime.ActiveEntry.Activity == ResidentActivityKind.Home)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident cannot leave the current priority activity for conversation.");
            }

            ActionResult suspended = Runtime.Suspend();
            if (suspended.Failed)
            {
                return suspended;
            }

            scheduleSuspendedForConversation = true;
            residentView.SetConversationState(true);
            return ActionResult.Success("Resident schedule suspended for conversation.");
        }

        public ActionResult BeginConversationMove(string interactionPointId)
        {
            if (!scheduleSuspendedForConversation || navigator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident must be conversation-suspended before moving to an anchor.");
            }

            return navigator.BeginMove(interactionPointId);
        }

        public ActionResult TickConversationMove(float deltaTime, out bool arrived)
        {
            arrived = false;
            if (!scheduleSuspendedForConversation || navigator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident has no active conversation movement.");
            }

            return navigator.Tick(deltaTime, out arrived);
        }

        public ActionResult ResumeAfterConversation()
        {
            if (!scheduleSuspendedForConversation)
            {
                return ActionResult.Success("Resident was not conversation-suspended.");
            }

            if (navigator != null)
            {
                navigator.CancelMove();
            }

            scheduleSuspendedForConversation = false;
            residentView.SetConversationState(false);
            return Runtime.Resume();
        }

        public ActionResult FaceConversationPartner(Transform partner)
        {
            if (!scheduleSuspendedForConversation || partner == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a conversation participant can face its partner.");
            }

            Vector3 direction = partner.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            return ActionResult.Success("Resident faced the other conversation participant.");
        }

        public void ShowConversationLine(ConversationUtterance utterance)
        {
            if (utterance != null && utterance.SpeakerResidentId == ResidentId)
            {
                residentView.ShowConversationLine(
                    utterance.Text,
                    utterance.Emoji,
                    utterance.Mood);
            }
        }

        public void ClearConversationLine()
        {
            residentView?.ClearConversationLine();
        }

        private void OnDisable()
        {
            if (Runtime != null)
            {
                Runtime.Suspend();
            }

            scheduleSuspendedForConversation = false;
        }
    }
}
