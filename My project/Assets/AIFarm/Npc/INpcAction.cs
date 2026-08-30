using AIFarm.Core;

namespace AIFarm.Npc
{
    public interface INpcAction
    {
        string DisplayName { get; }

        int? TargetPlotNumber { get; }

        float DurationSeconds { get; }

        ActionResult CheckPreconditions(NpcActionContext context);

        ActionResult Complete(NpcActionContext context);
    }
}
