using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;

namespace AIFarm.Npc
{
    public sealed class SowAction : PlotNpcAction
    {
        public SowAction(int plotNumber, float durationSeconds = 0.6f)
            : base("Sow", plotNumber, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            ActionResult lookup = TryGetPlot(context, out FarmPlot plot);
            if (lookup.Failed)
            {
                return lookup;
            }

            if (plot.State != PlotState.Empty)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Sow failed: Plot {PlotNumber:00} is not empty.");
            }

            if (!context.Inventory.Has(InventoryItem.CarrotSeed))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InsufficientResource,
                    "Sow failed: no carrot seed is available.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return context.Field.GetPlot(PlotNumber).Sow(context.Inventory);
        }
    }
}
