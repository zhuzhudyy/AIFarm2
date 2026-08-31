using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class NpcExpressionDirector
    {
        private const int MaxPendingExpressions = 8;
        private const int MaxExpressionHistory = 16;

        private readonly ICharacterExpressionService service;
        private readonly double cooldownSeconds;
        private readonly double displaySeconds;
        private readonly Dictionary<NpcExpressionTrigger, double> lastTriggerTimes =
            new Dictionary<NpcExpressionTrigger, double>();
        private readonly Queue<NpcExpression> pending = new Queue<NpcExpression>();
        private readonly List<NpcExpression> recentExpressions = new List<NpcExpression>();
        private readonly ReadOnlyCollection<NpcExpression> readOnlyRecentExpressions;
        private int variantIndex;
        private double currentElapsedSeconds;

        public NpcExpressionDirector(
            ICharacterExpressionService service,
            double cooldownSeconds,
            double displaySeconds)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            if (!IsFinitePositive(cooldownSeconds) || !IsFinitePositive(displaySeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cooldownSeconds),
                    "Expression cooldown and display duration must be finite and positive.");
            }

            this.cooldownSeconds = cooldownSeconds;
            this.displaySeconds = displaySeconds;
            readOnlyRecentExpressions = new ReadOnlyCollection<NpcExpression>(recentExpressions);
        }

        public NpcExpression Current { get; private set; }

        public NpcExpression Latest { get; private set; }

        public IReadOnlyList<NpcExpression> RecentExpressions => readOnlyRecentExpressions;

        public int PendingCount => pending.Count;

        public ActionResult CanTrigger(
            NpcExpressionTrigger trigger,
            double realSeconds)
        {
            return ValidateTrigger(trigger, realSeconds);
        }

        public ActionResult Trigger(
            NpcExpressionTrigger trigger,
            string context,
            double realSeconds,
            bool interrupt = false)
        {
            ActionResult available = ValidateTrigger(trigger, realSeconds);
            if (available.Failed)
            {
                return available;
            }

            ActionResult created = service.CreateExpression(
                trigger,
                context,
                variantIndex++,
                out NpcExpression expression);
            if (created.Failed)
            {
                return created;
            }

            return DisplayExpression(expression, realSeconds, interrupt);
        }

        public ActionResult Trigger(
            NpcExpression expression,
            double realSeconds,
            bool interrupt = false)
        {
            if (expression == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC expression cannot be null.");
            }

            ActionResult available = ValidateTrigger(expression.Trigger, realSeconds);
            if (available.Failed)
            {
                return available;
            }

            return DisplayExpression(expression, realSeconds, interrupt);
        }

        private ActionResult DisplayExpression(
            NpcExpression expression,
            double realSeconds,
            bool interrupt)
        {
            NpcExpressionTrigger trigger = expression.Trigger;

            lastTriggerTimes[trigger] = realSeconds;
            Latest = expression;
            AddToHistory(expression);
            if (interrupt)
            {
                pending.Clear();
                Current = expression;
                currentElapsedSeconds = 0d;
                return ActionResult.Success("NPC expression interrupted the current dialogue.");
            }

            if (Current == null)
            {
                Current = expression;
                currentElapsedSeconds = 0d;
                return ActionResult.Success("NPC expression displayed.");
            }

            if (pending.Count == MaxPendingExpressions)
            {
                pending.Dequeue();
            }

            pending.Enqueue(expression);
            return ActionResult.Success("NPC expression queued.");
        }

        private ActionResult ValidateTrigger(
            NpcExpressionTrigger trigger,
            double realSeconds)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Expression time must be finite and non-negative.");
            }

            if (lastTriggerTimes.TryGetValue(trigger, out double previousTime) &&
                realSeconds - previousTime < cooldownSeconds)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Expression trigger {trigger} is cooling down.");
            }

            return ActionResult.Success();
        }

        public ActionResult Advance(double unscaledSeconds)
        {
            if (double.IsNaN(unscaledSeconds) || double.IsInfinity(unscaledSeconds) || unscaledSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Expression elapsed time must be finite and non-negative.");
            }

            if (Current == null || unscaledSeconds == 0d)
            {
                return ActionResult.Success("NPC expression did not advance.");
            }

            currentElapsedSeconds += unscaledSeconds;
            while (Current != null && currentElapsedSeconds >= displaySeconds)
            {
                currentElapsedSeconds -= displaySeconds;
                Current = pending.Count > 0 ? pending.Dequeue() : null;
            }

            return ActionResult.Success("NPC expression advanced.");
        }

        private void AddToHistory(NpcExpression expression)
        {
            if (recentExpressions.Count == MaxExpressionHistory)
            {
                recentExpressions.RemoveAt(0);
            }

            recentExpressions.Add(expression);
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
