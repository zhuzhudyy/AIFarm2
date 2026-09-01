using System;
using System.Collections.Generic;
using System.Text;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class ConversationOutcomeApplier
    {
        public const int HelpfulTrustIncrease = 8;

        private readonly ResidentRegistry residentRegistry;
        private readonly SocialGraph socialGraph;
        private readonly ConversationSummaryService summaryService;
        private readonly HashSet<ConversationId> recordedTranscripts =
            new HashSet<ConversationId>();

        public ConversationOutcomeApplier(
            ResidentRegistry registry,
            SocialGraph graph,
            ConversationSummaryService conversationSummaries = null)
        {
            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            socialGraph = graph ?? throw new ArgumentNullException(nameof(graph));
            summaryService = conversationSummaries ?? new ConversationSummaryService(registry);
        }

        public ActionResult Apply(
            ConversationSession session,
            ConversationOutcome outcome,
            double gameSeconds)
        {
            if (session == null || session.State != ConversationState.Completed ||
                session.Outcome != outcome ||
                !Enum.IsDefined(typeof(ConversationOutcome), outcome) ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Only a valid completed conversation outcome can be applied.");
            }

            ActionResult firstDefinitionResult = residentRegistry.TryGetDefinition(
                session.FirstResidentId,
                out _);
            ActionResult secondDefinitionResult = residentRegistry.TryGetDefinition(
                session.SecondResidentId,
                out _);
            ActionResult firstRuntimeResult = residentRegistry.TryGetRuntimeState(
                session.FirstResidentId,
                out _);
            ActionResult secondRuntimeResult = residentRegistry.TryGetRuntimeState(
                session.SecondResidentId,
                out _);
            ActionResult firstRelationshipResult = socialGraph.TryGetRelationship(
                session.FirstResidentId,
                session.SecondResidentId,
                out _);
            ActionResult secondRelationshipResult = socialGraph.TryGetRelationship(
                session.SecondResidentId,
                session.FirstResidentId,
                out _);
            ActionResult validationFailure = FirstFailure(
                firstDefinitionResult,
                secondDefinitionResult,
                firstRuntimeResult,
                secondRuntimeResult,
                firstRelationshipResult,
                secondRelationshipResult);
            if (validationFailure.Failed)
            {
                return validationFailure;
            }

            GetDeltas(outcome, out int familiarityDelta, out int trustDelta);
            string eventId = session.ConversationId.Value;
            ActionResult firstRelationship = socialGraph.ApplyChange(
                session.FirstResidentId,
                session.SecondResidentId,
                familiarityDelta,
                trustDelta,
                eventId);
            ActionResult secondRelationship = socialGraph.ApplyChange(
                session.SecondResidentId,
                session.FirstResidentId,
                familiarityDelta,
                trustDelta,
                eventId);
            if (firstRelationship.Failed || secondRelationship.Failed)
            {
                return firstRelationship.Failed ? firstRelationship : secondRelationship;
            }

            return RecordPlayedTranscript(session, gameSeconds, includeOutcome: true);
        }

        internal ActionResult RecordPlayedTranscript(
            ConversationSession session,
            double gameSeconds,
            bool includeOutcome)
        {
            if (session != null && recordedTranscripts.Contains(session.ConversationId))
            {
                return ActionResult.Success("Played conversation transcript was already recorded.");
            }

            if (session == null || session.Utterances.Count == 0 ||
                !IsFiniteNonNegative(gameSeconds) ||
                (includeOutcome &&
                    (session.State != ConversationState.Completed || !session.Outcome.HasValue)))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A played transcript requires actual utterances and valid completion state.");
            }

            if (includeOutcome)
            {
                ActionResult summarized = summaryService.RecordCompletedConversation(
                    session,
                    gameSeconds);
                if (summarized.Succeeded)
                {
                    recordedTranscripts.Add(session.ConversationId);
                }

                return summarized;
            }

            ActionResult firstDefinitionResult = residentRegistry.TryGetDefinition(
                session.FirstResidentId,
                out ResidentDefinition firstDefinition);
            ActionResult secondDefinitionResult = residentRegistry.TryGetDefinition(
                session.SecondResidentId,
                out ResidentDefinition secondDefinition);
            ActionResult firstRuntimeResult = residentRegistry.TryGetRuntimeState(
                session.FirstResidentId,
                out ResidentRuntimeState firstRuntime);
            ActionResult secondRuntimeResult = residentRegistry.TryGetRuntimeState(
                session.SecondResidentId,
                out ResidentRuntimeState secondRuntime);
            ActionResult validationFailure = FirstFailure(
                firstDefinitionResult,
                secondDefinitionResult,
                firstRuntimeResult,
                secondRuntimeResult);
            if (validationFailure.Failed)
            {
                return validationFailure;
            }

            string firstMemory = CreateMemoryText(
                session,
                firstDefinition,
                secondDefinition,
                includeOutcome: false);
            string secondMemory = CreateMemoryText(
                session,
                secondDefinition,
                firstDefinition,
                includeOutcome: false);
            ActionResult firstStored = firstRuntime.Memories.AddObservation(
                session.FirstResidentId,
                gameSeconds,
                firstMemory,
                6,
                WorldEventKind.ConversationInterrupted,
                MemorySourceKind.Conversation,
                sourceEventId: session.ConversationId.Value,
                rootFactId: null,
                parentKnowledgeId: null,
                immediateSourceResidentId: session.SecondResidentId,
                tags: new[] { "conversation-fragment" },
                isShareable: false,
                out MemoryEntry _);
            ActionResult secondStored = secondRuntime.Memories.AddObservation(
                session.SecondResidentId,
                gameSeconds,
                secondMemory,
                6,
                WorldEventKind.ConversationInterrupted,
                MemorySourceKind.Conversation,
                sourceEventId: session.ConversationId.Value,
                rootFactId: null,
                parentKnowledgeId: null,
                immediateSourceResidentId: session.FirstResidentId,
                tags: new[] { "conversation-fragment" },
                isShareable: false,
                out MemoryEntry _);
            ActionResult stored = firstStored.Failed ? firstStored : secondStored;
            if (stored.Succeeded)
            {
                recordedTranscripts.Add(session.ConversationId);
            }

            return stored;
        }

        public static void GetDeltas(
            ConversationOutcome outcome,
            out int familiarityDelta,
            out int trustDelta)
        {
            switch (outcome)
            {
                case ConversationOutcome.Positive:
                    familiarityDelta = 4;
                    trustDelta = 3;
                    break;
                case ConversationOutcome.Neutral:
                    familiarityDelta = 1;
                    trustDelta = 0;
                    break;
                case ConversationOutcome.Helpful:
                    familiarityDelta = 5;
                    trustDelta = HelpfulTrustIncrease;
                    break;
                case ConversationOutcome.Awkward:
                    familiarityDelta = 2;
                    trustDelta = -2;
                    break;
                case ConversationOutcome.Conflict:
                    familiarityDelta = 1;
                    trustDelta = -6;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome));
            }
        }

        private static string CreateMemoryText(
            ConversationSession session,
            ResidentDefinition owner,
            ResidentDefinition other,
            bool includeOutcome)
        {
            var text = new StringBuilder();
            text.Append(owner.DisplayName)
                .Append("与")
                .Append(other.DisplayName)
                .Append(includeOutcome ? "的对话：" : "未完成的对话：");
            foreach (ConversationUtterance utterance in session.Utterances)
            {
                ResidentDefinition speaker = utterance.SpeakerResidentId == owner.ResidentId
                    ? owner
                    : other;
                if (text.Length > 0 && text[text.Length - 1] != '：')
                {
                    text.Append('；');
                }

                text.Append(speaker.DisplayName)
                    .Append('「')
                    .Append(utterance.Text)
                    .Append('」');
            }

            if (includeOutcome && session.Outcome.HasValue)
            {
                text.Append("；结果是").Append(session.Outcome.Value).Append('。');
            }

            string bounded = text.ToString();
            return bounded.Length <= MemoryStore.MaximumTextLength
                ? bounded
                : bounded.Substring(0, MemoryStore.MaximumTextLength);
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
    }
}
