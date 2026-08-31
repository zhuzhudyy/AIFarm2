using AIFarm.Core;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [CreateAssetMenu(
        fileName = "TownLocationDefinition",
        menuName = "AIFarm/Town/Location Definition")]
    public sealed class TownLocationDefinitionAsset : ScriptableObject
    {
        [SerializeField]
        private string locationIdValue = "location-plaza";

        [SerializeField]
        private string displayName = "广场";

        public TownLocationId LocationId => TownLocationId.TryCreate(
            locationIdValue,
            out TownLocationId parsed)
            ? parsed
            : default;

        public string LocationIdValue => locationIdValue ?? string.Empty;

        public string DisplayName => displayName ?? string.Empty;

        public ActionResult Configure(string stableLocationId, string locationDisplayName)
        {
            if (!TownLocationId.TryCreate(stableLocationId, out TownLocationId locationId) ||
                string.IsNullOrWhiteSpace(locationDisplayName))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Location asset requires a stable ID and display name.");
            }

            locationIdValue = locationId.Value;
            displayName = locationDisplayName.Trim();
            return ActionResult.Success($"Location asset configured for {locationId}.");
        }

        public ActionResult TryCreateDefinition(out TownLocationDefinition definition)
        {
            definition = null;
            TownLocationId locationId = LocationId;
            if (!locationId.IsValid || string.IsNullOrWhiteSpace(displayName))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Location asset contains an invalid ID or display name.");
            }

            definition = new TownLocationDefinition(locationId, displayName);
            return ActionResult.Success();
        }
    }
}
