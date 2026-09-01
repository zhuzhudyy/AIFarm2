using System;
using System.Collections.Generic;
using System.Text;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class ReflectionService
    {
        public const int MaximumExpressionContextLength = 200;
        public const int MaximumReflectionContextLength = 240;

        private readonly ResidentRuntimeState runtimeState;

        public ReflectionService(ResidentRuntimeState state)
        {
            runtimeState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string BuildExpressionContext(string immediateContext)
        {
            var builder = new StringBuilder(MaximumExpressionContextLength);
            AppendSegment(
                builder,
                $"人物：{Bound(runtimeState.Persona.PromptSummary, 100)}",
                MaximumExpressionContextLength);
            AppendSegment(
                builder,
                $"当前：{Bound(immediateContext, 80)}",
                MaximumExpressionContextLength);

            IReadOnlyList<MemoryEntry> important = QueryImportantMemories(2);
            foreach (MemoryEntry memory in important)
            {
                AppendSegment(
                    builder,
                    $"重要观察：{Bound(memory.Text, 55)}",
                    MaximumExpressionContextLength);
            }

            return builder.ToString();
        }

        public string BuildReflectionContext(string completionSummary)
        {
            var builder = new StringBuilder(MaximumReflectionContextLength);
            AppendSegment(
                builder,
                $"人物：{Bound(runtimeState.Persona.PromptSummary, 120)}",
                MaximumReflectionContextLength);
            AppendSegment(
                builder,
                $"本轮结果：{Bound(completionSummary, 60)}",
                MaximumReflectionContextLength);

            if (runtimeState.LatestReflection != null)
            {
                AppendSegment(
                    builder,
                    $"上次反思：{Bound(runtimeState.LatestReflection.Text, 65)}",
                    MaximumReflectionContextLength);
            }

            IReadOnlyList<MemoryEntry> important = QueryImportantMemories(2);
            foreach (MemoryEntry memory in important)
            {
                AppendSegment(
                    builder,
                    $"重要观察：{Bound(memory.Text, 45)}",
                    MaximumReflectionContextLength);
            }

            return builder.ToString();
        }

        public ActionResult CreateLocalReflection(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            out NpcReflection reflection)
        {
            reflection = null;
            if (goal == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Local reflection requires a farm goal.");
            }

            NpcMood mood;
            string emoji;
            string text;
            if (outcome == NpcReflectionOutcome.Completed)
            {
                mood = NpcMood.Proud;
                emoji = "🥕";
                text = CreateCompletedText();
            }
            else if (outcome == NpcReflectionOutcome.Failed)
            {
                mood = NpcMood.Worried;
                emoji = "⚠";
                text = $"这轮没做好，我记住原因了：{Bound(eventSummary, 120)} ⚠";
            }
            else
            {
                mood = NpcMood.Focused;
                emoji = "🌱";
                text = $"还在忙，我先处理最影响生长的问题。{Bound(eventSummary, 100)} 🌱";
            }

            reflection = new NpcReflection(goal.GoalId, outcome, mood, emoji, Bound(text, 300));
            return ActionResult.Success("Local generative-agent reflection created.");
        }

        public ActionResult CreateAndStoreLocalDayEndReflection(
            int completedDay,
            double gameSeconds,
            out MemoryEntry memory)
        {
            memory = null;
            if (completedDay < 1 || double.IsNaN(gameSeconds) ||
                double.IsInfinity(gameSeconds) || gameSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A daily reflection requires a completed day and valid game time.");
            }

            double dayStart = Math.Max(
                0d,
                (completedDay - 1d) * ResidentReflectionCoordinator.GameSecondsPerDay);
            IReadOnlyList<MemoryEntry> important = QueryImportantMemories(
                2,
                dayStart,
                gameSeconds);
            string detail = important.Count == 0
                ? "今天没有发生需要特别记录的大事。"
                : $"今天最值得记住的是：{Bound(important[0].Text, 120)}";
            string text = Bound(
                $"第 {completedDay} 天结束了。{detail} 明天继续按自己的日程生活。",
                MemoryStore.MaximumTextLength);
            return runtimeState.Memories.AddDayEndReflection(
                runtimeState.ResidentId,
                completedDay,
                gameSeconds,
                text,
                out memory);
        }

        public static bool IsEligibleTrigger(WorldEventKind kind)
        {
            return kind == WorldEventKind.GoalCompleted ||
                kind == WorldEventKind.DayEnded;
        }

        private string CreateCompletedText()
        {
            if (runtimeState.LatestReflection != null)
            {
                return
                    "又完成一轮啦！上次的经验也用上了：" +
                    Bound(runtimeState.LatestReflection.Text, 75) +
                    " 🥕";
            }

            IReadOnlyList<MemoryEntry> important = QueryImportantMemories(8);
            foreach (MemoryEntry memory in important)
            {
                if (memory.SourceEventKind == WorldEventKind.WeedsAppeared)
                {
                    return "九块地都收好啦！杂草真会添乱，下轮我还是先清掉它们 🌿";
                }

                if (memory.SourceEventKind == WorldEventKind.MoistureChanged)
                {
                    return "九块地都收好啦！下轮一看到缺水，我就先补上 💧";
                }

                if (memory.SourceEventKind == WorldEventKind.ActionFailed)
                {
                    return "这一轮总算完成啦！出过的问题我记住了，下次少浪费时间 ⚠";
                }
            }

            return "九块地整整齐齐收好啦！下轮也继续保持 🥕";
        }

        private IReadOnlyList<MemoryEntry> QueryImportantMemories(
            int count,
            double? afterGameSecondsExclusive = null,
            double? beforeGameSecondsExclusive = null)
        {
            if (count <= 0)
            {
                return Array.Empty<MemoryEntry>();
            }

            var memories = new List<MemoryEntry>();
            foreach (MemoryEntry memory in runtimeState.Memories.Entries)
            {
                if (memory == null ||
                    memory.OwnerResidentId != runtimeState.ResidentId ||
                    memory.Kind == MemoryEntryKind.Reflection ||
                    memory.Importance < MemoryEntry.HighImportanceThreshold ||
                    (afterGameSecondsExclusive.HasValue &&
                        memory.GameSeconds <= afterGameSecondsExclusive.Value) ||
                    (beforeGameSecondsExclusive.HasValue &&
                        memory.GameSeconds >= beforeGameSecondsExclusive.Value))
                {
                    continue;
                }

                memories.Add(memory);
            }

            memories.Sort((left, right) =>
            {
                int importanceOrder = right.Importance.CompareTo(left.Importance);
                if (importanceOrder != 0)
                {
                    return importanceOrder;
                }

                int recencyOrder = right.GameSeconds.CompareTo(left.GameSeconds);
                return recencyOrder != 0
                    ? recencyOrder
                    : right.Sequence.CompareTo(left.Sequence);
            });
            if (memories.Count > count)
            {
                memories.RemoveRange(count, memories.Count - count);
            }

            return memories;
        }

        private static void AppendSegment(
            StringBuilder builder,
            string segment,
            int maximumLength)
        {
            string normalized = (segment ?? string.Empty).Trim();
            if (normalized.Length == 0 || builder.Length >= maximumLength)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('；');
            }

            int remaining = maximumLength - builder.Length;
            builder.Append(normalized.Length <= remaining
                ? normalized
                : normalized.Substring(0, remaining));
        }

        private static string Bound(string value, int maximumLength)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }
}
