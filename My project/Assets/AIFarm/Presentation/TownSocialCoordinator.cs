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
                Session = session ?? throw new ArgumentNullException(nameof(session));
                if (firstResident == null || secondResident == null ||
                    firstResident.ResidentId == secondResident.ResidentId ||
                    !session.IsParticipant(firstResident.ResidentId) ||
                    !session.IsParticipant(secondResident.ResidentId))
                {
                    throw new ArgumentException(
                        "Scene conversation controllers must match its two participants.");
                }

                FirstResident = firstResident.ResidentId == session.FirstResidentId
                    ? firstResident
                    : secondResident;
                SecondResident = secondResident.ResidentId == session.SecondResidentId
                    ? secondResident
                    : firstResident;
                NextLineAtSeconds = nextLineAtSeconds;
                RequestedConversationVersion = session.ConversationVersion;
            }

            public ConversationSession Session { get; }

            public TownResidentScheduleController FirstResident { get; }

            public TownResidentScheduleController SecondResident { get; }

            public AiRequestCancellation RequestCancellation { get; } =
                new AiRequestCancellation();

            public long RequestedConversationVersion { get; }

            public bool FirstArrived { get; set; }

            public bool SecondArrived { get; set; }

            public double NextLineAtSeconds { get; set; }

            public bool IsFinalLineVisible { get; set; }

            public double FinalLineVisibleUntilSeconds { get; set; }
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
                    "Town social coordination requires bootstrap, scheduling, residents, and anchors.");
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
            return ActionResult.Success("Town social scene references configured.");
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
                bootstrap.AiRequests == null ||
                scheduleCoordinator == null || !scheduleCoordinator.IsInitialized ||
                residents == null || residents.Length < 2 ||
                conversationAnchors == null || conversationAnchors.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town social coordination requires initialized scheduling, AI requests, and scene references.");
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
            return ActionResult.Success(
                "Two-resident social system initialized with remote-script and local-template paths.");
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
                    if (record.Session.State == ConversationState.Completed &&
                        record.IsFinalLineVisible &&
                        monotonicSeconds < record.FinalLineVisibleUntilSeconds)
                    {
                        continue;
                    }

                    CleanupSceneConversation(record);
                    continue;
                }

                ActionResult movement = TickMovement(record, deltaTime);
                if (movement.Failed)
                {
                    CancelAndCleanup(record, ConversationEndReason.NavigationFailed);
                    continue;
                }

                if (!record.FirstArrived || !record.SecondArrived ||
                    !record.Session.HasPreparedScript ||
                    monotonicSeconds < record.NextLineAtSeconds)
                {
                    continue;
                }

                ActionResult advanced = ConversationCoordinator.AdvanceConversation(
                    record.Session.ConversationId,
                    monotonicSeconds,
                    gameSeconds,
                    out ConversationUtterance utterance);

                if (advanced.Succeeded && utterance != null)
                {
                    PresentUtterance(record, utterance);
                }

                record.NextLineAtSeconds = monotonicSeconds + lineIntervalSeconds;
                if (record.Session.State == ConversationState.Completed)
                {
                    record.IsFinalLineVisible = true;
                    record.FinalLineVisibleUntilSeconds = record.NextLineAtSeconds;
                }
                else if (record.Session.IsTerminal)
                {
                    CleanupSceneConversation(record);
                }

                if (advanced.Failed && !record.Session.IsTerminal)
                {
                    CancelAndCleanup(record, ConversationEndReason.Cancelled);
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
                return ActionResult.Success("No eligible social opportunity this tick.");
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
            var record = new SceneConversation(
                session,
                firstResident,
                secondResident,
                monotonicSeconds);
            sceneConversations.Add(session.ConversationId, record);
            StartCoroutine(RequestConversationScript(record));
            return ActionResult.Success(
                "Residents locked a local session and are preparing a conversation script.");
        }

        private IEnumerator RequestConversationScript(SceneConversation record)
        {
            // Defer beyond the Update() call that detected the social opportunity.
            yield return null;
            if (!IsCurrent(record))
            {
                yield break;
            }

            ActionResult built = TryBuildConversationRequest(
                record,
                out ConversationScriptRequest request);
            AiGatewayResult<ConversationScriptSpec> result = null;
            if (built.Succeeded)
            {
                yield return bootstrap.AiRequests.GenerateConversationScript(
                    request,
                    AiRequestPriority.Normal,
                    record.RequestCancellation,
                    value => result = value);
            }

            if (!IsCurrent(record) || record.RequestCancellation.IsCancellationRequested ||
                record.Session.ConversationVersion != record.RequestedConversationVersion)
            {
                yield break;
            }

            ActionResult prepared = result != null && result.Succeeded &&
                    result.ResidentId == record.Session.FirstResidentId
                ? ConversationCoordinator.SetConversationScript(
                    record.Session.ConversationId,
                    result.Value)
                : ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    built.Failed
                        ? built.Message
                        : result?.Outcome.Message ?? "Conversation AI returned no result.");
            if (prepared.Failed)
            {
                prepared = ConversationCoordinator.PrepareLocalFallbackScript(
                    record.Session.ConversationId);
            }

            if (prepared.Failed)
            {
                Debug.LogWarning(
                    $"Conversation {record.Session.ConversationId} could not prepare a script: " +
                    prepared.Message,
                    this);
                CancelAndCleanup(record, ConversationEndReason.Cancelled);
                yield break;
            }

            Debug.Log(
                $"Conversation script ready residentId={record.Session.FirstResidentId} " +
                $"participants={record.Session.FirstResidentId},{record.Session.SecondResidentId} " +
                $"provider={record.Session.ScriptProvider} lines={record.Session.TargetSentenceCount}",
                this);
        }

        private ActionResult TryBuildConversationRequest(
            SceneConversation record,
            out ConversationScriptRequest request)
        {
            request = null;
            ActionResult firstContext = TryBuildResidentContext(
                record.FirstResident,
                record.SecondResident.ResidentId,
                out ResidentContext first);
            ActionResult secondContext = TryBuildResidentContext(
                record.SecondResident,
                record.FirstResident.ResidentId,
                out ResidentContext second);
            if (firstContext.Failed || secondContext.Failed)
            {
                return firstContext.Failed ? firstContext : secondContext;
            }

            try
            {
                request = new ConversationScriptRequest(
                    record.Session.FirstResidentId,
                    new[]
                    {
                        record.Session.FirstResidentId,
                        record.Session.SecondResidentId
                    },
                    new[] { first, second },
                    $"在{record.FirstResident.CurrentLocationId.Value}的日常交流",
                    record.Session.RequestedSentenceLimit);
                return ActionResult.Success("Isolated participant contexts collected.");
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Conversation context validation failed: {exception.Message}");
            }
        }

        private ActionResult TryBuildResidentContext(
            TownResidentScheduleController resident,
            ResidentId otherResidentId,
            out ResidentContext context)
        {
            context = null;
            ActionResult definitionResult = bootstrap.ResidentRegistry.TryGetDefinition(
                resident.ResidentId,
                out ResidentDefinition definition);
            ActionResult runtimeResult = bootstrap.ResidentRegistry.TryGetRuntimeState(
                resident.ResidentId,
                out ResidentRuntimeState runtime);
            ActionResult relationshipResult = SocialGraph.TryGetRelationship(
                resident.ResidentId,
                otherResidentId,
                out RelationshipState relationship);
            if (definitionResult.Failed || runtimeResult.Failed || relationshipResult.Failed)
            {
                if (definitionResult.Failed)
                {
                    return definitionResult;
                }

                return runtimeResult.Failed ? runtimeResult : relationshipResult;
            }

            ActionResult memoryResult = runtime.Memories.GetRecent(
                resident.ResidentId,
                6,
                out IReadOnlyList<MemoryEntry> recentMemories);
            if (memoryResult.Failed)
            {
                return memoryResult;
            }

            var memories = new List<ResidentMemorySnapshot>(recentMemories.Count);
            foreach (MemoryEntry memory in recentMemories)
            {
                // Each context reads only its own strictly partitioned MemoryStore.
                memories.Add(new ResidentMemorySnapshot(
                    resident.ResidentId,
                    memory.Text,
                    memory.Importance));
            }

            string currentState =
                $"schedule={resident.Runtime.State};activity={resident.CurrentActivity};" +
                $"location={resident.CurrentLocationId.Value};farmBusy={resident.IsFarmingBusy};" +
                $"hasGoal={runtime.CurrentGoal != null}";
            try
            {
                context = new ResidentContext(
                    resident.ResidentId,
                    ResidentPersonaSnapshot.FromDefinition(definition),
                    currentState,
                    new[]
                    {
                        new RelationshipSnapshot(
                            resident.ResidentId,
                            otherResidentId,
                            relationship.Familiarity,
                            relationship.Trust)
                    },
                    memories);
                return ActionResult.Success("Resident AI context collected without private-memory crossover.");
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Resident context validation failed: {exception.Message}");
            }
        }

        private void PresentUtterance(
            SceneConversation record,
            ConversationUtterance utterance)
        {
            record.FirstResident.ClearConversationLine();
            record.SecondResident.ClearConversationLine();
            record.FirstResident.FaceConversationPartner(record.SecondResident.transform);
            record.SecondResident.FaceConversationPartner(record.FirstResident.transform);
            if (utterance.SpeakerResidentId == record.FirstResident.ResidentId)
            {
                record.FirstResident.ShowConversationLine(utterance);
            }
            else if (utterance.SpeakerResidentId == record.SecondResident.ResidentId)
            {
                record.SecondResident.ShowConversationLine(utterance);
            }
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

        private bool IsCurrent(SceneConversation record)
        {
            return record != null && !record.Session.IsTerminal &&
                sceneConversations.TryGetValue(
                    record.Session.ConversationId,
                    out SceneConversation current) &&
                ReferenceEquals(current, record);
        }

        private void CancelAndCleanup(
            SceneConversation record,
            ConversationEndReason reason)
        {
            if (record == null)
            {
                return;
            }

            ConversationCoordinator.CancelConversation(
                record.Session.ConversationId,
                reason);
            CleanupSceneConversation(record);
        }

        private void CleanupSceneConversation(SceneConversation record)
        {
            if (record == null ||
                !sceneConversations.Remove(record.Session.ConversationId))
            {
                return;
            }

            record.RequestCancellation.Cancel();
            record.SecondResident.ClearConversationLine();
            record.FirstResident.ClearConversationLine();
            record.SecondResident.ResumeAfterConversation();
            record.FirstResident.ResumeAfterConversation();
        }

        private void Awake()
        {
            ActionResult result = Initialize();
            if (result.Failed)
            {
                Debug.LogError($"Town social system did not start: {result.Message}", this);
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
                Debug.LogWarning($"Town social flow recovered from: {result.Message}", this);
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
                CancelAndCleanup(record, ConversationEndReason.Cancelled);
            }

            StopAllCoroutines();
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
