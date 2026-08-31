using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class ActionQueue
    {
        private readonly Queue<INpcAction> actions = new Queue<INpcAction>();

        public int Count => actions.Count;

        public bool IsEmpty => actions.Count == 0;

        public IReadOnlyList<INpcAction> GetSnapshot()
        {
            return new ReadOnlyCollection<INpcAction>(actions.ToArray());
        }

        public ActionResult Enqueue(INpcAction action)
        {
            if (action == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Cannot enqueue a null NPC action.");
            }

            actions.Enqueue(action);
            return ActionResult.Success($"Queued {action.DisplayName}.");
        }

        public ActionResult TryPeek(out INpcAction action)
        {
            if (actions.Count == 0)
            {
                action = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The NPC action queue is empty.");
            }

            action = actions.Peek();
            return ActionResult.Success();
        }

        public ActionResult TryDequeue(out INpcAction action)
        {
            if (actions.Count == 0)
            {
                action = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The NPC action queue is empty.");
            }

            action = actions.Dequeue();
            return ActionResult.Success($"Dequeued {action.DisplayName}.");
        }

        public ActionResult Clear()
        {
            actions.Clear();
            return ActionResult.Success("NPC action queue cleared.");
        }
    }
}
