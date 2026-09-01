using System;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;

namespace AIFarm.Social
{
    public sealed class ConversationCoordinator
    {
        public const double DefaultTimeoutSeconds = 30d;

        private sealed class ActiveConversationRecord
        {
            public ActiveConversationRecord(
                ConversationSession session,
                ConversationParticipantLease participantLease,
                InteractionPointReservation firstReservation,
                InteractionPointReservation secondReservation)
            {
                Session = session;
                ParticipantLease = participantLease;
                FirstReservation = firstReservation;
                SecondReservation = secondReservation;
            }

            public ConversationSession Session { get; }

            public ConversationParticipantLease ParticipantLease { get; }

            public InteractionPointReservation FirstReservation { get; }

            public InteractionPointReservation SecondReservation { get; }
        }

        private readonly ResidentRegistry residentRegistry;
        private readonly ConversationParticipantLock participantLock;
        private readonly InteractionPointReservationService reservationService;
        private readonly LocalConversationTemplateService templateService;
        private readonly ConversationOutcomeApplier outcomeApplier;
        private readonly Func<ResidentId, bool> participantAvailability;
        private readonly Dictionary<ConversationId, ConversationSession> sessions =
            new Dictionary<ConversationId, ConversationSession>();
        private readonly Dictionary<ConversationId, ActiveConversationRecord> activeSessions =
            new Dictionary<ConversationId, ActiveConversationRecord>();
        private long nextConversationNumber;

        public ConversationCoordinator(
            ResidentRegistry registry,
            ConversationParticipantLock conversationParticipantLock,
            InteractionPointReservationService interactionPointReservations,
            LocalConversationTemplateService localTemplates,
            ConversationOutcomeApplier deterministicOutcomeApplier,
            double timeoutSeconds = DefaultTimeoutSeconds,
            Func<ResidentId, bool> canParticipate = null)
        {
            if (!IsFinitePositive(timeoutSeconds) ||
                timeoutSeconds > ConversationSession.MaximumTimeoutSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutSeconds),
                    "Conversation timeout must be positive and no greater than 30 seconds.");
            }

            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            participantLock = conversationParticipantLock ??
                throw new ArgumentNullException(nameof(conversationParticipantLock));
            reservationService = interactionPointReservations ??
                throw new ArgumentNullException(nameof(interactionPointReservations));
            templateService = localTemplates ?? throw new ArgumentNullException(nameof(localTemplates));
            outcomeApplier = deterministicOutcomeApplier ??
                throw new ArgumentNullException(nameof(deterministicOutcomeApplier));
            participantAvailability = canParticipate ?? (_ => true);
            TimeoutSeconds = timeoutSeconds;
        }

        public double TimeoutSeconds { get; }

        public int ActiveSessionCount => activeSessions.Count;

        public ActionResult ProjectPlayedTranscriptForSave(
            ResidentId residentId,
            double gameSeconds,
            MemoryStore snapshotMemory,
            out bool projected)
        {
            projected = false;
            if (!residentId.IsValid || snapshotMemory == null ||
                snapshotMemory.OwnerResidentId != residentId ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Conversation save projection requires an owner-matched memory snapshot.");
            }

            ActiveConversationRecord ownedRecord = null;
            foreach (ActiveConversationRecord record in activeSessions.Values)
            {
                if (!record.Session.IsParticipant(residentId))
                {
                    continue;
                }

                if (ownedRecord != null)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        $"Resident '{residentId}' belongs to more than one active conversation.");
                }

                ownedRecord = record;
            }

            if (ownedRecord == null || ownedRecord.Session.Utterances.Count == 0)
            {
                return ActionResult.Success("No played conversation fragment required projection.");
            }

            ActionResult result = outcomeApplier.ProjectPlayedTranscriptForResident(
                ownedRecord.Session,
                residentId,
                gameSeconds,
                snapshotMemory);
            projected = result.Succeeded;
            return result;
        }

        public ActionResult TryStartConversation(
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            string firstInteractionPointId,
            string secondInteractionPointId,
            int targetSentenceCount,
            double monotonicSeconds,
            out ConversationSession session)
        {
            session = null;
            if (!firstResidentId.IsValid || !secondResidentId.IsValid ||
                firstResidentId == secondResidentId ||
                string.IsNullOrWhiteSpace(firstInteractionPointId) ||
                string.IsNullOrWhiteSpace(secondInteractionPointId) ||
                string.Equals(
                    firstInteractionPointId.Trim(),
                    secondInteractionPointId.Trim(),
                    StringComparison.Ordinal) ||
                targetSentenceCount < ConversationSession.MinimumSentenceCount ||
                targetSentenceCount > ConversationSession.MaximumSentenceCount ||
                !IsFiniteNonNegative(monotonicSeconds) ||
                !IsFiniteNonNegative(monotonicSeconds + TimeoutSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A conversation requires two residents, two distinct points, 2-6 sentences, and monotonic time.");
            }

            ActionResult firstResident = residentRegistry.TryGetDefinition(
                firstResidentId,
                out _);
            ActionResult secondResident = residentRegistry.TryGetDefinition(
                secondResidentId,
                out _);
            if (firstResident.Failed || secondResident.Failed)
            {
                return firstResident.Failed ? firstResident : secondResident;
            }

            if (!participantAvailability(firstResidentId) ||
                !participantAvailability(secondResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "At least one resident has an action, player instruction, or sleep priority.");
            }

            string firstPoint = firstInteractionPointId.Trim();
            string secondPoint = secondInteractionPointId.Trim();
            NormalizeParticipantsAndPoints(
                ref firstResidentId,
                ref secondResidentId,
                ref firstPoint,
                ref secondPoint);
            var conversationId = new ConversationId(
                $"conversation-{++nextConversationNumber:000000}");
            ActionResult locked = participantLock.TryAcquire(
                conversationId,
                firstResidentId,
                secondResidentId,
                out ConversationParticipantLease participantLease);
            if (locked.Failed)
            {
                return locked;
            }

            double reservationLeaseSeconds = TimeoutSeconds + 1d;
            ActionResult firstReserved = reservationService.TryReserve(
                firstResidentId,
                firstPoint,
                monotonicSeconds,
                reservationLeaseSeconds,
                out InteractionPointReservation firstReservation);
            if (firstReserved.Failed)
            {
                participantLock.Release(participantLease);
                return firstReserved;
            }

            ActionResult secondReserved = reservationService.TryReserve(
                secondResidentId,
                secondPoint,
                monotonicSeconds,
                reservationLeaseSeconds,
                out InteractionPointReservation secondReservation);
            if (secondReserved.Failed)
            {
                reservationService.Release(firstReservation);
                participantLock.Release(participantLease);
                return secondReserved;
            }

            session = new ConversationSession(
                conversationId,
                firstResidentId,
                secondResidentId,
                targetSentenceCount,
                monotonicSeconds,
                TimeoutSeconds);
            ActionResult activated = session.Activate();
            if (activated.Failed)
            {
                reservationService.Release(secondReservation);
                reservationService.Release(firstReservation);
                participantLock.Release(participantLease);
                session = null;
                return activated;
            }

            var record = new ActiveConversationRecord(
                session,
                participantLease,
                firstReservation,
                secondReservation);
            sessions.Add(conversationId, session);
            activeSessions.Add(conversationId, record);
            return ActionResult.Success("Offline two-resident conversation started.");
        }

        public ActionResult SetConversationScript(
            ConversationId conversationId,
            ConversationScriptSpec script)
        {
            if (!conversationId.IsValid || script == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Preparing a conversation requires its ID and a validated script.");
            }

            if (!activeSessions.TryGetValue(
                    conversationId,
                    out ActiveConversationRecord record))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A script cannot be attached to an inactive conversation.");
            }

            if (script.ResidentId != record.Session.FirstResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Conversation script owner does not match the session request owner.");
            }

            ActionResult knowledgeValidation = ValidateSharedKnowledge(
                record.Session,
                script,
                out IReadOnlyList<MemoryEntry> knowledgeSnapshots);
            if (knowledgeValidation.Failed)
            {
                return knowledgeValidation;
            }

            return record.Session.SetPreparedScript(script, knowledgeSnapshots);
        }

        public ActionResult PrepareLocalFallbackScript(ConversationId conversationId)
        {
            if (!conversationId.IsValid ||
                !activeSessions.TryGetValue(
                    conversationId,
                    out ActiveConversationRecord record))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A local fallback requires an active conversation.");
            }

            ActionResult firstResolved = residentRegistry.TryGetDefinition(
                record.Session.FirstResidentId,
                out ResidentDefinition firstResident);
            ActionResult secondResolved = residentRegistry.TryGetDefinition(
                record.Session.SecondResidentId,
                out ResidentDefinition secondResident);
            ActionResult firstRuntimeResolved = residentRegistry.TryGetRuntimeState(
                record.Session.FirstResidentId,
                out ResidentRuntimeState firstRuntime);
            ActionResult secondRuntimeResolved = residentRegistry.TryGetRuntimeState(
                record.Session.SecondResidentId,
                out ResidentRuntimeState secondRuntime);
            if (firstResolved.Failed || secondResolved.Failed ||
                firstRuntimeResolved.Failed || secondRuntimeResolved.Failed)
            {
                return FirstFailure(
                    firstResolved,
                    secondResolved,
                    firstRuntimeResolved,
                    secondRuntimeResolved);
            }

            ActionResult firstKnowledgeSelected = TrySelectShareableKnowledge(
                firstRuntime,
                secondRuntime,
                out MemoryEntry firstSharedKnowledge);
            ActionResult secondKnowledgeSelected = TrySelectShareableKnowledge(
                secondRuntime,
                firstRuntime,
                out MemoryEntry secondSharedKnowledge);
            if (firstKnowledgeSelected.Failed || secondKnowledgeSelected.Failed)
            {
                return firstKnowledgeSelected.Failed
                    ? firstKnowledgeSelected
                    : secondKnowledgeSelected;
            }

            ActionResult created = templateService.CreateScript(
                record.Session,
                firstResident,
                secondResident,
                out ConversationScriptSpec script,
                firstSharedKnowledge,
                secondSharedKnowledge);
            return created.Failed
                ? created
                : SetConversationScript(conversationId, script);
        }

        public ActionResult AdvanceConversation(
            ConversationId conversationId,
            double monotonicSeconds,
            double gameSeconds,
            out ConversationUtterance utterance)
        {
            utterance = null;
            if (!conversationId.IsValid || !IsFiniteNonNegative(monotonicSeconds) ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Advancing a conversation requires its ID and valid clock values.");
            }

            if (!activeSessions.TryGetValue(
                    conversationId,
                    out ActiveConversationRecord record))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The conversation is not active.");
            }

            if (record.Session.IsTimedOut(monotonicSeconds))
            {
                EndAndRelease(
                    record,
                    ConversationState.TimedOut,
                    ConversationEndReason.TimedOut);
                return ActionResult.Success("Conversation timed out before another sentence.");
            }

            if (!ReservationsStillOwned(record, monotonicSeconds))
            {
                EndAndRelease(
                    record,
                    ConversationState.Cancelled,
                    ConversationEndReason.ReservationLost);
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A conversation position reservation was lost.");
            }

            ResidentId speakerId;
            NpcMood mood;
            string emoji;
            string line;
            string sharedKnowledgeId;
            if (record.Session.TryGetNextPreparedLine(
                    out ConversationLineSpec preparedLine))
            {
                speakerId = preparedLine.SpeakerId;
                mood = preparedLine.Mood;
                emoji = preparedLine.Emoji;
                line = preparedLine.Text;
                sharedKnowledgeId = preparedLine.SharedKnowledgeId;
            }
            else if (record.Session.HasPreparedScript)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The prepared conversation script has no playable next line.");
            }
            else
            {
                speakerId = record.Session.TurnOwnerResidentId;
                ResidentId listenerId = speakerId == record.Session.FirstResidentId
                    ? record.Session.SecondResidentId
                    : record.Session.FirstResidentId;
                ActionResult speakerResolved = residentRegistry.TryGetDefinition(
                    speakerId,
                    out ResidentDefinition speaker);
                ActionResult listenerResolved = residentRegistry.TryGetDefinition(
                    listenerId,
                    out ResidentDefinition listener);
                if (speakerResolved.Failed || listenerResolved.Failed)
                {
                    EndAndRelease(
                        record,
                        ConversationState.Cancelled,
                        ConversationEndReason.Cancelled);
                    return speakerResolved.Failed ? speakerResolved : listenerResolved;
                }

                ActionResult generated = templateService.CreateLine(
                    record.Session,
                    speaker,
                    listener,
                    out line);
                if (generated.Failed)
                {
                    EndAndRelease(
                        record,
                        ConversationState.Cancelled,
                        ConversationEndReason.Cancelled);
                    return generated;
                }

                mood = NpcMood.Focused;
                emoji = "💬";
                sharedKnowledgeId = string.Empty;
            }

            ActionResult delivered = record.Session.AddUtterance(
                speakerId,
                mood,
                emoji,
                line,
                sharedKnowledgeId,
                gameSeconds,
                out utterance);
            if (delivered.Failed)
            {
                EndAndRelease(
                    record,
                    ConversationState.Cancelled,
                    ConversationEndReason.Cancelled);
                return delivered;
            }

            if (record.Session.DeliveredSentenceCount < record.Session.TargetSentenceCount)
            {
                return delivered;
            }

            ActionResult applied;
            try
            {
                ConversationOutcome outcome = record.Session.PreparedOutcome ??
                    templateService.SelectOutcome(record.Session);
                ActionResult completed = record.Session.Complete(outcome);
                if (completed.Failed)
                {
                    record.Session.End(
                        ConversationState.Cancelled,
                        ConversationEndReason.Cancelled);
                    ReleaseResources(record);
                    return completed;
                }

                applied = outcomeApplier.Apply(record.Session, outcome, gameSeconds);
            }
            catch (Exception exception)
            {
                record.Session.End(
                    ConversationState.Cancelled,
                    ConversationEndReason.Cancelled);
                ReleaseResources(record);
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    $"Deterministic local conversation outcome failed: {exception.Message}");
            }

            ReleaseResources(record);
            return applied.Failed
                ? applied
                : ActionResult.Success("Offline conversation completed and its outcome was applied.");
        }

        public int TickTimeouts(double monotonicSeconds)
        {
            if (!IsFiniteNonNegative(monotonicSeconds))
            {
                return 0;
            }

            var timedOut = new List<ActiveConversationRecord>();
            foreach (ActiveConversationRecord record in activeSessions.Values)
            {
                if (record.Session.IsTimedOut(monotonicSeconds))
                {
                    timedOut.Add(record);
                }
            }

            foreach (ActiveConversationRecord record in timedOut)
            {
                EndAndRelease(
                    record,
                    ConversationState.TimedOut,
                    ConversationEndReason.TimedOut);
            }

            return timedOut.Count;
        }

        public ActionResult CancelConversation(
            ConversationId conversationId,
            ConversationEndReason reason = ConversationEndReason.Cancelled)
        {
            if (!conversationId.IsValid || reason == ConversationEndReason.None ||
                reason == ConversationEndReason.SentenceLimitReached ||
                reason == ConversationEndReason.TimedOut)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Cancelling requires an active conversation and cancellation reason.");
            }

            if (!activeSessions.TryGetValue(
                    conversationId,
                    out ActiveConversationRecord record))
            {
                return ActionResult.Success("Conversation was already inactive.");
            }

            EndAndRelease(record, ConversationState.Cancelled, reason);
            return ActionResult.Success("Conversation cancelled and resources released.");
        }

        public int CancelAllActiveConversations(
            ConversationEndReason reason = ConversationEndReason.Cancelled)
        {
            if (reason == ConversationEndReason.None ||
                reason == ConversationEndReason.SentenceLimitReached ||
                reason == ConversationEndReason.TimedOut)
            {
                return 0;
            }

            var records = new List<ActiveConversationRecord>(activeSessions.Values);
            foreach (ActiveConversationRecord record in records)
            {
                EndAndRelease(record, ConversationState.Cancelled, reason);
            }

            return records.Count;
        }

        public ActionResult TryGetSession(
            ConversationId conversationId,
            ResidentId requesterResidentId,
            out ConversationSession session)
        {
            session = null;
            if (!conversationId.IsValid || !requesterResidentId.IsValid ||
                !sessions.TryGetValue(conversationId, out session) ||
                !session.IsParticipant(requesterResidentId))
            {
                session = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Only a conversation participant can resolve that session.");
            }

            return ActionResult.Success("Conversation session resolved for its participant.");
        }

        private bool ReservationsStillOwned(
            ActiveConversationRecord record,
            double monotonicSeconds)
        {
            return reservationService.TryGetOwner(
                    record.FirstReservation.InteractionPointId,
                    monotonicSeconds,
                    out ResidentId firstOwner) &&
                firstOwner == record.FirstReservation.ResidentId &&
                reservationService.TryGetOwner(
                    record.SecondReservation.InteractionPointId,
                    monotonicSeconds,
                    out ResidentId secondOwner) &&
                secondOwner == record.SecondReservation.ResidentId;
        }

        private ActionResult ValidateSharedKnowledge(
            ConversationSession session,
            ConversationScriptSpec script,
            out IReadOnlyList<MemoryEntry> knowledgeSnapshots)
        {
            var captured = new List<MemoryEntry>();
            var capturedKeys = new HashSet<string>(StringComparer.Ordinal);
            knowledgeSnapshots = captured;
            foreach (ConversationLineSpec line in script.Lines)
            {
                if (line == null || !session.IsParticipant(line.SpeakerId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Every prepared speaker must belong to this conversation.");
                }

                if (string.IsNullOrEmpty(line.SharedKnowledgeId))
                {
                    continue;
                }

                ActionResult runtimeResolved = residentRegistry.TryGetRuntimeState(
                    line.SpeakerId,
                    out ResidentRuntimeState runtime);
                if (runtimeResolved.Failed)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "A prepared knowledge reference has no resident runtime owner.");
                }

                ActionResult knowledgeResolved = runtime.Memories.TryGetKnowledge(
                    line.SpeakerId,
                    line.SharedKnowledgeId,
                    out MemoryEntry knowledge);
                if (knowledgeResolved.Failed || knowledge == null ||
                    knowledge.OwnerResidentId != line.SpeakerId ||
                    !knowledge.IsShareable)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "A prepared knowledge reference must identify shareable memory owned by its speaker.");
                }

                string key = line.SpeakerId.Value + "\n" + knowledge.KnowledgeId;
                if (capturedKeys.Add(key))
                {
                    captured.Add(knowledge);
                }
            }

            knowledgeSnapshots = captured;
            return ActionResult.Success("Prepared knowledge references are resident-owned and shareable.");
        }

        private static ActionResult TrySelectShareableKnowledge(
            ResidentRuntimeState speaker,
            ResidentRuntimeState listener,
            out MemoryEntry selected)
        {
            selected = null;
            ActionResult queried = speaker.Memories.Query(
                speaker.ResidentId,
                Array.Empty<string>(),
                MemoryStore.DefaultCapacity,
                out IReadOnlyList<MemoryEntry> candidates);
            if (queried.Failed)
            {
                return queried;
            }

            foreach (MemoryEntry candidate in candidates)
            {
                if (candidate == null || !candidate.IsShareable ||
                    candidate.OwnerResidentId != speaker.ResidentId)
                {
                    continue;
                }

                string rootFactId = string.IsNullOrWhiteSpace(candidate.RootFactId)
                    ? candidate.KnowledgeId
                    : candidate.RootFactId;
                ActionResult checkedKnowledge = listener.Memories.ContainsRootFact(
                    listener.ResidentId,
                    rootFactId,
                    out bool listenerAlreadyKnows);
                if (checkedKnowledge.Failed)
                {
                    return checkedKnowledge;
                }

                if (!listenerAlreadyKnows)
                {
                    selected = candidate;
                    break;
                }
            }

            return ActionResult.Success(selected == null
                ? "No new shareable fact was available for this listener."
                : "The highest-ranked new shareable fact was selected.");
        }

        private void EndAndRelease(
            ActiveConversationRecord record,
            ConversationState terminalState,
            ConversationEndReason reason)
        {
            record.Session.End(terminalState, reason);
            if (record.Session.Utterances.Count > 0)
            {
                double playedAt = record.Session.Utterances[
                    record.Session.Utterances.Count - 1].PlayedAtGameSeconds;
                outcomeApplier.RecordPlayedTranscript(
                    record.Session,
                    playedAt,
                    includeOutcome: false);
            }

            ReleaseResources(record);
        }

        private void ReleaseResources(ActiveConversationRecord record)
        {
            activeSessions.Remove(record.Session.ConversationId);
            if (record.SecondReservation.IsValid)
            {
                reservationService.Release(record.SecondReservation);
            }

            if (record.FirstReservation.IsValid)
            {
                reservationService.Release(record.FirstReservation);
            }

            participantLock.Release(record.ParticipantLease);
        }

        private static void NormalizeParticipantsAndPoints(
            ref ResidentId firstResidentId,
            ref ResidentId secondResidentId,
            ref string firstPoint,
            ref string secondPoint)
        {
            if (firstResidentId.CompareTo(secondResidentId) <= 0)
            {
                return;
            }

            ResidentId residentTemporary = firstResidentId;
            firstResidentId = secondResidentId;
            secondResidentId = residentTemporary;
            string pointTemporary = firstPoint;
            firstPoint = secondPoint;
            secondPoint = pointTemporary;
        }

        private static ActionResult FirstFailure(params ActionResult[] results)
        {
            foreach (ActionResult result in results)
            {
                if (result.Failed)
                {
                    return result;
                }
            }

            return ActionResult.Success();
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
