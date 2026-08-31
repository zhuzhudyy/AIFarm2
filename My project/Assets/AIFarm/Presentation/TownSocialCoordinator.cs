using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class TownSocialCoordinator : MonoBehaviour
    {
        private sealed class SceneConversation
        {
            public SceneConversation(
                ConversationSession session,
                TownResidentScheduleController firstResident,
                TownResidentScheduleController secondResident,
                double nextLineAtSeconds)
            {
                Session = session;
                FirstResident = firstResident;
                SecondResident = secondResident;
                NextLineAtSeconds = nextLineAtSeconds;
            }

            public ConversationSession Session { get; }

            public TownResidentScheduleController FirstResident { get; }

            public TownResidentScheduleController SecondResident { get; }

            public bool FirstArrived { get; set; }

            public bool SecondArrived { get; set; }

            public double NextLineAtSeconds { get; set; }
        }

        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private TownScheduleCoordinator scheduleCoordinator;

        [SerializeField]
        private TownResidentScheduleController[] residents =
            Array.Empty<TownResidentScheduleController>();

        [SerializeField]
        private ConversationAnchor[] conversationAnchors =
            Array.Empty<ConversationAnchor>();

        [Range(ConversationSession.MinimumSentenceCount, ConversationSession.MaximumSentenceCount)]
        [SerializeField]
        private int sentenceCount = 4;

        [Range(1f, (float)ConversationSession.MaximumTimeoutSeconds)]
        [SerializeField]
        private float timeoutSeconds = 20f;

        [Min(0.1f)]
        [SerializeField]
        private float opportunityCheckIntervalSeconds = 1f;

        [Min(0.1f)]
        [SerializeField]
        private float lineIntervalSeconds = 0.8f;

        private readonly Dictionary<ResidentId, TownResidentScheduleController>
            residentsById =
                new Dictionary<ResidentId, TownResidentScheduleController>();
        private readonly Dictionary<ConversationId, SceneConversation> sceneConversations =
            new Dictionary<ConversationId, SceneConversation>();
        private double nextOpportunityCheckAtSeconds;

        public SocialGraph SocialGraph { get; private set; }

        public SocialOpportunityDetector OpportunityDetector { get; private set; }

        public ConversationParticipantLock ParticipantLock { get; private set; }

        public ConversationCoordinator ConversationCoordinator { get; private set; }

        public bool IsInitialized { get; private set; }

        public int ActiveSceneConversationCount => sceneConversations.Count;

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            TownScheduleCoordinator townScheduleCoordinator,
            TownResidentScheduleController[] residentControllers,
            ConversationAnchor[] anchors)
        {
            if (gameBootstrap == null || townScheduleCoordinator == null ||
                residentControllers == null || residentControllers.Length < 2 ||
                anchors == null || anchors.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Offline town social coordination requires bootstrap, scheduling, residents, and anchors.");
            }

            bootstrap = gameBootstrap;
            scheduleCoordinator = townScheduleCoordinator;
            residents = (TownResidentScheduleController[])residentControllers.Clone();
            conversationAnchors = (ConversationAnchor[])anchors.Clone();
            Array.Sort(residents, (left, right) => left.ResidentId.CompareTo(right.ResidentId));
            Array.Sort(
                conversationAnchors,
                (left, right) => string.Compare(
                    left == null ? string.Empty : left.AnchorId,
                    right == null ? string.Empty : right.AnchorId,
                    StringComparison.Ordinal));
            return ActionResult.Success("Offline town social scene references configured.");
        }

        public ActionResult Initialize()
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town social coordination is already initialized.");
            }

            if (bootstrap == null || !bootstrap.IsInitialized ||
                scheduleCoordinator == null || !scheduleCoordinator.IsInitialized ||
                residents == null || residents.Length < 2 ||
                conversationAnchors == null || conversationAnchors.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town social coordination requires initialized scheduling and scene references.");
            }

            residentsById.Clear();
            foreach (TownResidentScheduleController resident in residents)
            {
                if (resident == null || !resident.ResidentId.IsValid ||
                    !resident.IsInitialized || residentsById.ContainsKey(resident.ResidentId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Social residents must be initialized and have unique stable IDs.");
                }

                residentsById.Add(resident.ResidentId, resident);
            }

            var anchorIds = new HashSet<string>(StringComparer.Ordinal);
            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationAnchor anchor in conversationAnchors)
            {
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.AnchorId) ||
                    anchor.FirstStandPoint == null || anchor.SecondStandPoint == null ||
                    !anchorIds.Add(anchor.AnchorId) ||
                    !pointIds.Add(anchor.FirstStandPoint.InteractionPointId) ||
                    !pointIds.Add(anchor.SecondStandPoint.InteractionPointId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Conversation anchors and stand point IDs must be complete and unique.");
                }
            }

            SocialGraph = new SocialGraph(bootstrap.ResidentRegistry.ResidentIds);
            OpportunityDetector = new SocialOpportunityDetector();
            ParticipantLock = new ConversationParticipantLock();
            var templates = new LocalConversationTemplateService();
            var outcomeApplier = new ConversationOutcomeApplier(
                bootstrap.ResidentRegistry,
                SocialGraph);
            ConversationCoordinator = new ConversationCoordinator(
                bootstrap.ResidentRegistry,
                ParticipantLock,
                scheduleCoordinator.ReservationService,
                templates,
                outcomeApplier,
                timeoutSeconds);
            nextOpportunityCheckAtSeconds = UnityEngine.Time.realtimeSinceStartupAsDouble;
            IsInitialized = true;
            return ActionResult.Success("Completely offline two-resident social system initialized.");
        }

        public ActionResult TickSocial(
            float deltaTime,
            double monotonicSeconds,
            double gameSeconds)
        {
            if (!IsInitialized || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                deltaTime < 0f || !IsFiniteNonNegative(monotonicSeconds) ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Social ticks require initialization and valid clock values.");
            }

            ConversationCoordinator.TickTimeouts(monotonicSeconds);
            ActionResult activeResult = TickActiveConversations(
                deltaTime,
                monotonicSeconds,
                gameSeconds);
            if (activeResult.Failed)
            {
                return activeResult;
            }

            if (bootstrap.Clock.IsPaused ||
                monotonicSeconds < nextOpportunityCheckAtSeconds)
            {
                return ActionResult.Success();
            }

            nextOpportunityCheckAtSeconds =
                monotonicSeconds + opportunityCheckIntervalSeconds;
            return TryStartOpportunity(monotonicSeconds, gameSeconds);
        }

        private ActionResult TickActiveConversations(
            float deltaTime,
            double monotonicSeconds,
            double gameSeconds)
        {
            var records = new List<SceneConversation>(sceneConversations.Values);
            foreach (SceneConversation record in records)
            {
                if (record.Session.IsTerminal)
                {
                    CleanupSceneConversation(record);
                    continue;
                }

                ActionResult movement = TickMovement(record, deltaTime);
                if (movement.Failed)
                {
                    ConversationCoordinator.CancelConversation(
                        record.Session.ConversationId,
                        ConversationEndReason.NavigationFailed);
                    CleanupSceneConversation(record);
                    continue;
                }

                if (!record.FirstArrived || !record.SecondArrived ||
                    monotonicSeconds < record.NextLineAtSeconds)
                {
                    continue;
                }

                ActionResult advanced = ConversationCoordinator.AdvanceConversation(
                    record.Session.ConversationId,
                    monotonicSeconds,
                    gameSeconds,
                    out _);

                record.NextLineAtSeconds = monotonicSeconds + lineIntervalSeconds;
                if (record.Session.IsTerminal)
                {
                    CleanupSceneConversation(record);
                }

                if (advanced.Failed && !record.Session.IsTerminal)
                {
                    return advanced;
                }
            }

            return ActionResult.Success();
        }

        private ActionResult TickMovement(SceneConversation record, float deltaTime)
        {
            if (!record.FirstArrived)
            {
                ActionResult firstMove = record.FirstResident.TickConversationMove(
                    deltaTime,
                    out bool firstArrived);
                if (firstMove.Failed)
                {
                    return firstMove;
                }

                record.FirstArrived = firstArrived;
            }

            if (!record.SecondArrived)
            {
                ActionResult secondMove = record.SecondResident.TickConversationMove(
                    deltaTime,
                    out bool secondArrived);
                if (secondMove.Failed)
                {
                    return secondMove;
                }

                record.SecondArrived = secondArrived;
            }

            return ActionResult.Success();
        }

        private ActionResult TryStartOpportunity(
            double monotonicSeconds,
            double gameSeconds)
        {
            var statuses = new List<SocialResidentStatus>(residents.Length);
            foreach (TownResidentScheduleController resident in residents)
            {
                ResidentActivityKind? activity = resident.CurrentActivity;
                bool hasPlayerInstruction = true;
                if (bootstrap.ResidentRegistry.TryGetRuntimeState(
                        resident.ResidentId,
                        out ResidentRuntimeState runtime).Succeeded)
                {
                    hasPlayerInstruction = runtime.CurrentGoal != null;
                }

                bool idleAtSocialLocation = resident.Runtime.State ==
                        ResidentScheduleState.Working &&
                    activity == ResidentActivityKind.Gather &&
                    HasAnchorAt(resident.CurrentLocationId);
                statuses.Add(new SocialResidentStatus(
                    resident.ResidentId,
                    resident.CurrentLocationId,
                    idleAtSocialLocation,
                    hasActiveAction: resident.IsFarmingBusy,
                    isPerformingEmergencyFarmWork: resident.IsFarmingBusy,
                    hasPlayerInstruction: hasPlayerInstruction,
                    isGoingHomeToSleep: activity == ResidentActivityKind.Home));
            }

            ActionResult detected = OpportunityDetector.TryDetect(
                statuses,
                gameSeconds,
                ParticipantLock,
                out SocialOpportunity opportunity);
            if (detected.Failed)
            {
                return ActionResult.Success("No eligible offline social opportunity this tick.");
            }

            ConversationAnchor anchor = FindFreeAnchor(
                opportunity.LocationId,
                monotonicSeconds);
            if (anchor == null ||
                !residentsById.TryGetValue(
                    opportunity.FirstResidentId,
                    out TownResidentScheduleController firstResident) ||
                !residentsById.TryGetValue(
                    opportunity.SecondResidentId,
                    out TownResidentScheduleController secondResident))
            {
                return ActionResult.Success("No free paired conversation anchor is available.");
            }

            ActionResult firstSuspended = firstResident.SuspendForConversation();
            if (firstSuspended.Failed)
            {
                return firstSuspended;
            }

            ActionResult secondSuspended = secondResident.SuspendForConversation();
            if (secondSuspended.Failed)
            {
                firstResident.ResumeAfterConversation();
                return secondSuspended;
            }

            ActionResult started = ConversationCoordinator.TryStartConversation(
                opportunity.FirstResidentId,
                opportunity.SecondResidentId,
                anchor.FirstStandPoint.InteractionPointId,
                anchor.SecondStandPoint.InteractionPointId,
                sentenceCount,
                monotonicSeconds,
                out ConversationSession session);
            if (started.Failed)
            {
                secondResident.ResumeAfterConversation();
                firstResident.ResumeAfterConversation();
                return started;
            }

            ActionResult firstMove = firstResident.BeginConversationMove(
                anchor.FirstStandPoint.InteractionPointId);
            ActionResult secondMove = firstMove.Succeeded
                ? secondResident.BeginConversationMove(
                    anchor.SecondStandPoint.InteractionPointId)
                : firstMove;
            if (firstMove.Failed || secondMove.Failed)
            {
                ConversationCoordinator.CancelConversation(
                    session.ConversationId,
                    ConversationEndReason.NavigationFailed);
                secondResident.ResumeAfterConversation();
                firstResident.ResumeAfterConversation();
                return firstMove.Failed ? firstMove : secondMove;
            }

            OpportunityDetector.RecordConversationStarted(opportunity, gameSeconds);
            sceneConversations.Add(
                session.ConversationId,
                new SceneConversation(
                    session,
                    firstResident,
                    secondResident,
                    monotonicSeconds));
            return ActionResult.Success("Residents are moving to an offline conversation anchor.");
        }

        private bool HasAnchorAt(TownLocationId locationId)
        {
            if (!locationId.IsValid)
            {
                return false;
            }

            foreach (ConversationAnchor anchor in conversationAnchors)
            {
                if (anchor != null && anchor.LocationId == locationId)
                {
                    return true;
                }
            }

            return false;
        }

        private ConversationAnchor FindFreeAnchor(
            TownLocationId locationId,
            double monotonicSeconds)
        {
            foreach (ConversationAnchor anchor in conversationAnchors)
            {
                if (anchor == null || anchor.LocationId != locationId)
                {
                    continue;
                }

                if (!scheduleCoordinator.ReservationService.IsReserved(
                        anchor.FirstStandPoint.InteractionPointId,
                        monotonicSeconds) &&
                    !scheduleCoordinator.ReservationService.IsReserved(
                        anchor.SecondStandPoint.InteractionPointId,
                        monotonicSeconds))
                {
                    return anchor;
                }
            }

            return null;
        }

        private void CleanupSceneConversation(SceneConversation record)
        {
            sceneConversations.Remove(record.Session.ConversationId);
            record.SecondResident.ResumeAfterConversation();
            record.FirstResident.ResumeAfterConversation();
        }

        private void Awake()
        {
            ActionResult result = Initialize();
            if (result.Failed)
            {
                Debug.LogError($"Offline town social system did not start: {result.Message}", this);
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            ActionResult result = TickSocial(
                UnityEngine.Time.deltaTime,
                UnityEngine.Time.realtimeSinceStartupAsDouble,
                bootstrap.Clock.ElapsedGameSeconds);
            if (result.Failed)
            {
                Debug.LogWarning($"Offline social flow recovered from: {result.Message}", this);
            }
        }

        private void OnDisable()
        {
            if (ConversationCoordinator == null)
            {
                return;
            }

            var records = new List<SceneConversation>(sceneConversations.Values);
            foreach (SceneConversation record in records)
            {
                ConversationCoordinator.CancelConversation(
                    record.Session.ConversationId,
                    ConversationEndReason.Cancelled);
                CleanupSceneConversation(record);
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
