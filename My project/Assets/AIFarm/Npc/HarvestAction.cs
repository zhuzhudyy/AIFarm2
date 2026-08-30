using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;

namespace AIFarm.Npc
{
    public sealed class HarvestAction : PlotNpcAction
    {
        public HarvestAction(int plotNumber, float durationSeconds = 0.8f)
            : base("Harvest", plotNumber, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            ActionResult lookup = TryGetPlot(context, out FarmPlot plot);
            if (lookup.Failed)
            {
                return lookup;
            }

            if (plot.State != PlotState.Mature || plot.Crop != CropType.Carrot)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Harvest failed: Plot {PlotNumber:00} does not contain a mature carrot.");
            }

            if (context.Inventory.GetCount(InventoryItem.Carrot) == int.MaxValue)
            {
                return ActionResult.Failure(
                    ActionFailureReason.CapacityExceeded,
                    "Harvest failed: the carrot inventory is full.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return context.Field.GetPlot(PlotNumber).Harvest(context.Inventory);
        }
    }
}
