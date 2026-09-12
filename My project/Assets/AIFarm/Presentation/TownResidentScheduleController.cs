using System;
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
        private bool scheduleSuspendedForLife;
        private TownLifeController lifeController;
        private bool scheduleSuspendedForConversation;
        private bool scheduleSuspendedForTownEvent;
        private TownEventState townEventState = TownEventState.Scheduled;

        public ResidentId ResidentId => definitionAsset == null
            ? default
            : definitionAsset.ResidentId;

        public ResidentDefinitionAsset DefinitionAsset => definitionAsset;

        public ResidentScheduleRuntime Runtime { get; private set; }

        public bool IsInitialized => Runtime != null;

        public bool IsFarmingBusy => farmingExecutor != null && farmingExecutor.IsBusy;

        public bool IsLifeBusy => lifeController != null && lifeController.IsBusy;

        public bool IsConversationSuspended => scheduleSuspendedForConversation;

        public bool IsTownEventSuspended => scheduleSuspendedForTownEvent;

        public TownResidentNavigator Navigator => navigator;

        public void AttachLifeController(TownLifeController life, NpcPlanExecutor residentExecutor)
        {
            lifeController = life;
            farmingExecutor = residentExecutor;
        }

        public void SuspendForLifeAction()
        {
            if (scheduleSuspendedForLife) return;
            Runtime?.Suspend();
            scheduleSuspendedForLife = true;
        }

        public void ResumeAfterLifeAction()
        {
            if (!scheduleSuspendedForLife) return;
            scheduleSuspendedForLife = false;
            if (!scheduleSuspendedForConversation && !scheduleSuspendedForTownEvent) Runtime?.Resume();
        }

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

            if (scheduleSuspendedForTownEvent)
            {
                residentView.SetTownEventState(townEventState);
                return ActionResult.Success("Town schedule paused for HarvestDinner.");
            }

            if (scheduleSuspendedForConversation)
            {
                residentView.SetConversationState(true);
                return ActionResult.Success("Town schedule paused for an active conversation.");
            }

            if (scheduleSuspendedForLife)
            {
                return ActionResult.Success("Resident is executing a life activity.");
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

            if (scheduleSuspendedForConversation || scheduleSuspendedForTownEvent || scheduleSuspendedForLife ||
                (lifeController != null && !lifeController.IsAvailableForConversation) ||
                scheduleSuspendedForFarming ||
                IsFarmingBusy || Runtime.ActiveEntry == null ||
                (lifeController == null && Runtime.ActiveEntry.Activity == ResidentActivityKind.Home))
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

        public bool CanAttendTownEvent()
        {
            return IsInitialized && navigator != null && residentView != null &&
                !scheduleSuspendedForConversation && !scheduleSuspendedForTownEvent &&
                !scheduleSuspendedForFarming && !scheduleSuspendedForLife && !IsFarmingBusy &&
                (lifeController == null || !lifeController.HasPlayerTask) &&
                Runtime.ActiveEntry != null &&
                Runtime.ActiveEntry.Activity != ResidentActivityKind.Home;
        }

        public ActionResult SuspendForTownEvent()
        {
            if (!CanAttendTownEvent())
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident cannot leave the current priority activity for HarvestDinner.");
            }

            ActionResult suspended = Runtime.Suspend();
            if (suspended.Failed)
            {
                return suspended;
            }

            scheduleSuspendedForTownEvent = true;
            townEventState = TownEventState.Gathering;
            residentView.SetTownEventState(townEventState);
            return ActionResult.Success("Resident schedule suspended for HarvestDinner.");
        }

        public ActionResult BeginTownEventMove(string interactionPointId)
        {
            if (!scheduleSuspendedForTownEvent || navigator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident must be town-event-suspended before moving to the plaza.");
            }

            return navigator.BeginMove(interactionPointId);
        }

        public ActionResult TickTownEventMove(float deltaTime, out bool arrived)
        {
            arrived = false;
            if (!scheduleSuspendedForTownEvent || navigator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident has no active HarvestDinner movement.");
            }

            return navigator.Tick(deltaTime, out arrived);
        }

        public ActionResult SetTownEventActive()
        {
            if (!scheduleSuspendedForTownEvent)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a HarvestDinner participant can begin the activity performance.");
            }

            townEventState = TownEventState.Active;
            residentView.SetTownEventState(townEventState);
            return ActionResult.Success("Resident began the deterministic HarvestDinner performance.");
        }

        public void ShowTownEventWelcome(string text)
        {
            if (scheduleSuspendedForTownEvent)
            {
                residentView.ShowTownEventLine(text, "🍲", NpcMood.Happy);
            }
        }

        public void ShowTownEventProposal(string text)
        {
            if (!scheduleSuspendedForConversation && !scheduleSuspendedForTownEvent)
            {
                residentView.ShowConversationLine(text, "🍲", NpcMood.Happy);
            }
        }

        public ActionResult ResumeAfterTownEvent()
        {
            if (!scheduleSuspendedForTownEvent)
            {
                return ActionResult.Success("Resident was not HarvestDinner-suspended.");
            }

            navigator?.CancelMove();
            scheduleSuspendedForTownEvent = false;
            townEventState = TownEventState.Scheduled;
            residentView.SetTownEventState(TownEventState.Completed);
            return Runtime.Resume();
        }

        public ActionResult ResetForAuthoritativeStateChange()
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The resident schedule must be initialized before it can be reset.");
            }

            ActionResult scheduleReset = Runtime.ResetForAuthoritativeStateChange();
            ActionResult executorReset = ActionResult.Success();
            if (farmingExecutor != null && farmingExecutor.IsInitialized && farmingExecutor.IsBusy)
            {
                executorReset = farmingExecutor.RestorePendingActions(Array.Empty<INpcAction>());
            }

            scheduleSuspendedForFarming = false;
            scheduleSuspendedForLife = false;
            scheduleSuspendedForConversation = false;
            scheduleSuspendedForTownEvent = false;
            townEventState = TownEventState.Scheduled;
            residentView?.SetConversationState(false);
            residentView?.SetTownEventState(TownEventState.Scheduled);
            residentView?.SetScheduleState(ResidentScheduleState.WaitingForSchedule, null);
            residentView?.ClearConversationLine();

            if (scheduleReset.Failed)
            {
                return scheduleReset;
            }

            return executorReset.Failed
                ? executorReset
                : ActionResult.Success(
                    $"Resident '{ResidentId}' reset and ready for deterministic replanning.");
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
                // Release any schedule reservation or in-flight social/event move.
                // The runtime stays suspended while this component is disabled,
                // even though TownScheduleCoordinator still owns the reference.
                Runtime.Suspend();
            }

            scheduleSuspendedForConversation = false;
            scheduleSuspendedForTownEvent = false;
            townEventState = TownEventState.Scheduled;
        }

        private void OnEnable()
        {
            // Unity invokes Start only once; a transient component toggle must make
            // the deterministic schedule runnable again without restoring a stale
            // reservation or movement.
            Runtime?.Resume();
        }
    }
}
