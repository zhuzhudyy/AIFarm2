using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;

namespace AIFarm.Social
{
    public readonly struct SocialResidentStatus
    {
        public SocialResidentStatus(
            ResidentId residentId,
            TownLocationId locationId,
            bool isIdle,
            bool hasActiveAction = false,
            bool isPerformingEmergencyFarmWork = false,
            bool hasPlayerInstruction = false,
            bool isGoingHomeToSleep = false)
        {
            ResidentId = residentId;
            LocationId = locationId;
            IsIdle = isIdle;
            HasActiveAction = hasActiveAction;
            IsPerformingEmergencyFarmWork = isPerformingEmergencyFarmWork;
            HasPlayerInstruction = hasPlayerInstruction;
            IsGoingHomeToSleep = isGoingHomeToSleep;
        }

        public ResidentId ResidentId { get; }

        public TownLocationId LocationId { get; }

        public bool IsIdle { get; }

        public bool HasActiveAction { get; }

        public bool IsPerformingEmergencyFarmWork { get; }

        public bool HasPlayerInstruction { get; }

        public bool IsGoingHomeToSleep { get; }

        public bool IsEligible => ResidentId.IsValid && LocationId.IsValid && IsIdle &&
            !HasActiveAction && !IsPerformingEmergencyFarmWork &&
            !HasPlayerInstruction && !IsGoingHomeToSleep;
    }

    public readonly struct SocialOpportunity
    {
        internal SocialOpportunity(
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            TownLocationId locationId)
        {
            FirstResidentId = firstResidentId;
            SecondResidentId = secondResidentId;
            LocationId = locationId;
        }

        public ResidentId FirstResidentId { get; }

        public ResidentId SecondResidentId { get; }

        public TownLocationId LocationId { get; }

        public bool IsValid => FirstResidentId.IsValid && SecondResidentId.IsValid &&
            FirstResidentId != SecondResidentId && LocationId.IsValid;
    }

    public sealed class SocialOpportunityDetector
    {
        public const double DefaultCooldownGameSeconds = 2d * 60d * 60d;

        private readonly Dictionary<ResidentId, double> lastSocialStartByResident =
            new Dictionary<ResidentId, double>();

        public SocialOpportunityDetector(
            double cooldownGameSeconds = DefaultCooldownGameSeconds)
        {
            if (!IsFiniteNonNegative(cooldownGameSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(cooldownGameSeconds));
            }

            CooldownGameSeconds = cooldownGameSeconds;
        }

        public double CooldownGameSeconds { get; }

        public ActionResult TryDetect(
            IEnumerable<SocialResidentStatus> residentStatuses,
            double gameSeconds,
            ConversationParticipantLock participantLock,
            out SocialOpportunity opportunity)
        {
            opportunity = default;
            if (residentStatuses == null || participantLock == null ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Social detection requires statuses, a participant lock, and game time.");
            }

            var eligible = new List<SocialResidentStatus>();
            var uniqueResidents = new HashSet<ResidentId>();
            foreach (SocialResidentStatus status in residentStatuses)
            {
                if (!status.ResidentId.IsValid || !uniqueResidents.Add(status.ResidentId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "Social statuses must contain unique valid resident IDs.");
                }

                if (status.IsEligible && !participantLock.IsLocked(status.ResidentId) &&
                    IsCooldownComplete(status.ResidentId, gameSeconds))
                {
                    eligible.Add(status);
                }
            }

            eligible.Sort((left, right) => left.ResidentId.CompareTo(right.ResidentId));
            for (int firstIndex = 0; firstIndex < eligible.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1;
                    secondIndex < eligible.Count;
                    secondIndex++)
                {
                    if (eligible[firstIndex].LocationId != eligible[secondIndex].LocationId)
                    {
                        continue;
                    }

                    opportunity = new SocialOpportunity(
                        eligible[firstIndex].ResidentId,
                        eligible[secondIndex].ResidentId,
                        eligible[firstIndex].LocationId);
                    return ActionResult.Success("An offline two-resident social opportunity exists.");
                }
            }

            return ActionResult.Failure(
                ActionFailureReason.InvalidState,
                "No two eligible residents share a location outside their cooldowns.");
        }

        public ActionResult RecordConversationStarted(
            SocialOpportunity opportunity,
            double gameSeconds)
        {
            if (!opportunity.IsValid || !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid social opportunity and game time are required.");
            }

            lastSocialStartByResident[opportunity.FirstResidentId] = gameSeconds;
            lastSocialStartByResident[opportunity.SecondResidentId] = gameSeconds;
            return ActionResult.Success("Social cooldown recorded for both participants.");
        }

        public bool IsCooldownComplete(ResidentId residentId, double gameSeconds)
        {
            if (!residentId.IsValid || !IsFiniteNonNegative(gameSeconds))
            {
                return false;
            }

            return !lastSocialStartByResident.TryGetValue(residentId, out double lastStart) ||
                gameSeconds < lastStart ||
                gameSeconds - lastStart >= CooldownGameSeconds;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
