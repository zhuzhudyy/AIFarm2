using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class MoveToPlotAction : PlotNpcAction
    {
        public MoveToPlotAction(int plotNumber, float durationSeconds = 0.15f)
            : base("Move To", plotNumber, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            return TryGetPlot(context, out _);
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            return ActionResult.Success($"NPC reached Plot {PlotNumber:00}.");
        }
    }
}
