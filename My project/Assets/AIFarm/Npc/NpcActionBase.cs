using AIFarm.Core;

namespace AIFarm.Npc
{
    public abstract class NpcActionBase : INpcAction
    {
        protected NpcActionBase(string displayName, int? targetPlotNumber, float durationSeconds)
        {
            DisplayName = displayName ?? string.Empty;
            TargetPlotNumber = targetPlotNumber;
            DurationSeconds = durationSeconds;
        }

        public string DisplayName { get; }

        public int? TargetPlotNumber { get; }

        public float DurationSeconds { get; }

        public ActionResult CheckPreconditions(NpcActionContext context)
        {
            if (context == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"{DisplayName} requires an action context.");
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "An NPC action requires a display name.");
            }

            if (float.IsNaN(DurationSeconds) || float.IsInfinity(DurationSeconds) || DurationSeconds <= 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"{DisplayName} requires a finite, positive duration.");
            }

            return CheckActionPreconditions(context);
        }

        public ActionResult Complete(NpcActionContext context)
        {
            ActionResult preconditions = CheckPreconditions(context);
            if (preconditions.Failed)
            {
                return preconditions;
            }

            return ApplyCompletion(context);
        }

        protected abstract ActionResult CheckActionPreconditions(NpcActionContext context);

        protected abstract ActionResult ApplyCompletion(NpcActionContext context);
    }
}
