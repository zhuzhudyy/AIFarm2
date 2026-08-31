using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public readonly struct ConversationParticipantLease
    {
        internal ConversationParticipantLease(
            ConversationId conversationId,
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            long revision)
        {
            ConversationId = conversationId;
            FirstResidentId = firstResidentId;
            SecondResidentId = secondResidentId;
            Revision = revision;
        }

        public ConversationId ConversationId { get; }

        public ResidentId FirstResidentId { get; }

        public ResidentId SecondResidentId { get; }

        public long Revision { get; }

        public bool IsValid => ConversationId.IsValid && FirstResidentId.IsValid &&
            SecondResidentId.IsValid && FirstResidentId != SecondResidentId && Revision > 0;
    }

    public sealed class ConversationParticipantLock
    {
        private readonly struct LockRecord
        {
            public LockRecord(ConversationId conversationId, long revision)
            {
                ConversationId = conversationId;
                Revision = revision;
            }

            public ConversationId ConversationId { get; }

            public long Revision { get; }
        }

        private readonly Dictionary<ResidentId, LockRecord> locksByResident =
            new Dictionary<ResidentId, LockRecord>();
        private long nextRevision;

        public int LockedResidentCount => locksByResident.Count;

        public ActionResult TryAcquire(
            ConversationId conversationId,
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            out ConversationParticipantLease lease)
        {
            lease = default;
            if (!conversationId.IsValid || !firstResidentId.IsValid ||
                !secondResidentId.IsValid || firstResidentId == secondResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A participant lock requires one conversation and two different residents.");
            }

            Normalize(ref firstResidentId, ref secondResidentId);
            if (locksByResident.ContainsKey(firstResidentId) ||
                locksByResident.ContainsKey(secondResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "At least one resident is already participating in a conversation.");
            }

            long revision = ++nextRevision;
            var record = new LockRecord(conversationId, revision);
            locksByResident.Add(firstResidentId, record);
            locksByResident.Add(secondResidentId, record);
            lease = new ConversationParticipantLease(
                conversationId,
                firstResidentId,
                secondResidentId,
                revision);
            return ActionResult.Success("Both conversation participants were locked atomically.");
        }

        public ActionResult Release(ConversationParticipantLease lease)
        {
            if (!lease.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid participant lease is required.");
            }

            ReleaseIfMatching(lease.FirstResidentId, lease);
            ReleaseIfMatching(lease.SecondResidentId, lease);
            return ActionResult.Success("Conversation participant locks released.");
        }

        public bool IsLocked(ResidentId residentId)
        {
            return residentId.IsValid && locksByResident.ContainsKey(residentId);
        }

        public bool TryGetConversationId(
            ResidentId residentId,
            out ConversationId conversationId)
        {
            conversationId = default;
            if (!residentId.IsValid ||
                !locksByResident.TryGetValue(residentId, out LockRecord record))
            {
                return false;
            }

            conversationId = record.ConversationId;
            return true;
        }

        private void ReleaseIfMatching(
            ResidentId residentId,
            ConversationParticipantLease lease)
        {
            if (locksByResident.TryGetValue(residentId, out LockRecord record) &&
                record.ConversationId == lease.ConversationId &&
                record.Revision == lease.Revision)
            {
                locksByResident.Remove(residentId);
            }
        }

        private static void Normalize(ref ResidentId first, ref ResidentId second)
        {
            if (first.CompareTo(second) <= 0)
            {
                return;
            }

            ResidentId temporary = first;
            first = second;
            second = temporary;
        }
    }
}
