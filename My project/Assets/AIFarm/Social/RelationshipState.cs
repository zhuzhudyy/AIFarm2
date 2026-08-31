using System;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class RelationshipState
    {
        public const int MinimumValue = -100;
        public const int MaximumValue = 100;

        internal RelationshipState(ResidentId ownerResidentId, ResidentId otherResidentId)
        {
            if (!ownerResidentId.IsValid || !otherResidentId.IsValid ||
                ownerResidentId == otherResidentId)
            {
                throw new ArgumentException(
                    "A relationship requires two different valid resident IDs.");
            }

            OwnerResidentId = ownerResidentId;
            OtherResidentId = otherResidentId;
        }

        public ResidentId OwnerResidentId { get; }

        public ResidentId OtherResidentId { get; }

        public int Familiarity { get; private set; }

        public int Trust { get; private set; }

        public long RelationVersion { get; private set; }

        public string LastChangeEventId { get; private set; } = string.Empty;

        internal ActionResult ApplyChange(
            int familiarityDelta,
            int trustDelta,
            string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A relationship change requires a stable event ID.");
            }

            Familiarity = Clamp(Familiarity + familiarityDelta);
            Trust = Clamp(Trust + trustDelta);
            LastChangeEventId = eventId.Trim();
            RelationVersion++;
            return ActionResult.Success("Relationship updated by deterministic local rules.");
        }

        private static int Clamp(int value)
        {
            return Math.Max(MinimumValue, Math.Min(MaximumValue, value));
        }
    }
}
