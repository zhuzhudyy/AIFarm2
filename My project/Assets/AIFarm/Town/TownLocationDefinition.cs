using System;

namespace AIFarm.Town
{
    public sealed class TownLocationDefinition
    {
        public TownLocationDefinition(TownLocationId locationId, string displayName)
        {
            if (!locationId.IsValid)
            {
                throw new ArgumentException(
                    "A town location requires a valid TownLocationId.",
                    nameof(locationId));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A town location requires a display name.",
                    nameof(displayName));
            }

            LocationId = locationId;
            DisplayName = displayName.Trim();
        }

        public TownLocationId LocationId { get; }

        public string DisplayName { get; }
    }
}
