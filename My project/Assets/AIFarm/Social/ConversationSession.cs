using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public readonly struct ConversationId : IEquatable<ConversationId>
    {
        private readonly string value;

        public ConversationId(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || normalized.Length > 80)
            {
                throw new ArgumentException(
                    "ConversationId must contain 1-80 characters.",
                    nameof(value));
            }

            this.value = normalized;
        }

        public string Value => value ?? string.Empty;

        public bool IsValid => !string.IsNullOrWhiteSpace(value) && value.Length <= 80;

        public bool Equals(ConversationId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ConversationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(ConversationId left, ConversationId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ConversationId left, ConversationId right)
        {
            return !left.Equals(right);
        }
    }

    public enum ConversationState
    {
        Proposed = 0,
        Active,
        Completed,
        TimedOut,
        Cancelled,
        Rejected
    }

    public enum ConversationEndReason
    {
        None = 0,
        SentenceLimitReached,
        TimedOut,
        Cancelled,
        ReservationLost,
        NavigationFailed
    }

    public enum ConversationOutcome
    {
        Positive = 0,
        Neutral,
        Helpful,
        Awkward,
        Conflict
    }

    public sealed class ConversationUtterance
    {
        internal ConversationUtterance(
            string utteranceId,
            int turnNumber,
            ResidentId speakerResidentId,
            NpcMood mood,
            string emoji,
            string text,
            double playedAtGameSeconds)
        {
            UtteranceId = utteranceId;
            TurnNumber = turnNumber;
            SpeakerResidentId = speakerResidentId;
            Mood = mood;
            Emoji = emoji;
            Text = text;
            PlayedAtGameSeconds = playedAtGameSeconds;
        }

        public string UtteranceId { get; }

        public int TurnNumber { get; }

        public ResidentId SpeakerResidentId { get; }

        public NpcMood Mood { get; }

        public string Emoji { get; }

        public string Text { get; }

        public double PlayedAtGameSeconds { get; }
    }

    public sealed class ConversationSession
    {
        public const int MinimumSentenceCount = 2;
        public const int MaximumSentenceCount = 6;
        public const double MaximumTimeoutSeconds = 30d;

        private readonly List<ConversationUtterance> utterances =
            new List<ConversationUtterance>(MaximumSentenceCount);
        private readonly ReadOnlyCollection<ConversationUtterance> readOnlyUtterances;
        private ReadOnlyCollection<ConversationLineSpec> preparedLines;

        internal ConversationSession(
            ConversationId conversationId,
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            int targetSentenceCount,
            double startedAtMonotonicSeconds,
            double timeoutSeconds)
        {
            if (!conversationId.IsValid || !firstResidentId.IsValid ||
                !secondResidentId.IsValid || firstResidentId == secondResidentId)
            {
                throw new ArgumentException(
                    "A conversation requires one ID and exactly two different residents.");
            }

            if (targetSentenceCount < MinimumSentenceCount ||
                targetSentenceCount > MaximumSentenceCount)
            {
                throw new ArgumentOutOfRangeException(nameof(targetSentenceCount));
            }

            if (!IsFiniteNonNegative(startedAtMonotonicSeconds) ||
                !IsFinitePositive(timeoutSeconds) ||
                timeoutSeconds > MaximumTimeoutSeconds ||
                !IsFiniteNonNegative(startedAtMonotonicSeconds + timeoutSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutSeconds),
                    "Conversation timeout must be positive and no greater than 30 seconds.");
            }

            ConversationId = conversationId;
            FirstResidentId = firstResidentId;
            SecondResidentId = secondResidentId;
            TargetSentenceCount = targetSentenceCount;
            RequestedSentenceLimit = targetSentenceCount;
            StartedAtMonotonicSeconds = startedAtMonotonicSeconds;
            DeadlineAtMonotonicSeconds = startedAtMonotonicSeconds + timeoutSeconds;
            TurnOwnerResidentId = firstResidentId;
            State = ConversationState.Proposed;
            readOnlyUtterances = new ReadOnlyCollection<ConversationUtterance>(utterances);
        }

        public ConversationId ConversationId { get; }

        public ResidentId FirstResidentId { get; }

        public ResidentId SecondResidentId { get; }

        public ResidentId TurnOwnerResidentId { get; private set; }

        public int DeliveredSentenceCount => utterances.Count;

        public int TargetSentenceCount { get; private set; }

        public int RequestedSentenceLimit { get; }

        public long ConversationVersion { get; private set; } = 1;

        public double StartedAtMonotonicSeconds { get; }

        public double DeadlineAtMonotonicSeconds { get; }

        public ConversationState State { get; private set; }

        public ConversationEndReason EndReason { get; private set; }

        public ConversationOutcome? Outcome { get; private set; }

        public ConversationOutcome? PreparedOutcome { get; private set; }

        public string ScriptProvider { get; private set; } = string.Empty;

        public IReadOnlyList<ConversationLineSpec> PreparedLines =>
            preparedLines ?? (IReadOnlyList<ConversationLineSpec>)Array.Empty<ConversationLineSpec>();

        public bool HasPreparedScript => preparedLines != null;

        public IReadOnlyList<ConversationUtterance> Utterances => readOnlyUtterances;

        public bool IsTerminal => State == ConversationState.Completed ||
            State == ConversationState.TimedOut ||
            State == ConversationState.Cancelled ||
            State == ConversationState.Rejected;

        public bool IsParticipant(ResidentId residentId)
        {
            return residentId == FirstResidentId || residentId == SecondResidentId;
        }

        internal ActionResult Activate()
        {
            if (State != ConversationState.Proposed)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a proposed conversation can become active.");
            }

            State = ConversationState.Active;
            return ActionResult.Success("Conversation activated.");
        }

        internal ActionResult SetPreparedScript(ConversationScriptSpec script)
        {
            if (State != ConversationState.Active || script == null ||
                HasPreparedScript || DeliveredSentenceCount != 0 ||
                !IsParticipant(script.ResidentId) ||
                script.Lines.Count < MinimumSentenceCount ||
                script.Lines.Count > RequestedSentenceLimit ||
                script.Lines.Count > MaximumSentenceCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "A script can be attached once, before playback, with 2-6 participant lines.");
            }

            var validatedLines = new List<ConversationLineSpec>(script.Lines.Count);
            foreach (ConversationLineSpec line in script.Lines)
            {
                if (line == null || !IsParticipant(line.SpeakerId))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Every prepared speaker must belong to this conversation.");
                }

                validatedLines.Add(line);
            }

            preparedLines = new ReadOnlyCollection<ConversationLineSpec>(validatedLines);
            PreparedOutcome = script.Outcome;
            ScriptProvider = script.Provider;
            TargetSentenceCount = preparedLines.Count;
            TurnOwnerResidentId = preparedLines[0].SpeakerId;
            ConversationVersion++;
            return ActionResult.Success("Conversation script prepared for Unity playback.");
        }

        internal bool TryGetNextPreparedLine(out ConversationLineSpec line)
        {
            line = null;
            if (!HasPreparedScript || State != ConversationState.Active ||
                DeliveredSentenceCount >= preparedLines.Count)
            {
                return false;
            }

            line = preparedLines[DeliveredSentenceCount];
            return true;
        }

        internal ActionResult AddUtterance(
            ResidentId speakerResidentId,
            NpcMood mood,
            string emoji,
            string text,
            double playedAtGameSeconds,
            out ConversationUtterance utterance)
        {
            utterance = null;
            if (State != ConversationState.Active || !IsParticipant(speakerResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only an active conversation participant can deliver a sentence.");
            }

            string normalized = (text ?? string.Empty).Trim();
            string normalizedEmoji = (emoji ?? string.Empty).Trim();
            if (!Enum.IsDefined(typeof(NpcMood), mood) ||
                normalizedEmoji.Length < 1 || normalizedEmoji.Length > 8 ||
                normalized.Length == 0 || normalized.Length > 300 ||
                !IsFiniteNonNegative(playedAtGameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A played line requires bounded text, Emoji, mood, and game time.");
            }

            if (utterances.Count >= TargetSentenceCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The configured sentence limit has already been reached.");
            }

            if (HasPreparedScript)
            {
                ConversationLineSpec expected = preparedLines[utterances.Count];
                if (expected.SpeakerId != speakerResidentId || expected.Mood != mood ||
                    expected.Emoji != normalizedEmoji || expected.Text != normalized)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Only the next validated prepared line can be played.");
                }
            }
            else if (speakerResidentId != TurnOwnerResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The local template speaker does not own the current turn.");
            }

            int turnNumber = utterances.Count + 1;
            utterance = new ConversationUtterance(
                $"{ConversationId.Value}-utterance-{turnNumber:00}",
                turnNumber,
                speakerResidentId,
                mood,
                normalizedEmoji,
                normalized,
                playedAtGameSeconds);
            utterances.Add(utterance);
            TurnOwnerResidentId = HasPreparedScript && utterances.Count < preparedLines.Count
                ? preparedLines[utterances.Count].SpeakerId
                : speakerResidentId == FirstResidentId
                    ? SecondResidentId
                    : FirstResidentId;
            ConversationVersion++;
            return ActionResult.Success("Local conversation sentence delivered.");
        }

        internal ActionResult Complete(ConversationOutcome outcome)
        {
            if (State != ConversationState.Active ||
                utterances.Count < MinimumSentenceCount ||
                utterances.Count != TargetSentenceCount ||
                !Enum.IsDefined(typeof(ConversationOutcome), outcome) ||
                (PreparedOutcome.HasValue && PreparedOutcome.Value != outcome))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A conversation completes only after its valid 2-6 sentence limit.");
            }

            Outcome = outcome;
            State = ConversationState.Completed;
            EndReason = ConversationEndReason.SentenceLimitReached;
            ConversationVersion++;
            return ActionResult.Success("Conversation completed.");
        }

        internal ActionResult End(ConversationState terminalState, ConversationEndReason reason)
        {
            if (State != ConversationState.Active && State != ConversationState.Proposed)
            {
                return ActionResult.Success("Conversation was already terminal.");
            }

            if (terminalState != ConversationState.TimedOut &&
                terminalState != ConversationState.Cancelled &&
                terminalState != ConversationState.Rejected)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid non-completion terminal state is required.");
            }

            State = terminalState;
            EndReason = reason;
            ConversationVersion++;
            return ActionResult.Success("Conversation ended and will release its resources.");
        }

        internal bool IsTimedOut(double monotonicSeconds)
        {
            return State == ConversationState.Active &&
                IsFiniteNonNegative(monotonicSeconds) &&
                monotonicSeconds >= DeadlineAtMonotonicSeconds;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
