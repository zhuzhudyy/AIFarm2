using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly List<RelationshipKey> orderedKeys =
            new List<RelationshipKey>();

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
                    var key = new RelationshipKey(owner, other);
                    relationships.Add(
                        key,
                        new RelationshipState(owner, other));
                    orderedKeys.Add(key);
                }
            }
        }

        public int RelationshipCount => relationships.Count;

        public IReadOnlyList<RelationshipStateSnapshot> Snapshots => CaptureSnapshots();

        public IReadOnlyList<RelationshipStateSnapshot> CaptureSnapshots()
        {
            var snapshots = new RelationshipStateSnapshot[orderedKeys.Count];
            for (int index = 0; index < orderedKeys.Count; index++)
            {
                snapshots[index] = relationships[orderedKeys[index]].Snapshot;
            }

            return new ReadOnlyCollection<RelationshipStateSnapshot>(snapshots);
        }

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

        public ActionResult Restore(IEnumerable<RelationshipStateSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Relationship snapshots are required.");
            }

            var validated = new Dictionary<RelationshipKey, RelationshipStateSnapshot>();
            try
            {
                foreach (RelationshipStateSnapshot snapshot in snapshots)
                {
                    ActionResult validation = ValidateSnapshot(snapshot, validated);
                    if (validation.Failed)
                    {
                        return validation;
                    }
                }
            }
            catch (Exception exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Relationship snapshots could not be enumerated: {exception.Message}");
            }

            if (validated.Count != relationships.Count)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Relationship restore requires exactly {relationships.Count} directed edges, " +
                    $"but received {validated.Count}.");
            }

            for (int index = 0; index < orderedKeys.Count; index++)
            {
                RelationshipKey key = orderedKeys[index];
                relationships[key].RestoreValidated(validated[key]);
            }

            return ActionResult.Success("All directed relationships restored atomically.");
        }

        public ActionResult Reset()
        {
            for (int index = 0; index < orderedKeys.Count; index++)
            {
                relationships[orderedKeys[index]].Reset();
            }

            return ActionResult.Success("All directed relationships reset.");
        }

        private ActionResult ValidateSnapshot(
            RelationshipStateSnapshot snapshot,
            IDictionary<RelationshipKey, RelationshipStateSnapshot> validated)
        {
            if (!snapshot.OwnerResidentId.IsValid ||
                !snapshot.OtherResidentId.IsValid ||
                snapshot.OwnerResidentId == snapshot.OtherResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Every relationship snapshot requires two different valid resident IDs.");
            }

            var key = new RelationshipKey(
                snapshot.OwnerResidentId,
                snapshot.OtherResidentId);
            if (!relationships.ContainsKey(key))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A relationship snapshot references residents outside this SocialGraph.");
            }

            if (validated.ContainsKey(key))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A directed relationship edge may appear only once in a restore snapshot.");
            }

            if (snapshot.Familiarity < RelationshipState.MinimumValue ||
                snapshot.Familiarity > RelationshipState.MaximumValue ||
                snapshot.Trust < RelationshipState.MinimumValue ||
                snapshot.Trust > RelationshipState.MaximumValue)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Relationship values must be between {RelationshipState.MinimumValue} " +
                    $"and {RelationshipState.MaximumValue}.");
            }

            if (snapshot.RelationVersion < 0L)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Relationship versions cannot be negative.");
            }

            if (snapshot.LastChangeEventId == null ||
                !string.Equals(
                    snapshot.LastChangeEventId,
                    snapshot.LastChangeEventId.Trim(),
                    StringComparison.Ordinal))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Relationship event IDs cannot be null or padded with whitespace.");
            }

            if (snapshot.RelationVersion == 0L)
            {
                if (snapshot.Familiarity != 0 ||
                    snapshot.Trust != 0 ||
                    snapshot.LastChangeEventId.Length != 0)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "An unchanged relationship must use zero values and no last event ID.");
                }
            }
            else if (snapshot.LastChangeEventId.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A changed relationship requires its last change event ID.");
            }

            validated.Add(key, snapshot);
            return ActionResult.Success();
        }
    }
}
