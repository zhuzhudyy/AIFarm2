using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;

namespace AIFarm.Npc
{
    public sealed class WaterAction : PlotNpcAction
    {
        public WaterAction(int plotNumber, float durationSeconds = 0.6f)
            : base("Water", plotNumber, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            ActionResult lookup = TryGetPlot(context, out FarmPlot plot);
            if (lookup.Failed)
            {
                return lookup;
            }

            if (plot.State != PlotState.Growing)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Water failed: Plot {PlotNumber:00} has no growing crop.");
            }

            if (plot.WaterLevel >= FarmPlot.MaximumWaterLevel)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Water failed: Plot {PlotNumber:00} is already fully watered.");
            }

            if (!context.Inventory.Has(InventoryItem.Water))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InsufficientResource,
                    "Water failed: no water is available.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return context.Field.GetPlot(PlotNumber).Water(context.Inventory);
        }
    }
}
