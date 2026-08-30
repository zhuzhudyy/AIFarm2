using AIFarm.Core;
using AIFarm.Farming;

namespace AIFarm.Npc
{
    public abstract class PlotNpcAction : NpcActionBase
    {
        protected PlotNpcAction(string actionName, int plotNumber, float durationSeconds)
            : base($"{actionName} Plot {plotNumber:00}", plotNumber, durationSeconds)
        {
            PlotNumber = plotNumber;
        }

        public int PlotNumber { get; }

        protected ActionResult TryGetPlot(NpcActionContext context, out FarmPlot plot)
        {
            plot = null;
            if (PlotNumber < 1 || PlotNumber > FarmField.PlotCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Plot number {PlotNumber} is outside the supported range 1-{FarmField.PlotCount}.");
            }

            plot = context.Field.GetPlot(PlotNumber);
            return ActionResult.Success();
        }
    }
}
