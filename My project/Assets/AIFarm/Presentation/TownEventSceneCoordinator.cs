using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed class TownEventSceneCoordinator : MonoBehaviour
    {
        private const string HarvestDinnerWelcome =
            "收获晚餐开始啦！大家一起尝尝今晚的胡萝卜汤吧。";
        private const string HarvestDinnerProposalLine =
            "胡萝卜已经收好啦，晚上18:00在广场一起做胡萝卜汤吧！";

        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private TownScheduleCoordinator scheduleCoordinator;

        [SerializeField]
        private TownSocialCoordinator socialCoordinator;

        [SerializeField]
        private TownResidentScheduleController[] residents =
            Array.Empty<TownResidentScheduleController>();

        [SerializeField]
        private LocationArrivalPoint[] attendancePoints =
            Array.Empty<LocationArrivalPoint>();

        private readonly Dictionary<ResidentId, TownResidentScheduleController>
            residentsById =
                new Dictionary<ResidentId, TownResidentScheduleController>();
        private readonly HashSet<string> requestedKnowledgeIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<ResidentId> movingParticipants =
            new HashSet<ResidentId>();
        private AiRequestCancellation proposalCancellation;
        private long proposalRevision;
        private TownEventSession presentedSession;
        private bool gatheringMovementStarted;
        private bool activePresentationStarted;

        public TownEventCoordinator EventCoordinator { get; private set; }

        public bool IsInitialized { get; private set; }

        public ActionResult LastTownEventResult { get; private set; } =
            ActionResult.Success("Town event has not started.");

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            TownScheduleCoordinator townScheduleCoordinator,
            TownSocialCoordinator townSocialCoordinator,
            TownResidentScheduleController[] residentControllers,
            LocationArrivalPoint[] harvestDinnerAttendancePoints)
        {
            if (gameBootstrap == null || townScheduleCoordinator == null ||
                townSocialCoordinator == null || residentControllers == null ||
                harvestDinnerAttendancePoints == null ||
                residentControllers.Length != ResidentIds.TownResidents.Count ||
                harvestDinnerAttendancePoints.Length != ResidentIds.TownResidents.Count)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "HarvestDinner scene coordination requires four residents and four plaza points.");
            }

            bootstrap = gameBootstrap;
            scheduleCoordinator = townScheduleCoordinator;
            socialCoordinator = townSocialCoordinator;
            residents = (TownResidentScheduleController[])residentControllers.Clone();
            attendancePoints = (LocationArrivalPoint[])harvestDinnerAttendancePoints.Clone();
            Array.Sort(residents, (left, right) =>
                CompareResidents(left, right));
            Array.Sort(attendancePoints, (left, right) =>
                string.Compare(
                    left == null ? string.Empty : left.InteractionPointId,
                    right == null ? string.Empty : right.InteractionPointId,
                    StringComparison.Ordinal));
            return ActionResult.Success("HarvestDinner scene references configured.");
        }

        public ActionResult Initialize()
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner scene coordination is already initialized.");
            }

            if (bootstrap == null || !bootstrap.IsInitialized ||
                scheduleCoordinator == null || !scheduleCoordinator.IsInitialized ||
                socialCoordinator == null || !socialCoordinator.IsInitialized ||
                residents == null || attendancePoints == null ||
                residents.Length != ResidentIds.TownResidents.Count ||
                attendancePoints.Length != ResidentIds.TownResidents.Count)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner requires initialized bootstrap, schedule, social, and scene references.");
            }

            residentsById.Clear();
            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            var pointByResident = new Dictionary<ResidentId, string>();
            for (int index = 0; index < ResidentIds.TownResidents.Count; index++)
            {
                TownResidentScheduleController resident = residents[index];
                LocationArrivalPoint point = attendancePoints[index];
                ResidentId expectedResidentId = ResidentIds.TownResidents[index];
                if (resident == null || !resident.IsInitialized ||
                    resident.ResidentId != expectedResidentId ||
                    point == null ||
                    point.LocationId != TownEventCoordinator.HarvestDinnerLocationId ||
                    !point.InteractionPointId.StartsWith(
                        "location-plaza-point-",
                        StringComparison.Ordinal) ||
                    !pointIds.Add(point.InteractionPointId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "HarvestDinner requires the four fixed residents and four distinct ordinary plaza points.");
                }

                residentsById.Add(resident.ResidentId, resident);
                pointByResident.Add(resident.ResidentId, point.InteractionPointId);
            }

            EventCoordinator = new TownEventCoordinator(
                bootstrap.ResidentRegistry,
                scheduleCoordinator.ReservationService,
                bootstrap.Events,
                pointByResident);
            socialCoordinator.ConversationCompleted += HandleConversationCompleted;
            bootstrap.AuthoritativeStateResetting += HandleAuthoritativeStateResetting;
            IsInitialized = true;
            return ActionResult.Success(
                "HarvestDinner coordinator initialized with deterministic Unity authority.");
        }

        public ActionResult DecideInvitation(
            ResidentId residentId,
            bool willAttend)
        {
            if (!IsInitialized || EventCoordinator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner invitations require initialized coordination.");
            }

            return EventCoordinator.DecideInvitation(
                residentId,
                willAttend
                    ? TownEventInvitationStatus.Accepted
                    : TownEventInvitationStatus.Declined);
        }

        public ActionResult TrySubmitProposal(
            TownEventProposal proposal,
            out TownEventSession session)
        {
            session = null;
            if (!IsInitialized || EventCoordinator == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner proposals require initialized coordination.");
            }

            ActionResult scheduled = EventCoordinator.TrySchedule(proposal, out session);
            if (scheduled.Failed)
            {
                return scheduled;
            }

            foreach (TownEventInvitation invitation in session.Invitations)
            {
                if (invitation.Status == TownEventInvitationStatus.Pending)
                {
                    // V1 uses a bounded local rule: the resident who supplied the
                    // conversation fact joins the proposer; uninvolved invitees decline.
                    // Availability is checked again at 18:00 before suspension.
                    TownEventInvitationStatus localDecision =
                        invitation.ResidentId == session.ConversationSourceResidentId
                            ? TownEventInvitationStatus.Accepted
                            : TownEventInvitationStatus.Declined;
                    ActionResult decided = EventCoordinator.DecideInvitation(
                        invitation.ResidentId,
                        localDecision);
                    if (decided.Failed)
                    {
                        EventCoordinator.Cancel(
                            proposal.ProposedAtGameSeconds,
                            decided.Message);
                        return decided;
                    }
                }
            }

            presentedSession = session;
            gatheringMovementStarted = false;
            activePresentationStarted = false;
            if (residentsById.TryGetValue(
                    proposal.ProposerResidentId,
                    out TownResidentScheduleController proposer))
            {
                proposer.ShowTownEventProposal(HarvestDinnerProposalLine);
                StartCoroutine(ClearProposalLineAfterDelay(proposer));
            }

            return scheduled;
        }

        private static IEnumerator ClearProposalLineAfterDelay(
            TownResidentScheduleController proposer)
        {
            yield return new WaitForSecondsRealtime(3f);
            proposer?.ClearConversationLine();
        }

        public ActionResult TickTownEvent(
            float deltaTime,
            double monotonicSeconds,
            double gameSeconds)
        {
            if (!IsInitialized || EventCoordinator == null ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f ||
                !IsFiniteNonNegative(monotonicSeconds) ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Town-event ticks require initialized coordination and valid clocks.");
            }

            TownEventSession session = EventCoordinator.CurrentSession;
            if (session == null)
            {
                return ActionResult.Success("No HarvestDinner is scheduled.");
            }

            if (presentedSession != session)
            {
                presentedSession = session;
                gatheringMovementStarted = false;
                activePresentationStarted = false;
                movingParticipants.Clear();
            }

            if (session.State == TownEventState.Scheduled &&
                gameSeconds >= session.ScheduledStartGameSeconds)
            {
                ActionResult gathering = BeginGathering(gameSeconds, monotonicSeconds);
                if (gathering.Failed && !session.IsTerminal)
                {
                    return gathering;
                }
            }

            session = EventCoordinator.CurrentSession;
            if (session != null &&
                (session.State == TownEventState.Gathering ||
                    session.State == TownEventState.Active))
            {
                ActionResult available = ValidateActiveParticipants(session, gameSeconds);
                if (available.Failed)
                {
                    return available;
                }

                // Domain deadlines and leases advance before movement so an arrival
                // at or after the deadline cannot transition the event to Active.
                ActionResult domainTick = EventCoordinator.Tick(
                    gameSeconds,
                    monotonicSeconds);
                if (domainTick.Failed)
                {
                    if (session.IsTerminal)
                    {
                        CleanupResidentPresentation();
                    }

                    return domainTick;
                }
            }

            session = EventCoordinator.CurrentSession;
            if (session != null && session.State == TownEventState.Gathering)
            {
                ActionResult movement = TickGathering(deltaTime, gameSeconds, monotonicSeconds);
                if (movement.Failed && !session.IsTerminal)
                {
                    return movement;
                }
            }

            session = EventCoordinator.CurrentSession;
            if (session != null && session.State == TownEventState.Active &&
                !activePresentationStarted)
            {
                BeginActivePresentation(session);
            }

            if (session != null && session.IsTerminal)
            {
                CleanupResidentPresentation();
            }

            return ActionResult.Success("HarvestDinner scene state advanced deterministically.");
        }

        private ActionResult ValidateActiveParticipants(
            TownEventSession session,
            double gameSeconds)
        {
            foreach (ResidentId residentId in session.ParticipantResidentIds)
            {
                if (!residentsById.TryGetValue(
                        residentId,
                        out TownResidentScheduleController resident) ||
                    resident == null || !resident.isActiveAndEnabled ||
                    !resident.IsTownEventSuspended || resident.IsFarmingBusy ||
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Failed ||
                    runtime.CurrentGoal != null)
                {
                    ActionResult cancelled = EventCoordinator.Cancel(
                        gameSeconds,
                        $"HarvestDinner cancelled because participant {residentId} became unavailable.");
                    CleanupResidentPresentation();
                    return cancelled;
                }
            }

            return ActionResult.Success(
                "Every HarvestDinner participant remains active and event-suspended.");
        }

        private ActionResult BeginGathering(
            double gameSeconds,
            double monotonicSeconds)
        {
            var suspended = new List<TownResidentScheduleController>();
            foreach (ResidentId residentId in EventCoordinator.GetAcceptedInvitees())
            {
                if (!residentsById.TryGetValue(
                        residentId,
                        out TownResidentScheduleController resident) ||
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Failed ||
                    runtime.CurrentGoal != null || !resident.CanAttendTownEvent())
                {
                    EventCoordinator.MarkUnavailable(residentId);
                    continue;
                }

                ActionResult suspendedResult = resident.SuspendForTownEvent();
                if (suspendedResult.Failed)
                {
                    EventCoordinator.MarkUnavailable(residentId);
                    continue;
                }

                suspended.Add(resident);
            }

            ActionResult gathering = EventCoordinator.TryBeginGathering(
                gameSeconds,
                monotonicSeconds);
            if (gathering.Failed)
            {
                foreach (TownResidentScheduleController resident in suspended)
                {
                    resident.ResumeAfterTownEvent();
                }

                return gathering;
            }

            movingParticipants.Clear();
            foreach (ResidentId residentId in
                EventCoordinator.CurrentSession.ParticipantResidentIds)
            {
                TownResidentScheduleController resident = residentsById[residentId];
                ActionResult moving = resident.BeginTownEventMove(
                    EventCoordinator.GetAttendancePointId(residentId));
                if (moving.Failed)
                {
                    EventCoordinator.Cancel(gameSeconds, moving.Message);
                    CleanupResidentPresentation();
                    return moving;
                }

                movingParticipants.Add(residentId);
            }

            gatheringMovementStarted = true;
            return gathering;
        }

        private ActionResult TickGathering(
            float deltaTime,
            double gameSeconds,
            double monotonicSeconds)
        {
            if (!gatheringMovementStarted)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner gathering has no active movement plan.");
            }

            var activeMovers = new List<ResidentId>(movingParticipants);
            foreach (ResidentId residentId in activeMovers)
            {
                ActionResult moved = residentsById[residentId].TickTownEventMove(
                    deltaTime,
                    out bool arrived);
                if (moved.Failed)
                {
                    EventCoordinator.Cancel(gameSeconds, moved.Message);
                    CleanupResidentPresentation();
                    return moved;
                }

                if (!arrived)
                {
                    continue;
                }

                movingParticipants.Remove(residentId);
                ActionResult recorded = EventCoordinator.MarkArrived(
                    residentId,
                    gameSeconds,
                    monotonicSeconds);
                if (recorded.Failed)
                {
                    EventCoordinator.Cancel(gameSeconds, recorded.Message);
                    CleanupResidentPresentation();
                    return recorded;
                }
            }

            return ActionResult.Success("HarvestDinner participants are moving to the plaza.");
        }

        private void BeginActivePresentation(TownEventSession session)
        {
            activePresentationStarted = true;
            foreach (ResidentId residentId in session.ParticipantResidentIds)
            {
                residentsById[residentId].SetTownEventActive();
            }

            if (residentsById.TryGetValue(
                    session.Proposal.ProposerResidentId,
                    out TownResidentScheduleController proposer))
            {
                proposer.ShowTownEventWelcome(HarvestDinnerWelcome);
            }
        }

        private void CleanupResidentPresentation()
        {
            foreach (TownResidentScheduleController resident in residents)
            {
                resident?.ResumeAfterTownEvent();
            }

            movingParticipants.Clear();
            gatheringMovementStarted = false;
            activePresentationStarted = false;
        }

        private void HandleConversationCompleted(ConversationSession session)
        {
            if (!IsInitialized || session == null ||
                session.State != ConversationState.Completed ||
                EventCoordinator.ActiveTownEventCount != 0 ||
                proposalCancellation != null)
            {
                return;
            }

            if (!TryFindHarvestDinnerKnowledge(
                    session,
                    out ResidentId proposerResidentId,
                    out ResidentId sourceResidentId,
                    out MemoryEntry knowledge) ||
                !requestedKnowledgeIds.Add(knowledge.KnowledgeId))
            {
                return;
            }

            proposalCancellation = new AiRequestCancellation();
            long requestRevision = ++proposalRevision;
            StartCoroutine(RequestTownEventDecision(
                session.ConversationId.Value,
                proposerResidentId,
                sourceResidentId,
                knowledge.KnowledgeId,
                requestRevision,
                proposalCancellation));
        }

        private IEnumerator RequestTownEventDecision(
            string sourceConversationId,
            ResidentId proposerResidentId,
            ResidentId sourceResidentId,
            string sourceKnowledgeId,
            long requestRevision,
            AiRequestCancellation cancellation)
        {
            // Never begin a remote request from the Update() stack that completed a conversation.
            yield return null;
            if (!IsCurrentProposalRequest(requestRevision, cancellation))
            {
                yield break;
            }

            ActionResult contextBuilt = socialCoordinator.TryBuildResidentContext(
                proposerResidentId,
                sourceResidentId,
                out ResidentContext context);
            ResidentDecisionRequest request = null;
            if (contextBuilt.Succeeded)
            {
                try
                {
                    request = new ResidentDecisionRequest(
                        proposerResidentId,
                        context,
                        "一段实际完成的双人对话刚传递了胡萝卜收获消息；判断是否提出全镇活动。",
                        new[]
                        {
                            ResidentHighLevelIntents.ProposeTownEvent,
                            ResidentHighLevelIntents.Idle
                        },
                        Array.Empty<ResidentId>());
                }
                catch (ArgumentException exception)
                {
                    contextBuilt = ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        exception.Message);
                }
            }

            AiGatewayResult<ResidentDecisionSpec> result = null;
            if (contextBuilt.Succeeded && request != null)
            {
                yield return bootstrap.AiRequests.DecideResident(
                    request,
                    AiRequestPriority.High,
                    cancellation,
                    value => result = value);
            }

            if (!IsCurrentProposalRequest(requestRevision, cancellation))
            {
                yield break;
            }

            proposalCancellation = null;
            if (result == null || result.Failed ||
                result.ResidentId != proposerResidentId ||
                result.Value == null ||
                !string.Equals(
                    result.Value.Intent,
                    ResidentHighLevelIntents.ProposeTownEvent,
                    StringComparison.Ordinal) ||
                EventCoordinator.ActiveTownEventCount != 0 ||
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    proposerResidentId,
                    out ResidentRuntimeState runtime).Failed ||
                runtime.Memories.TryGetKnowledge(
                    proposerResidentId,
                    sourceKnowledgeId,
                    out MemoryEntry currentKnowledge).Failed ||
                currentKnowledge == null ||
                currentKnowledge.SourceKind != MemorySourceKind.Conversation ||
                !string.Equals(
                    currentKnowledge.SourceEventId,
                    sourceConversationId,
                    StringComparison.Ordinal))
            {
                yield break;
            }

            ActionResult converted = TownEventProposal.TryCreateFromDecision(
                result.Value,
                sourceKnowledgeId,
                sourceConversationId,
                bootstrap.Clock.ElapsedGameSeconds,
                out TownEventProposal proposal);
            if (converted.Failed)
            {
                LastTownEventResult = converted;
                yield break;
            }

            LastTownEventResult = TrySubmitProposal(proposal, out TownEventSession _);
        }

        private bool TryFindHarvestDinnerKnowledge(
            ConversationSession session,
            out ResidentId proposerResidentId,
            out ResidentId sourceResidentId,
            out MemoryEntry knowledge)
        {
            proposerResidentId = default;
            sourceResidentId = default;
            knowledge = null;
            var participants = new[]
            {
                session.FirstResidentId,
                session.SecondResidentId
            };
            Array.Sort(participants);
            foreach (ResidentId participant in participants)
            {
                if (bootstrap.ResidentRegistry.TryGetRuntimeState(
                        participant,
                        out ResidentRuntimeState runtime).Failed)
                {
                    continue;
                }

                for (int index = runtime.Memories.Entries.Count - 1; index >= 0; index--)
                {
                    MemoryEntry candidate = runtime.Memories.Entries[index];
                    if (candidate != null && candidate.OwnerResidentId == participant &&
                        candidate.SourceKind == MemorySourceKind.Conversation &&
                        candidate.ImmediateSourceResidentId.IsValid &&
                        candidate.ImmediateSourceResidentId != participant &&
                        candidate.IsShareable &&
                        string.Equals(
                            candidate.SourceEventId,
                            session.ConversationId.Value,
                            StringComparison.Ordinal) &&
                        HasTag(candidate, "harvest") && HasTag(candidate, "carrot"))
                    {
                        proposerResidentId = participant;
                        sourceResidentId = candidate.ImmediateSourceResidentId;
                        knowledge = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsCurrentProposalRequest(
            long requestRevision,
            AiRequestCancellation cancellation)
        {
            return IsInitialized && requestRevision == proposalRevision &&
                ReferenceEquals(proposalCancellation, cancellation) &&
                cancellation != null && !cancellation.IsCancellationRequested;
        }

        private void HandleAuthoritativeStateResetting()
        {
            proposalRevision++;
            proposalCancellation?.Cancel();
            proposalCancellation = null;
            TownEventSession activeSession = EventCoordinator?.CurrentSession;
            if (activeSession != null && !activeSession.IsTerminal)
            {
                double gameSeconds = bootstrap?.Clock?.ElapsedGameSeconds ?? 0d;
                LastTownEventResult = EventCoordinator.Cancel(
                    gameSeconds,
                    "HarvestDinner was interrupted by an authoritative state reset.");
            }

            EventCoordinator?.Reset();
            requestedKnowledgeIds.Clear();
            CleanupResidentPresentation();
            presentedSession = null;
        }

        private void Start()
        {
            ActionResult result = Initialize();
            if (result.Failed)
            {
                Debug.LogError($"HarvestDinner system did not start: {result.Message}", this);
            }
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                return;
            }

            // OnDisable resets the transient event and removes subscriptions. A
            // component toggle must restore those subscriptions without relying on
            // Start(), which Unity invokes only once.
            if (socialCoordinator != null)
            {
                socialCoordinator.ConversationCompleted -= HandleConversationCompleted;
                socialCoordinator.ConversationCompleted += HandleConversationCompleted;
            }

            if (bootstrap != null)
            {
                bootstrap.AuthoritativeStateResetting -= HandleAuthoritativeStateResetting;
                bootstrap.AuthoritativeStateResetting += HandleAuthoritativeStateResetting;
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            LastTownEventResult = TickTownEvent(
                UnityEngine.Time.deltaTime,
                UnityEngine.Time.realtimeSinceStartupAsDouble,
                bootstrap.Clock.ElapsedGameSeconds);
            if (LastTownEventResult.Failed &&
                EventCoordinator.CurrentSession != null &&
                !EventCoordinator.CurrentSession.IsTerminal)
            {
                Debug.LogWarning(
                    $"HarvestDinner flow recovered from: {LastTownEventResult.Message}",
                    this);
            }
        }

        private void OnDisable()
        {
            if (socialCoordinator != null)
            {
                socialCoordinator.ConversationCompleted -= HandleConversationCompleted;
            }

            if (bootstrap != null)
            {
                bootstrap.AuthoritativeStateResetting -= HandleAuthoritativeStateResetting;
            }

            proposalRevision++;
            proposalCancellation?.Cancel();
            proposalCancellation = null;
            // Let the cancelled proposal coroutine unwind through AiRequestCoordinator
            // so its shared slot and resident ownership are released deterministically.
            TownEventSession activeSession = EventCoordinator?.CurrentSession;
            if (activeSession != null && !activeSession.IsTerminal)
            {
                double gameSeconds = bootstrap?.Clock?.ElapsedGameSeconds ?? 0d;
                LastTownEventResult = EventCoordinator.Cancel(
                    gameSeconds,
                    "HarvestDinner cancelled because town-event scene coordination was disabled.");
            }

            EventCoordinator?.Reset();
            CleanupResidentPresentation();
            presentedSession = null;
        }

        private static bool HasTag(MemoryEntry memory, string expected)
        {
            foreach (string tag in memory.Tags)
            {
                if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareResidents(
            TownResidentScheduleController left,
            TownResidentScheduleController right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            return left.ResidentId.CompareTo(right.ResidentId);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
