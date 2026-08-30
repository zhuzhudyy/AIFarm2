using AIFarm.Core;

namespace AIFarm.Presentation
{
    public interface INpcNavigationDriver
    {
        bool IsMoving { get; }

        ActionResult BeginMove(int plotNumber);

        ActionResult Tick(float deltaTime, out bool arrived);

        ActionResult CancelMove();
    }
}
