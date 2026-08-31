using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;

namespace AIFarm.Npc
{
    public sealed class FertilizeAction : PlotNpcAction
    {
        public FertilizeAction(int plotNumber, float durationSeconds = 0.7f)
            : base("Fertilize", plotNumber, durationSeconds)
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
                    $"Fertilize failed: Plot {PlotNumber:00} must contain a growing crop.");
            }

            if (plot.WaterLevel < FarmPlot.RequiredWaterLevel)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Fertilize failed: Plot {PlotNumber:00} must be watered first.");
            }

            if (plot.IsFertilized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Fertilize failed: Plot {PlotNumber:00} is already fertilized.");
            }

            if (!context.Inventory.Has(InventoryItem.Fertilizer))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InsufficientResource,
                    "Fertilize failed: no fertilizer is available.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return context.Field.GetPlot(PlotNumber).Fertilize(context.Inventory);
        }
    }
}
