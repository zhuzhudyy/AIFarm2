using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Town
{
    public sealed class TownInteractionPointDefinition
    {
        public TownInteractionPointDefinition(
            string interactionPointId,
            TownLocationId locationId)
        {
            if (string.IsNullOrWhiteSpace(interactionPointId))
            {
                throw new ArgumentException(
                    "An interaction point requires a stable ID.",
                    nameof(interactionPointId));
            }

            if (!locationId.IsValid)
            {
                throw new ArgumentException(
                    "An interaction point requires a valid location.",
                    nameof(locationId));
            }

            InteractionPointId = interactionPointId.Trim();
            LocationId = locationId;
        }

        public string InteractionPointId { get; }

        public TownLocationId LocationId { get; }
    }

    public sealed class TownLocationPointDirectory
    {
        private readonly Dictionary<string, TownInteractionPointDefinition> pointsById =
            new Dictionary<string, TownInteractionPointDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<TownLocationId, List<string>> pointIdsByLocation =
            new Dictionary<TownLocationId, List<string>>();

        public int Count => pointsById.Count;

        public ActionResult Register(TownInteractionPointDefinition point)
        {
            if (point == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "An interaction point definition is required.");
            }

            if (pointsById.ContainsKey(point.InteractionPointId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Interaction point '{point.InteractionPointId}' is already registered.");
            }

            pointsById.Add(point.InteractionPointId, point);
            if (!pointIdsByLocation.TryGetValue(point.LocationId, out List<string> pointIds))
            {
                pointIds = new List<string>();
                pointIdsByLocation.Add(point.LocationId, pointIds);
            }

            pointIds.Add(point.InteractionPointId);
            pointIds.Sort(StringComparer.Ordinal);
            return ActionResult.Success();
        }

        public ActionResult TryGetPointIds(
            TownLocationId locationId,
            out IReadOnlyList<string> pointIds)
        {
            pointIds = Array.Empty<string>();
            if (!locationId.IsValid ||
                !pointIdsByLocation.TryGetValue(locationId, out List<string> found) ||
                found.Count == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Location '{locationId}' has no registered arrival points.");
            }

            pointIds = new ReadOnlyCollection<string>(found);
            return ActionResult.Success();
        }
    }
}
