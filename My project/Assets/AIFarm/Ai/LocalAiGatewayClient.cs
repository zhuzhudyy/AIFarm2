using System;
using System.Collections;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public sealed class LocalAiGatewayClient : IAiGatewayClient
    {
        private readonly LocalIntentInterpreter intentInterpreter = new LocalIntentInterpreter();
        private readonly LocalTemplateExpressionService expressionService =
            new LocalTemplateExpressionService();
        private int expressionVariantIndex;

        public AiGatewayMode ConfiguredMode => AiGatewayMode.Local;

        public AiGatewayMode ActiveMode => AiGatewayMode.Local;

        public IEnumerator InterpretCommand(
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed)
        {
            EnsureCallback(completed);
            ActionResult outcome = intentInterpreter.TryInterpret(command, out FarmGoalSpec goal);
            completed(outcome.Succeeded
                ? AiGatewayResult<FarmGoalSpec>.Success(goal, AiGatewayMode.Local, outcome.Message)
                : AiGatewayResult<FarmGoalSpec>.Failure(outcome, AiGatewayMode.Local));
            yield break;
        }

        public IEnumerator GenerateUtterance(
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed)
        {
            EnsureCallback(completed);
            ActionResult outcome = expressionService.CreateExpression(
                trigger,
                context,
                expressionVariantIndex++,
                out NpcExpression expression);
            completed(outcome.Succeeded
                ? AiGatewayResult<NpcExpression>.Success(
                    expression,
                    AiGatewayMode.Local,
                    outcome.Message)
                : AiGatewayResult<NpcExpression>.Failure(outcome, AiGatewayMode.Local));
            yield break;
        }

        public IEnumerator Reflect(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed)
        {
            EnsureCallback(completed);
            if (goal == null)
            {
                completed(AiGatewayResult<NpcReflection>.Failure(
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
                expressionVariantIndex++,
                out NpcExpression expression);
            if (expressionOutcome.Failed)
            {
                completed(AiGatewayResult<NpcReflection>.Failure(
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

        private static void EnsureCallback<T>(Action<AiGatewayResult<T>> completed)
            where T : class
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }
        }
    }
}
