using System;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public readonly struct RelationshipStateSnapshot : IEquatable<RelationshipStateSnapshot>
    {
        public RelationshipStateSnapshot(
            ResidentId ownerResidentId,
            ResidentId otherResidentId,
            int familiarity,
            int trust,
            long relationVersion,
            string lastChangeEventId)
        {
            OwnerResidentId = ownerResidentId;
            OtherResidentId = otherResidentId;
            Familiarity = familiarity;
            Trust = trust;
            RelationVersion = relationVersion;
            LastChangeEventId = lastChangeEventId;
        }

        public ResidentId OwnerResidentId { get; }

        public ResidentId OtherResidentId { get; }

        public int Familiarity { get; }

        public int Trust { get; }

        public long RelationVersion { get; }

        public string LastChangeEventId { get; }

        public bool Equals(RelationshipStateSnapshot other)
        {
            return OwnerResidentId == other.OwnerResidentId &&
                OtherResidentId == other.OtherResidentId &&
                Familiarity == other.Familiarity &&
                Trust == other.Trust &&
                RelationVersion == other.RelationVersion &&
                string.Equals(
                    LastChangeEventId,
                    other.LastChangeEventId,
                    StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RelationshipStateSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = OwnerResidentId.GetHashCode();
                hashCode = (hashCode * 397) ^ OtherResidentId.GetHashCode();
                hashCode = (hashCode * 397) ^ Familiarity;
                hashCode = (hashCode * 397) ^ Trust;
                hashCode = (hashCode * 397) ^ RelationVersion.GetHashCode();
                hashCode = (hashCode * 397) ^
                    (LastChangeEventId == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(LastChangeEventId));
                return hashCode;
            }
        }
    }

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

        public RelationshipStateSnapshot Snapshot => new RelationshipStateSnapshot(
            OwnerResidentId,
            OtherResidentId,
            Familiarity,
            Trust,
            RelationVersion,
            LastChangeEventId);

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

        internal void RestoreValidated(RelationshipStateSnapshot snapshot)
        {
            if (snapshot.OwnerResidentId != OwnerResidentId ||
                snapshot.OtherResidentId != OtherResidentId)
            {
                throw new InvalidOperationException(
                    "A validated relationship snapshot cannot be applied to another directed edge.");
            }

            Familiarity = snapshot.Familiarity;
            Trust = snapshot.Trust;
            RelationVersion = snapshot.RelationVersion;
            LastChangeEventId = snapshot.LastChangeEventId;
        }

        internal void Reset()
        {
            Familiarity = 0;
            Trust = 0;
            RelationVersion = 0L;
            LastChangeEventId = string.Empty;
        }

        private static int Clamp(int value)
        {
            return Math.Max(MinimumValue, Math.Min(MaximumValue, value));
        }
    }
}
