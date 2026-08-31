using System;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class ConversationOutcomeApplier
    {
        public const int HelpfulTrustIncrease = 8;

        private readonly ResidentRegistry residentRegistry;
        private readonly SocialGraph socialGraph;

        public ConversationOutcomeApplier(
            ResidentRegistry registry,
            SocialGraph graph)
        {
            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
            socialGraph = graph ?? throw new ArgumentNullException(nameof(graph));
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

            string firstMemory = CreateMemoryText(
                firstDefinition,
                secondDefinition,
                outcome);
            string secondMemory = CreateMemoryText(
                secondDefinition,
                firstDefinition,
                outcome);
            ActionResult firstStored = firstRuntime.Memories.AddObservation(
                session.FirstResidentId,
                gameSeconds,
                firstMemory,
                6,
                WorldEventKind.ConversationCompleted,
                out MemoryEntry _);
            ActionResult secondStored = secondRuntime.Memories.AddObservation(
                session.SecondResidentId,
                gameSeconds,
                secondMemory,
                6,
                WorldEventKind.ConversationCompleted,
                out MemoryEntry _);
            return firstStored.Failed ? firstStored : secondStored;
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
            ResidentDefinition owner,
            ResidentDefinition other,
            ConversationOutcome outcome)
        {
            return $"{owner.DisplayName}与{other.DisplayName}完成了一次双人交谈；结果是{outcome}。";
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
