using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class SocialGraph
    {
        private readonly struct RelationshipKey : IEquatable<RelationshipKey>
        {
            public RelationshipKey(ResidentId ownerResidentId, ResidentId otherResidentId)
            {
                OwnerResidentId = ownerResidentId;
                OtherResidentId = otherResidentId;
            }

            public ResidentId OwnerResidentId { get; }

            public ResidentId OtherResidentId { get; }

            public bool Equals(RelationshipKey other)
            {
                return OwnerResidentId == other.OwnerResidentId &&
                    OtherResidentId == other.OtherResidentId;
            }

            public override bool Equals(object obj)
            {
                return obj is RelationshipKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (OwnerResidentId.GetHashCode() * 397) ^
                        OtherResidentId.GetHashCode();
                }
            }
        }

        private readonly Dictionary<RelationshipKey, RelationshipState> relationships =
            new Dictionary<RelationshipKey, RelationshipState>();

        public SocialGraph(IEnumerable<ResidentId> residentIds)
        {
            if (residentIds == null)
            {
                throw new ArgumentNullException(nameof(residentIds));
            }

            var residents = new List<ResidentId>();
            var uniqueResidents = new HashSet<ResidentId>();
            foreach (ResidentId residentId in residentIds)
            {
                if (!residentId.IsValid || !uniqueResidents.Add(residentId))
                {
                    throw new ArgumentException(
                        "SocialGraph residents must have unique valid IDs.",
                        nameof(residentIds));
                }

                residents.Add(residentId);
            }

            if (residents.Count < 2)
            {
                throw new ArgumentException(
                    "SocialGraph requires at least two residents.",
                    nameof(residentIds));
            }

            residents.Sort();
            for (int ownerIndex = 0; ownerIndex < residents.Count; ownerIndex++)
            {
                for (int otherIndex = 0; otherIndex < residents.Count; otherIndex++)
                {
                    if (ownerIndex == otherIndex)
                    {
                        continue;
                    }

                    ResidentId owner = residents[ownerIndex];
                    ResidentId other = residents[otherIndex];
                    relationships.Add(
                        new RelationshipKey(owner, other),
                        new RelationshipState(owner, other));
                }
            }
        }

        public int RelationshipCount => relationships.Count;

        public ActionResult TryGetRelationship(
            ResidentId ownerResidentId,
            ResidentId otherResidentId,
            out RelationshipState relationship)
        {
            relationship = null;
            if (!ownerResidentId.IsValid || !otherResidentId.IsValid ||
                ownerResidentId == otherResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A relationship query requires two different valid resident IDs.");
            }

            if (!relationships.TryGetValue(
                    new RelationshipKey(ownerResidentId, otherResidentId),
                    out relationship))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "The requested residents are not registered in this SocialGraph.");
            }

            return ActionResult.Success("Relationship resolved.");
        }

        internal ActionResult ApplyChange(
            ResidentId ownerResidentId,
            ResidentId otherResidentId,
            int familiarityDelta,
            int trustDelta,
            string eventId)
        {
            ActionResult resolved = TryGetRelationship(
                ownerResidentId,
                otherResidentId,
                out RelationshipState relationship);
            return resolved.Failed
                ? resolved
                : relationship.ApplyChange(familiarityDelta, trustDelta, eventId);
        }
    }
}
