using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Town
{
    public enum TownEventType
    {
        HarvestDinner = 0
    }

    public enum TownEventState
    {
        Scheduled = 0,
        Gathering,
        Active,
        Completed,
        Cancelled
    }

    public enum TownEventInvitationStatus
    {
        Pending = 0,
        Accepted,
        Declined,
        Unavailable
    }

    public sealed class TownEventProposal
    {
        private TownEventProposal(
            ResidentId proposerResidentId,
            string sourceKnowledgeId,
            string sourceConversationId,
            double proposedAtGameSeconds,
            string reason)
        {
            ProposerResidentId = proposerResidentId;
            SourceKnowledgeId = sourceKnowledgeId;
            SourceConversationId = sourceConversationId;
            ProposedAtGameSeconds = proposedAtGameSeconds;
            Reason = reason;
        }

        public TownEventType EventType => TownEventType.HarvestDinner;

        public ResidentId ProposerResidentId { get; }

        public string SourceKnowledgeId { get; }

        public string SourceConversationId { get; }

        public double ProposedAtGameSeconds { get; }

        public string Reason { get; }

        public static ActionResult TryCreateFromDecision(
            ResidentDecisionSpec decision,
            string sourceKnowledgeId,
            string sourceConversationId,
            double proposedAtGameSeconds,
            out TownEventProposal proposal)
        {
            proposal = null;
            string normalizedKnowledgeId = (sourceKnowledgeId ?? string.Empty).Trim();
            string normalizedConversationId =
                (sourceConversationId ?? string.Empty).Trim();
            if (decision == null || !decision.ResidentId.IsValid ||
                !string.Equals(
                    decision.Intent,
                    ResidentHighLevelIntents.ProposeTownEvent,
                    StringComparison.Ordinal) ||
                decision.TargetResidentId.HasValue ||
                normalizedKnowledgeId.Length < 1 ||
                normalizedKnowledgeId.Length > MemoryEntry.MaximumIdentifierLength ||
                normalizedConversationId.Length < 1 ||
                normalizedConversationId.Length > MemoryEntry.MaximumIdentifierLength ||
                !IsFiniteNonNegative(proposedAtGameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Only a target-free propose_town_event decision with bounded provenance can create a proposal.");
            }

            proposal = new TownEventProposal(
                decision.ResidentId,
                normalizedKnowledgeId,
                normalizedConversationId,
                proposedAtGameSeconds,
                decision.Reason);
            return ActionResult.Success(
                "The model decision was converted to a non-authoritative HarvestDinner proposal.");
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }

    public sealed class TownEventInvitation
    {
        internal TownEventInvitation(
            ResidentId residentId,
            TownEventInvitationStatus status)
        {
            ResidentId = residentId;
            Status = status;
        }

        public ResidentId ResidentId { get; }

        public TownEventInvitationStatus Status { get; internal set; }
    }

    public sealed class TownEventSession
    {
        private readonly List<TownEventInvitation> invitations;
        private readonly ReadOnlyCollection<TownEventInvitation> readOnlyInvitations;
        private readonly List<ResidentId> participantResidentIds = new List<ResidentId>();
        private readonly ReadOnlyCollection<ResidentId> readOnlyParticipantResidentIds;
        private readonly HashSet<ResidentId> arrivedResidentIds = new HashSet<ResidentId>();
        private readonly Dictionary<ResidentId, InteractionPointReservation> reservations =
            new Dictionary<ResidentId, InteractionPointReservation>();

        internal TownEventSession(
            string eventId,
            TownEventProposal proposal,
            ResidentId conversationSourceResidentId,
            double scheduledStartGameSeconds,
            IEnumerable<ResidentId> invitedResidentIds)
        {
            EventId = eventId;
            Proposal = proposal;
            ConversationSourceResidentId = conversationSourceResidentId;
            ScheduledStartGameSeconds = scheduledStartGameSeconds;
            invitations = new List<TownEventInvitation>();
            foreach (ResidentId residentId in invitedResidentIds)
            {
                invitations.Add(new TownEventInvitation(
                    residentId,
                    residentId == proposal.ProposerResidentId
                        ? TownEventInvitationStatus.Accepted
                        : TownEventInvitationStatus.Pending));
            }

            invitations.Sort((left, right) =>
                left.ResidentId.CompareTo(right.ResidentId));
            readOnlyInvitations =
                new ReadOnlyCollection<TownEventInvitation>(invitations);
            readOnlyParticipantResidentIds =
                new ReadOnlyCollection<ResidentId>(participantResidentIds);
        }

        public string EventId { get; }

        public TownEventProposal Proposal { get; }

        public ResidentId ConversationSourceResidentId { get; }

        public TownEventType EventType => Proposal.EventType;

        public string DefinitionId => TownEventCoordinator.HarvestDinnerDefinitionId;

        public TownLocationId LocationId => TownEventCoordinator.HarvestDinnerLocationId;

        public double ScheduledStartGameSeconds { get; }

        public double ActiveStartGameSeconds { get; private set; }

        public double GatheringDeadlineMonotonicSeconds { get; private set; }

        public TownEventState State { get; private set; } = TownEventState.Scheduled;

        public string EndReason { get; private set; } = string.Empty;

        public IReadOnlyList<TownEventInvitation> Invitations => readOnlyInvitations;

        public IReadOnlyList<ResidentId> ParticipantResidentIds =>
            readOnlyParticipantResidentIds;

        public int ArrivedResidentCount => arrivedResidentIds.Count;

        public bool IsTerminal => State == TownEventState.Completed ||
            State == TownEventState.Cancelled;

        public bool IsParticipant(ResidentId residentId)
        {
            return participantResidentIds.Contains(residentId);
        }

        public bool HasArrived(ResidentId residentId)
        {
            return arrivedResidentIds.Contains(residentId);
        }

        public bool TryGetInvitation(
            ResidentId residentId,
            out TownEventInvitation invitation)
        {
            foreach (TownEventInvitation candidate in invitations)
            {
                if (candidate.ResidentId == residentId)
                {
                    invitation = candidate;
                    return true;
                }
            }

            invitation = null;
            return false;
        }

        public bool TryGetReservation(
            ResidentId residentId,
            out InteractionPointReservation reservation)
        {
            return reservations.TryGetValue(residentId, out reservation);
        }

        internal void BeginGathering(
            IEnumerable<ResidentId> participants,
            IDictionary<ResidentId, InteractionPointReservation> participantReservations,
            double deadlineMonotonicSeconds)
        {
            participantResidentIds.Clear();
            participantResidentIds.AddRange(participants);
            participantResidentIds.Sort();
            reservations.Clear();
            foreach (KeyValuePair<ResidentId, InteractionPointReservation> pair in
                participantReservations)
            {
                reservations.Add(pair.Key, pair.Value);
            }

            arrivedResidentIds.Clear();
            GatheringDeadlineMonotonicSeconds = deadlineMonotonicSeconds;
            State = TownEventState.Gathering;
        }

        internal bool RecordArrival(ResidentId residentId)
        {
            return IsParticipant(residentId) && arrivedResidentIds.Add(residentId);
        }

        internal bool HaveAllParticipantsArrived()
        {
            return participantResidentIds.Count >= TownEventCoordinator.MinimumParticipants &&
                arrivedResidentIds.Count == participantResidentIds.Count;
        }

        internal void Activate(double gameSeconds)
        {
            ActiveStartGameSeconds = gameSeconds;
            State = TownEventState.Active;
        }

        internal void Complete()
        {
            State = TownEventState.Completed;
            EndReason = "HarvestDinner completed by the deterministic local clock.";
        }

        internal void Cancel(string reason)
        {
            State = TownEventState.Cancelled;
            EndReason = (reason ?? string.Empty).Trim();
        }
    }

    public sealed class TownEventCoordinator
    {
        public const string HarvestDinnerDefinitionId = "town-event-harvest-dinner";
        public const int HarvestDinnerStartMinute = 18 * 60;
        public const double GameSecondsPerDay = 24d * 60d * 60d;
        public const double DefaultDurationGameSeconds = 30d * 60d;
        public const double DefaultArrivalTimeoutSeconds = 15d;
        public const double DefaultReservationLeaseSeconds = 5d;
        public const int MinimumParticipants = 2;

        public static TownLocationId HarvestDinnerLocationId { get; } =
            new TownLocationId("location-plaza");

        private readonly ResidentRegistry residentRegistry;
        private readonly InteractionPointReservationService reservationService;
        private readonly WorldEventLog eventLog;
        private readonly Dictionary<ResidentId, string> attendancePointByResident =
            new Dictionary<ResidentId, string>();
        private readonly HashSet<string> usedRootFactIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly double durationGameSeconds;
        private readonly double arrivalTimeoutSeconds;
        private readonly double reservationLeaseSeconds;

        public TownEventCoordinator(
            ResidentRegistry registry,
            InteractionPointReservationService reservations,
            WorldEventLog worldEvents,
            IReadOnlyDictionary<ResidentId, string> attendancePointIds,
            double harvestDinnerDurationGameSeconds = DefaultDurationGameSeconds,
            double gatheringArrivalTimeoutSeconds = DefaultArrivalTimeoutSeconds,
            double leaseSeconds = DefaultReservationLeaseSeconds)
        {
            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            reservationService = reservations ??
                throw new ArgumentNullException(nameof(reservations));
            eventLog = worldEvents ?? throw new ArgumentNullException(nameof(worldEvents));
            if (!IsFinitePositive(harvestDinnerDurationGameSeconds) ||
                !IsFinitePositive(gatheringArrivalTimeoutSeconds) ||
                !IsFinitePositive(leaseSeconds) ||
                leaseSeconds > gatheringArrivalTimeoutSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(harvestDinnerDurationGameSeconds),
                    "Town-event duration, arrival timeout, and renewable lease must be positive.");
            }

            if (residentRegistry.Count != ResidentIds.TownResidents.Count ||
                attendancePointIds == null ||
                attendancePointIds.Count != ResidentIds.TownResidents.Count)
            {
                throw new ArgumentException(
                    "HarvestDinner requires exactly the four fixed residents and four seats.");
            }

            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                if (residentRegistry.TryGetRuntimeState(residentId, out _).Failed ||
                    !attendancePointIds.TryGetValue(residentId, out string pointId) ||
                    string.IsNullOrWhiteSpace(pointId) || !pointIds.Add(pointId.Trim()))
                {
                    throw new ArgumentException(
                        "Every fixed resident requires a different HarvestDinner attendance point.",
                        nameof(attendancePointIds));
                }

                attendancePointByResident.Add(residentId, pointId.Trim());
            }

            durationGameSeconds = harvestDinnerDurationGameSeconds;
            arrivalTimeoutSeconds = gatheringArrivalTimeoutSeconds;
            reservationLeaseSeconds = leaseSeconds;
        }

        public TownEventSession CurrentSession { get; private set; }

        public int ActiveTownEventCount => CurrentSession != null &&
            !CurrentSession.IsTerminal ? 1 : 0;

        public ActionResult TrySchedule(
            TownEventProposal proposal,
            out TownEventSession session)
        {
            session = null;
            if (proposal == null || proposal.EventType != TownEventType.HarvestDinner)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "The first town-event version accepts only HarvestDinner proposals.");
            }

            if (ActiveTownEventCount != 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only one scheduled, gathering, or active town event is allowed.");
            }

            ActionResult runtimeResolved = residentRegistry.TryGetRuntimeState(
                proposal.ProposerResidentId,
                out ResidentRuntimeState proposerRuntime);
            if (runtimeResolved.Failed)
            {
                return runtimeResolved;
            }

            ActionResult sourceResolved = proposerRuntime.Memories.TryGetKnowledge(
                proposal.ProposerResidentId,
                proposal.SourceKnowledgeId,
                out MemoryEntry sourceKnowledge);
            if (sourceResolved.Failed || sourceKnowledge == null ||
                sourceKnowledge.OwnerResidentId != proposal.ProposerResidentId ||
                sourceKnowledge.SourceKind != MemorySourceKind.Conversation ||
                !sourceKnowledge.ImmediateSourceResidentId.IsValid ||
                !sourceKnowledge.IsShareable ||
                !string.Equals(
                    sourceKnowledge.SourceEventId,
                    proposal.SourceConversationId,
                    StringComparison.Ordinal) ||
                sourceKnowledge.GameSeconds > proposal.ProposedAtGameSeconds ||
                !HasTag(sourceKnowledge, "harvest") ||
                !HasTag(sourceKnowledge, "carrot") ||
                usedRootFactIds.Contains(sourceKnowledge.RootFactId) ||
                HasPersistedConsumptionMarker(sourceKnowledge.RootFactId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner requires a new shareable carrot-harvest fact learned in an actual conversation.");
            }

            ActionResult provenance = ValidateConversationProvenance(sourceKnowledge);
            if (provenance.Failed)
            {
                return provenance;
            }

            double scheduledStart = ResolveNextHarvestDinnerStart(
                proposal.ProposedAtGameSeconds);
            long dayNumber = (long)Math.Floor(scheduledStart / GameSecondsPerDay) + 1L;
            string eventId = $"town-event:harvest-dinner:day-{dayNumber:D6}";
            var candidate = new TownEventSession(
                eventId,
                proposal,
                sourceKnowledge.ImmediateSourceResidentId,
                scheduledStart,
                ResidentIds.TownResidents);
            ActionResult consumptionRecorded = proposerRuntime.Memories.AddObservation(
                proposal.ProposerResidentId,
                proposal.ProposedAtGameSeconds,
                "我已经用这条胡萝卜收获消息提出了收获晚餐，不能重复安排。",
                MemoryEntry.MaximumImportance,
                WorldEventKind.TownEventProposed,
                MemorySourceKind.Perception,
                sourceEventId: eventId + ":proposal-consumed",
                rootFactId: sourceKnowledge.RootFactId,
                // This is a local idempotency marker rather than another link in the
                // conversation provenance chain. Keeping the shared RootFactId is
                // sufficient for replay rejection and remains valid across save/load.
                parentKnowledgeId: string.Empty,
                immediateSourceResidentId: default,
                tags: new[]
                {
                    "town-event",
                    "harvest-dinner",
                    "town-event-proposal-consumed"
                },
                isShareable: false,
                entry: out MemoryEntry _);
            if (consumptionRecorded.Failed)
            {
                return consumptionRecorded;
            }

            ActionResult published = eventLog.RecordPublicTownEvent(
                proposal.ProposedAtGameSeconds,
                WorldEventKind.TownEventProposed,
                $"{proposerRuntime.Definition.DisplayName}邀请全镇居民于18:00到广场参加收获晚餐。",
                proposal.ProposerResidentId,
                factId: eventId + ":invitation",
                tags: new[] { "town-event", "harvest-dinner", "invitation", "carrot" });
            if (published.Failed)
            {
                return published;
            }

            usedRootFactIds.Add(sourceKnowledge.RootFactId);
            CurrentSession = candidate;
            session = candidate;
            return ActionResult.Success(
                "HarvestDinner invitation scheduled for the next valid 18:00 at the plaza.");
        }

        public ActionResult DecideInvitation(
            ResidentId residentId,
            TownEventInvitationStatus decision)
        {
            if (CurrentSession == null || CurrentSession.State != TownEventState.Scheduled ||
                !residentId.IsValid ||
                (decision != TownEventInvitationStatus.Accepted &&
                    decision != TownEventInvitationStatus.Declined) ||
                !CurrentSession.TryGetInvitation(residentId, out TownEventInvitation invitation))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A pending scheduled invitation can only be accepted or declined by its resident.");
            }

            if (residentId == CurrentSession.Proposal.ProposerResidentId &&
                decision != TownEventInvitationStatus.Accepted)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The proposer already committed to attending HarvestDinner.");
            }

            if (invitation.Status == decision)
            {
                return ActionResult.Success("The same invitation decision was already recorded.");
            }

            if (invitation.Status != TownEventInvitationStatus.Pending)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A final invitation decision cannot be overwritten.");
            }

            invitation.Status = decision;
            return ActionResult.Success($"{residentId} recorded a HarvestDinner decision.");
        }

        public ActionResult MarkUnavailable(ResidentId residentId)
        {
            if (CurrentSession == null || CurrentSession.State != TownEventState.Scheduled ||
                !CurrentSession.TryGetInvitation(residentId, out TownEventInvitation invitation))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only an invited resident can become unavailable before gathering.");
            }

            if (invitation.Status == TownEventInvitationStatus.Unavailable)
            {
                return ActionResult.Success("The resident was already unavailable.");
            }

            if (invitation.Status != TownEventInvitationStatus.Accepted)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only an accepted invitation can become unavailable.");
            }

            invitation.Status = TownEventInvitationStatus.Unavailable;
            return ActionResult.Success($"{residentId} cannot attend this HarvestDinner.");
        }

        public IReadOnlyList<ResidentId> GetAcceptedInvitees()
        {
            if (CurrentSession == null)
            {
                return Array.Empty<ResidentId>();
            }

            var accepted = new List<ResidentId>();
            foreach (TownEventInvitation invitation in CurrentSession.Invitations)
            {
                if (invitation.Status == TownEventInvitationStatus.Accepted)
                {
                    accepted.Add(invitation.ResidentId);
                }
            }

            return new ReadOnlyCollection<ResidentId>(accepted);
        }

        public ActionResult TryBeginGathering(
            double gameSeconds,
            double monotonicSeconds)
        {
            if (CurrentSession == null || CurrentSession.State != TownEventState.Scheduled ||
                !IsFiniteNonNegative(gameSeconds) ||
                !IsFiniteNonNegative(monotonicSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A valid scheduled town event is required before gathering.");
            }

            if (gameSeconds < CurrentSession.ScheduledStartGameSeconds)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner cannot gather residents before 18:00.");
            }

            var participants = new List<ResidentId>();
            foreach (TownEventInvitation invitation in CurrentSession.Invitations)
            {
                if (invitation.Status == TownEventInvitationStatus.Pending)
                {
                    invitation.Status = TownEventInvitationStatus.Declined;
                }

                if (invitation.Status == TownEventInvitationStatus.Accepted)
                {
                    participants.Add(invitation.ResidentId);
                }
            }

            if (participants.Count < MinimumParticipants)
            {
                return CancelInternal(
                    gameSeconds,
                    "HarvestDinner requires at least two accepted and available residents.");
            }

            var acquired = new Dictionary<ResidentId, InteractionPointReservation>();
            foreach (ResidentId residentId in participants)
            {
                ActionResult reserved = reservationService.TryReserve(
                    residentId,
                    attendancePointByResident[residentId],
                    monotonicSeconds,
                    reservationLeaseSeconds,
                    out InteractionPointReservation reservation);
                if (reserved.Failed)
                {
                    Release(acquired);
                    return CancelInternal(
                        gameSeconds,
                        $"HarvestDinner could not atomically reserve every accepted seat: {reserved.Message}");
                }

                acquired.Add(residentId, reservation);
            }

            CurrentSession.BeginGathering(
                participants,
                acquired,
                monotonicSeconds + arrivalTimeoutSeconds);
            return ActionResult.Success(
                "Accepted residents reserved distinct plaza seats and began gathering.");
        }

        public ActionResult MarkArrived(
            ResidentId residentId,
            double gameSeconds,
            double monotonicSeconds)
        {
            if (CurrentSession == null || CurrentSession.State != TownEventState.Gathering ||
                !IsFiniteNonNegative(gameSeconds) ||
                !IsFiniteNonNegative(monotonicSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a gathering HarvestDinner can record a valid arrival.");
            }

            if (monotonicSeconds >= CurrentSession.GatheringDeadlineMonotonicSeconds)
            {
                return CancelInternal(
                    gameSeconds,
                    "HarvestDinner gathering timed out before all participants arrived.");
            }

            if (!CurrentSession.TryGetReservation(
                    residentId,
                    out InteractionPointReservation reservation) ||
                !reservationService.TryGetOwner(
                    reservation.InteractionPointId,
                    monotonicSeconds,
                    out ResidentId owner) || owner != residentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a reserved HarvestDinner participant can be marked arrived.");
            }

            if (!CurrentSession.HasArrived(residentId))
            {
                CurrentSession.RecordArrival(residentId);
            }

            if (!CurrentSession.HaveAllParticipantsArrived())
            {
                return ActionResult.Success($"{residentId} arrived at HarvestDinner.");
            }

            ActionResult published = eventLog.RecordPublicTownEvent(
                gameSeconds,
                WorldEventKind.TownEventStarted,
                "收获晚餐在广场开始了；活动使用公共欢迎语，可选短对话仍按双人约束执行。",
                CurrentSession.Proposal.ProposerResidentId,
                factId: CurrentSession.EventId + ":started",
                tags: new[] { "town-event", "harvest-dinner", "started", "plaza" });
            if (published.Failed)
            {
                return published;
            }

            CurrentSession.Activate(gameSeconds);
            return ActionResult.Success("All actual participants arrived; HarvestDinner started.");
        }

        public ActionResult Tick(double gameSeconds, double monotonicSeconds)
        {
            if (!IsFiniteNonNegative(gameSeconds) ||
                !IsFiniteNonNegative(monotonicSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Town-event ticks require valid game and monotonic clocks.");
            }

            if (CurrentSession == null || CurrentSession.IsTerminal ||
                CurrentSession.State == TownEventState.Scheduled)
            {
                return ActionResult.Success("No active town-event lease requires a tick.");
            }

            ActionResult renewed = RenewParticipantReservations(monotonicSeconds);
            if (renewed.Failed)
            {
                return CancelInternal(
                    gameSeconds,
                    "HarvestDinner ended because an attendance reservation was lost.");
            }

            if (CurrentSession.State == TownEventState.Gathering)
            {
                return monotonicSeconds >=
                        CurrentSession.GatheringDeadlineMonotonicSeconds
                    ? CancelInternal(
                        gameSeconds,
                        "HarvestDinner gathering timed out before all participants arrived.")
                    : ActionResult.Success("HarvestDinner participants are still gathering.");
            }

            if (CurrentSession.State == TownEventState.Active &&
                gameSeconds >= CurrentSession.ActiveStartGameSeconds +
                    durationGameSeconds)
            {
                return CompleteInternal(gameSeconds);
            }

            return ActionResult.Success("HarvestDinner remains active.");
        }

        public ActionResult Cancel(double gameSeconds, string reason)
        {
            if (CurrentSession == null || CurrentSession.IsTerminal ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a non-terminal town event can be cancelled.");
            }

            return CancelInternal(gameSeconds, reason);
        }

        public ActionResult Reset()
        {
            ReleaseCurrentReservations();
            CurrentSession = null;
            usedRootFactIds.Clear();
            return ActionResult.Success("Town-event state and transient reservations reset.");
        }

        public string GetAttendancePointId(ResidentId residentId)
        {
            return attendancePointByResident.TryGetValue(residentId, out string pointId)
                ? pointId
                : string.Empty;
        }

        public static double ResolveNextHarvestDinnerStart(double gameSeconds)
        {
            if (!IsFiniteNonNegative(gameSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            }

            double dayStart = Math.Floor(gameSeconds / GameSecondsPerDay) *
                GameSecondsPerDay;
            double candidate = dayStart + HarvestDinnerStartMinute * 60d;
            return gameSeconds <= candidate ? candidate : candidate + GameSecondsPerDay;
        }

        private ActionResult CompleteInternal(double gameSeconds)
        {
            foreach (ResidentId residentId in CurrentSession.ParticipantResidentIds)
            {
                ActionResult definitionResolved = residentRegistry.TryGetDefinition(
                    residentId,
                    out ResidentDefinition definition);
                if (definitionResolved.Failed)
                {
                    return definitionResolved;
                }

                ActionResult recorded = eventLog.RecordPrivate(
                    gameSeconds,
                    WorldEventKind.TownEventCompleted,
                    $"{definition.DisplayName}实际参加了广场的收获晚餐，并一起分享了胡萝卜汤。",
                    residentId,
                    factId: CurrentSession.EventId + ":attended:" + residentId.Value,
                    tags: new[]
                    {
                        "town-event",
                        "harvest-dinner",
                        "attended",
                        "carrot-soup"
                    },
                    actorResidentId: residentId);
                if (recorded.Failed)
                {
                    return recorded;
                }
            }

            ReleaseCurrentReservations();
            CurrentSession.Complete();
            return ActionResult.Success(
                "HarvestDinner completed and only actual participants received attendance memories.");
        }

        private ActionResult CancelInternal(double gameSeconds, string reason)
        {
            string boundedReason = (reason ?? string.Empty).Trim();
            if (boundedReason.Length == 0)
            {
                boundedReason = "HarvestDinner was cancelled by local constraints.";
            }

            if (boundedReason.Length > 240)
            {
                boundedReason = boundedReason.Substring(0, 240);
            }

            ReleaseCurrentReservations();
            CurrentSession.Cancel(boundedReason);
            eventLog.RecordPublicTownEvent(
                gameSeconds,
                WorldEventKind.TownEventCancelled,
                "本次收获晚餐未能满足本地活动约束，已经安全取消。",
                CurrentSession.Proposal.ProposerResidentId,
                factId: CurrentSession.EventId + ":cancelled",
                tags: new[] { "town-event", "harvest-dinner", "cancelled" });
            return ActionResult.Failure(ActionFailureReason.InvalidState, boundedReason);
        }

        private ActionResult RenewParticipantReservations(double monotonicSeconds)
        {
            foreach (ResidentId residentId in CurrentSession.ParticipantResidentIds)
            {
                if (!CurrentSession.TryGetReservation(
                        residentId,
                        out InteractionPointReservation reservation))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        "A participant has no HarvestDinner reservation.");
                }

                ActionResult renewed = reservationService.Renew(
                    reservation,
                    monotonicSeconds,
                    reservationLeaseSeconds);
                if (renewed.Failed)
                {
                    return renewed;
                }
            }

            return ActionResult.Success("HarvestDinner attendance reservations renewed.");
        }

        private ActionResult ValidateConversationProvenance(MemoryEntry receivedKnowledge)
        {
            ActionResult sourceRuntimeResolved = residentRegistry.TryGetRuntimeState(
                receivedKnowledge.ImmediateSourceResidentId,
                out ResidentRuntimeState sourceRuntime);
            if (sourceRuntimeResolved.Failed ||
                string.IsNullOrWhiteSpace(receivedKnowledge.ParentKnowledgeId) ||
                sourceRuntime.Memories.TryGetKnowledge(
                    receivedKnowledge.ImmediateSourceResidentId,
                    receivedKnowledge.ParentKnowledgeId,
                    out MemoryEntry parentKnowledge).Failed ||
                parentKnowledge == null || !parentKnowledge.IsShareable ||
                !string.Equals(
                    parentKnowledge.RootFactId,
                    receivedKnowledge.RootFactId,
                    StringComparison.Ordinal) ||
                !HasTag(parentKnowledge, "harvest") ||
                !HasTag(parentKnowledge, "carrot"))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "HarvestDinner proposal provenance must resolve to the immediate speaker's matching shareable harvest fact.");
            }

            return ActionResult.Success(
                "HarvestDinner proposal provenance resolves across the completed conversation.");
        }

        private bool HasPersistedConsumptionMarker(string rootFactId)
        {
            foreach (ResidentId residentId in residentRegistry.ResidentIds)
            {
                if (residentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtime).Failed ||
                    runtime == null)
                {
                    continue;
                }

                foreach (MemoryEntry memory in runtime.Memories.Entries)
                {
                    if (memory != null &&
                        string.Equals(memory.RootFactId, rootFactId, StringComparison.Ordinal) &&
                        HasTag(memory, "town-event-proposal-consumed"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void ReleaseCurrentReservations()
        {
            if (CurrentSession == null)
            {
                return;
            }

            foreach (ResidentId residentId in CurrentSession.ParticipantResidentIds)
            {
                if (CurrentSession.TryGetReservation(
                        residentId,
                        out InteractionPointReservation reservation))
                {
                    reservationService.Release(reservation);
                }
            }
        }

        private void Release(
            IDictionary<ResidentId, InteractionPointReservation> reservations)
        {
            foreach (InteractionPointReservation reservation in reservations.Values)
            {
                reservationService.Release(reservation);
            }
        }

        private static bool HasTag(MemoryEntry memory, string expectedTag)
        {
            foreach (string tag in memory.Tags)
            {
                if (string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
