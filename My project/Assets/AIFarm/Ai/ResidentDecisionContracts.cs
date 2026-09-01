using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public static class ResidentHighLevelIntents
    {
        public const string ProposeTownEvent = "propose_town_event";

        public const string Idle = "Idle";
    }

    public sealed class ResidentDecisionRequest
    {
        private readonly ReadOnlyCollection<string> allowedIntents;
        private readonly ReadOnlyCollection<ResidentId> allowedTargetResidentIds;

        public ResidentDecisionRequest(
            ResidentId residentId,
            ResidentContext context,
            string situation,
            IEnumerable<string> intents,
            IEnumerable<ResidentId> targetResidentIds)
        {
            if (!residentId.IsValid || context == null ||
                context.ResidentId != residentId)
            {
                throw new ArgumentException(
                    "A resident decision requires a matching owner context.");
            }

            var validatedIntents = new List<string>();
            var uniqueIntents = new HashSet<string>(StringComparer.Ordinal);
            foreach (string intent in intents ??
                throw new ArgumentNullException(nameof(intents)))
            {
                string normalized = (intent ?? string.Empty).Trim();
                if (!IsBoundedIntent(normalized) || !uniqueIntents.Add(normalized))
                {
                    throw new ArgumentException(
                        "Allowed intents must be unique bounded identifiers.",
                        nameof(intents));
                }

                validatedIntents.Add(normalized);
            }

            if (validatedIntents.Count < 1 || validatedIntents.Count > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(intents));
            }

            var validatedTargets = new List<ResidentId>();
            var uniqueTargets = new HashSet<ResidentId>();
            foreach (ResidentId targetResidentId in targetResidentIds ??
                throw new ArgumentNullException(nameof(targetResidentIds)))
            {
                if (!targetResidentId.IsValid || targetResidentId == residentId ||
                    !uniqueTargets.Add(targetResidentId))
                {
                    throw new ArgumentException(
                        "Allowed targets must be unique residents other than the owner.",
                        nameof(targetResidentIds));
                }

                validatedTargets.Add(targetResidentId);
            }

            if (validatedTargets.Count > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(targetResidentIds));
            }

            ResidentId = residentId;
            Context = context;
            Situation = BoundOptional(situation, 200);
            allowedIntents = new ReadOnlyCollection<string>(validatedIntents);
            allowedTargetResidentIds =
                new ReadOnlyCollection<ResidentId>(validatedTargets);
        }

        public ResidentId ResidentId { get; }

        public ResidentContext Context { get; }

        public string Situation { get; }

        public IReadOnlyList<string> AllowedIntents => allowedIntents;

        public IReadOnlyList<ResidentId> AllowedTargetResidentIds =>
            allowedTargetResidentIds;

        public bool IsAllowedIntent(string intent)
        {
            return allowedIntents.Contains((intent ?? string.Empty).Trim());
        }

        public bool IsAllowedTarget(ResidentId residentId)
        {
            return allowedTargetResidentIds.Contains(residentId);
        }

        private static bool IsBoundedIntent(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '_' || character == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static string BoundOptional(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }

    public sealed class ResidentDecisionSpec
    {
        public ResidentDecisionSpec(
            ResidentId residentId,
            string intent,
            ResidentId? targetResidentId,
            string reason,
            string provider)
        {
            string normalizedIntent = (intent ?? string.Empty).Trim();
            string normalizedReason = (reason ?? string.Empty).Trim();
            string normalizedProvider = (provider ?? string.Empty).Trim();
            if (!residentId.IsValid || normalizedIntent.Length < 1 ||
                normalizedIntent.Length > 64 || normalizedReason.Length < 1 ||
                normalizedReason.Length > 300 || normalizedProvider.Length < 1 ||
                normalizedProvider.Length > 32 ||
                (targetResidentId.HasValue &&
                    (!targetResidentId.Value.IsValid ||
                        targetResidentId.Value == residentId)))
            {
                throw new ArgumentException(
                    "A resident decision requires a bounded owner, intent, reason, and provider.");
            }

            ResidentId = residentId;
            Intent = normalizedIntent;
            TargetResidentId = targetResidentId;
            Reason = normalizedReason;
            Provider = normalizedProvider;
        }

        public ResidentId ResidentId { get; }

        public string Intent { get; }

        public ResidentId? TargetResidentId { get; }

        public string Reason { get; }

        public string Provider { get; }
    }
}
