using AIFarm.Core;

namespace AIFarm.Npc
{
    public interface ICharacterExpressionService
    {
        bool IsExternalAiRequired { get; }

        ActionResult CreateExpression(
            NpcExpressionTrigger trigger,
            string context,
            int variantIndex,
            out NpcExpression expression);
    }
}
