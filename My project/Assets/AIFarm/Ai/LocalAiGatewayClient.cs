using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public sealed class LocalAiGatewayClient : IAiGatewayClient
    {
        private readonly LocalIntentInterpreter intentInterpreter = new LocalIntentInterpreter();
        private readonly LocalTemplateExpressionService expressionService =
            new LocalTemplateExpressionService();
        private readonly Dictionary<ResidentId, int> expressionVariantIndexes =
            new Dictionary<ResidentId, int>();

        public AiGatewayMode ConfiguredMode => AiGatewayMode.Local;

        public AiGatewayMode ActiveMode => AiGatewayMode.Local;

        public IEnumerator InterpretCommand(
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            return InterpretCommand(ResidentIds.Yaya, command, completed);
        }

        public IEnumerator InterpretCommand(
            ResidentId residentId,
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            ActionResult outcome = intentInterpreter.TryInterpret(command, out FarmGoalSpec goal);
            completed(outcome.Succeeded
                ? AiGatewayResult<FarmGoalSpec>.Success(
                    residentId,
                    goal,
                    AiGatewayMode.Local,
                    outcome.Message)
                : AiGatewayResult<FarmGoalSpec>.Failure(
                    residentId,
                    outcome,
                    AiGatewayMode.Local));
            yield break;
        }

        public IEnumerator GenerateUtterance(
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            return GenerateUtterance(ResidentIds.Yaya, trigger, context, completed);
        }

        public IEnumerator GenerateUtterance(
            ResidentId residentId,
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            ActionResult outcome = expressionService.CreateExpression(
                trigger,
                context,
                NextExpressionVariant(residentId),
                out NpcExpression expression);
            completed(outcome.Succeeded
                ? AiGatewayResult<NpcExpression>.Success(
                    residentId,
                    expression,
                    AiGatewayMode.Local,
                    outcome.Message)
                : AiGatewayResult<NpcExpression>.Failure(
                    residentId,
                    outcome,
                    AiGatewayMode.Local));
            yield break;
        }

        public IEnumerator Reflect(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            return Reflect(
                ResidentIds.Yaya,
                goal,
                outcome,
                eventSummary,
                completed);
        }

        public IEnumerator Reflect(
            ResidentId residentId,
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            EnsureResidentId(residentId);
            EnsureCallback(completed);
            if (goal == null)
            {
                completed(AiGatewayResult<NpcReflection>.Failure(
                    residentId,
                    ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "Local reflection requires a farm goal."),
                    AiGatewayMode.Local));
                yield break;
            }

            NpcExpressionTrigger trigger = SelectReflectionTrigger(outcome);
            ActionResult expressionOutcome = expressionService.CreateExpression(
                trigger,
                eventSummary,
                NextExpressionVariant(residentId),
                out NpcExpression expression);
            if (expressionOutcome.Failed)
            {
                completed(AiGatewayResult<NpcReflection>.Failure(
                    residentId,
                    expressionOutcome,
                    AiGatewayMode.Local));
                yield break;
            }

            var reflection = new NpcReflection(
                goal.GoalId,
                outcome,
                expression.Mood,
                expression.Emoji,
                expression.Text);
            completed(AiGatewayResult<NpcReflection>.Success(
                residentId,
                reflection,
                AiGatewayMode.Local,
                "Local NPC reflection generated."));
        }

        public IEnumerator GenerateConversationScript(
            ConversationScriptRequest request,
            Action<AiGatewayResult<ConversationScriptSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EnsureCallback(completed);
            completed(AiGatewayResult<ConversationScriptSpec>.Failure(
                request.ResidentId,
                ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "Conversation scripts use LocalConversationTemplateService when remote AI is unavailable."),
                AiGatewayMode.Local));
            yield break;
        }

        public IEnumerator DecideResident(
            ResidentDecisionRequest request,
            Action<AiGatewayResult<ResidentDecisionSpec>> completed)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EnsureCallback(completed);
            bool canProposeHarvestDinner =
                request.IsAllowedIntent(ResidentHighLevelIntents.ProposeTownEvent) &&
                HasHarvestDinnerConversationMemory(request);
            string intent = canProposeHarvestDinner
                ? ResidentHighLevelIntents.ProposeTownEvent
                : FirstSafeNonProposalIntent(request.AllowedIntents);
            if (intent == null)
            {
                completed(AiGatewayResult<ResidentDecisionSpec>.Failure(
                    request.ResidentId,
                    ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "propose_town_event requires a shareable carrot-harvest fact learned from another resident."),
                    AiGatewayMode.Local));
                yield break;
            }

            ResidentId? targetResidentId = RequiresTargetResident(intent) &&
                request.AllowedTargetResidentIds.Count > 0
                    ? (ResidentId?)request.AllowedTargetResidentIds[0]
                    : null;
            var decision = new ResidentDecisionSpec(
                request.ResidentId,
                intent,
                targetResidentId,
                canProposeHarvestDinner
                    ? "我从对话中得知了胡萝卜收获消息，可以提议全镇参加收获晚餐。"
                    : "本地规则从本次请求的允许集合中选择安全的高层意图。",
                "mock");
            completed(AiGatewayResult<ResidentDecisionSpec>.Success(
                request.ResidentId,
                decision,
                AiGatewayMode.Local,
                "Deterministic local resident decision selected."));
            yield break;
        }

        private static string FirstSafeNonProposalIntent(
            IReadOnlyList<string> allowedIntents)
        {
            foreach (string intent in allowedIntents)
            {
                if (!string.Equals(
                        intent,
                        ResidentHighLevelIntents.ProposeTownEvent,
                        StringComparison.Ordinal))
                {
                    return intent;
                }
            }

            return null;
        }

        private static bool HasHarvestDinnerConversationMemory(
            ResidentDecisionRequest request)
        {
            foreach (ResidentMemorySnapshot memory in request.Context.RelevantMemories)
            {
                if (memory.OwnerResidentId == request.ResidentId &&
                    memory.IsShareable &&
                    memory.ImmediateSourceResidentId.HasValue &&
                    memory.ImmediateSourceResidentId.Value.IsValid &&
                    memory.ImmediateSourceResidentId.Value != request.ResidentId &&
                    ContainsTag(memory.Tags, "harvest") &&
                    ContainsTag(memory.Tags, "carrot"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTag(IReadOnlyList<string> tags, string expected)
        {
            foreach (string tag in tags)
            {
                if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresTargetResident(string intent)
        {
            return string.Equals(intent, "RequestConversation", StringComparison.Ordinal) ||
                string.Equals(intent, "ContinueConversation", StringComparison.Ordinal) ||
                string.Equals(intent, "ShareKnownFact", StringComparison.Ordinal);
        }

        private static NpcExpressionTrigger SelectReflectionTrigger(
            NpcReflectionOutcome outcome)
        {
            switch (outcome)
            {
                case NpcReflectionOutcome.Completed:
                    return NpcExpressionTrigger.GoalCompleted;
                case NpcReflectionOutcome.Failed:
                    return NpcExpressionTrigger.ActionFailed;
                default:
                    return NpcExpressionTrigger.WaitingForGrowth;
            }
        }

        private int NextExpressionVariant(ResidentId residentId)
        {
            expressionVariantIndexes.TryGetValue(residentId, out int variantIndex);
            expressionVariantIndexes[residentId] = variantIndex + 1;
            return variantIndex;
        }

        private static void EnsureCallback<T>(Action<AiGatewayResult<T>> completed)
            where T : class
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }
        }

        private static void EnsureResidentId(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException(
                    "AI gateway requests require a valid ResidentId.",
                    nameof(residentId));
            }
        }
    }
}
