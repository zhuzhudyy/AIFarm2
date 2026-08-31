using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class ResidentRegistry
    {
        private readonly Dictionary<ResidentId, ResidentDefinition> definitions =
            new Dictionary<ResidentId, ResidentDefinition>();
        private readonly Dictionary<ResidentId, ResidentRuntimeState> runtimeStates =
            new Dictionary<ResidentId, ResidentRuntimeState>();
        private readonly List<ResidentId> residentIds = new List<ResidentId>();

        public int Count => residentIds.Count;

        public IReadOnlyList<ResidentId> ResidentIds =>
            new ReadOnlyCollection<ResidentId>(residentIds);

        public ActionResult Register(
            ResidentDefinition definition,
            ResidentRuntimeState runtimeState)
        {
            if (definition == null || runtimeState == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A resident definition and runtime state are required.");
            }

            ResidentId residentId = definition.ResidentId;
            if (runtimeState.ResidentId != residentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident definition and runtime state IDs do not match.");
            }

            if (definitions.ContainsKey(residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"ResidentId '{residentId}' is already registered.");
            }

            definitions.Add(residentId, definition);
            runtimeStates.Add(residentId, runtimeState);
            residentIds.Add(residentId);
            residentIds.Sort();
            return ActionResult.Success($"Resident '{residentId}' registered.");
        }

        public ActionResult TryGetDefinition(
            ResidentId residentId,
            out ResidentDefinition definition)
        {
            if (!residentId.IsValid || !definitions.TryGetValue(residentId, out definition))
            {
                definition = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not registered.");
            }

            return ActionResult.Success($"Resident definition '{residentId}' resolved.");
        }

        public ActionResult TryGetRuntimeState(
            ResidentId residentId,
            out ResidentRuntimeState runtimeState)
        {
            if (!residentId.IsValid || !runtimeStates.TryGetValue(residentId, out runtimeState))
            {
                runtimeState = null;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not registered.");
            }

            return ActionResult.Success($"Resident runtime '{residentId}' resolved.");
        }

        public ActionResult ReplaceRuntimeState(
            ResidentId residentId,
            ResidentRuntimeState runtimeState)
        {
            if (!residentId.IsValid || !runtimeStates.ContainsKey(residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not registered.");
            }

            if (runtimeState == null || runtimeState.ResidentId != residentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Replacement runtime state must have the registered ResidentId.");
            }

            runtimeStates[residentId] = runtimeState;
            return ActionResult.Success($"Resident runtime '{residentId}' replaced.");
        }
    }
}
