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
