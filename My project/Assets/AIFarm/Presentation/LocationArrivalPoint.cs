using AIFarm.Core;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class LocationArrivalPoint : MonoBehaviour
    {
        [SerializeField]
        private TownLocationDefinitionAsset locationDefinition;

        [SerializeField]
        private string interactionPointId;

        [SerializeField]
        private Transform facingTarget;

        public TownLocationDefinitionAsset LocationDefinition => locationDefinition;

        public TownLocationId LocationId => locationDefinition == null
            ? default
            : locationDefinition.LocationId;

        public string InteractionPointId => interactionPointId ?? string.Empty;

        public Vector3 Position => transform.position;

        public Vector3 FacingPosition => facingTarget == null
            ? transform.position
            : facingTarget.position;

        public ActionResult Configure(
            TownLocationDefinitionAsset location,
            string stableInteractionPointId,
            Transform targetToFace)
        {
            if (location == null || !location.LocationId.IsValid ||
                string.IsNullOrWhiteSpace(stableInteractionPointId) || targetToFace == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "An arrival point requires a location, stable point ID, and facing target.");
            }

            locationDefinition = location;
            interactionPointId = stableInteractionPointId.Trim();
            facingTarget = targetToFace;
            return ActionResult.Success(
                $"Arrival point '{interactionPointId}' configured for {location.LocationId}.");
        }

        public TownInteractionPointDefinition CreateDefinition()
        {
            return new TownInteractionPointDefinition(InteractionPointId, LocationId);
        }
    }
}
