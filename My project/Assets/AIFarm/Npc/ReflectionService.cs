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

        private readonly NpcRuntimeState runtimeState;

        public ReflectionService(NpcRuntimeState state)
        {
            runtimeState = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string BuildExpressionContext(string immediateContext)
        {
            var builder = new StringBuilder(MaximumExpressionContextLength);
            AppendSegment(builder, "人物：芽芽，认真乐观，优先解决作物生长问题", MaximumExpressionContextLength);
            AppendSegment(
                builder,
                $"当前：{Bound(immediateContext, 80)}",
                MaximumExpressionContextLength);

            IReadOnlyList<MemoryEntry> important =
                runtimeState.Memories.GetImportantRecentObservations(2);
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
                "人物：芽芽，农场助手，认真乐观、偶尔小抱怨，喜欢整齐，讨厌杂草和浪费",
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

            IReadOnlyList<MemoryEntry> important =
                runtimeState.Memories.GetImportantRecentObservations(2);
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

        private string CreateCompletedText()
        {
            if (runtimeState.LatestReflection != null)
            {
                return
                    "又完成一轮啦！上次的经验也用上了：" +
                    Bound(runtimeState.LatestReflection.Text, 75) +
                    " 🥕";
            }

            IReadOnlyList<MemoryEntry> important =
                runtimeState.Memories.GetImportantRecentObservations(8);
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
