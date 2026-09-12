using AIFarm.Core;
using AIFarm.Farming;

namespace AIFarm.Npc
{
    public sealed class WeedAction : PlotNpcAction
    {
        public WeedAction(int plotNumber, float durationSeconds = 0.6f)
            : base("Weed", plotNumber, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            ActionResult lookup = TryGetPlot(context, out FarmPlot plot);
            if (lookup.Failed)
            {
                return lookup;
            }

            if (plot.State != PlotState.Growing || !plot.HasWeeds)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Weed failed: Plot {PlotNumber:00} has no weeds to remove.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return context.Field.GetPlot(PlotNumber).Weed(context.Inventory);
        }
    }
}
