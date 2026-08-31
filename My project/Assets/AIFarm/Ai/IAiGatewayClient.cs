using System;
using System.Collections;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public interface IAiGatewayClient
    {
        AiGatewayMode ConfiguredMode { get; }

        AiGatewayMode ActiveMode { get; }

        IEnumerator InterpretCommand(
            string command,
            Action<AiGatewayResult<FarmGoalSpec>> completed);

        IEnumerator GenerateUtterance(
            NpcExpressionTrigger trigger,
            string context,
            Action<AiGatewayResult<NpcExpression>> completed);

        IEnumerator Reflect(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            Action<AiGatewayResult<NpcReflection>> completed);
    }
}
