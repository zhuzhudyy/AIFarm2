using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class WaitAction : NpcActionBase
    {
        public WaitAction(float durationSeconds)
            : base($"Wait {durationSeconds:0.##}s", null, durationSeconds)
        {
        }

        protected override ActionResult CheckActionPreconditions(NpcActionContext context)
        {
            if (context.Clock.IsPaused && context.Simulation == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Wait failed: the game clock is paused.");
            }

            return ActionResult.Success();
        }

        protected override ActionResult ApplyCompletion(NpcActionContext context)
        {
            if (context.Simulation == null)
            {
                return context.Clock.Advance(DurationSeconds);
            }

            return ActionResult.Success("Wait completed; world time advanced continuously.");
        }
    }
}
