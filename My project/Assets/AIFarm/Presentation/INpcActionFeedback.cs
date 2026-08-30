using AIFarm.Core;

namespace AIFarm.Presentation
{
    public interface INpcActionFeedback
    {
        bool IsPlaying { get; }

        float Progress { get; }

        ActionResult Begin(string actionName, float durationSeconds);

        ActionResult Tick(float deltaTime, out bool completed);

        ActionResult Cancel();
    }
}
