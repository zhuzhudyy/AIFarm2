using System;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class ObservationService
    {
        public ObservationService()
            : this(ResidentIds.Yaya)
        {
        }

        public ObservationService(ResidentId ownerResidentId)
        {
            if (!ownerResidentId.IsValid)
            {
                throw new ArgumentException(
                    "ObservationService requires a valid owner ResidentId.",
                    nameof(ownerResidentId));
            }

            OwnerResidentId = ownerResidentId;
        }

        public ResidentId OwnerResidentId { get; }

        public long LastObservedWorldEventSequence { get; private set; }

        public ActionResult CaptureNewObservations(
            WorldEventLog eventLog,
            MemoryStore memoryStore,
            out int capturedCount)
        {
            capturedCount = 0;
            if (eventLog == null || memoryStore == null ||
                memoryStore.OwnerResidentId != OwnerResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Observation capture requires an event log and memory store.");
            }

            foreach (WorldEventEntry entry in eventLog.Entries)
            {
                if (entry.Sequence <= LastObservedWorldEventSequence)
                {
                    continue;
                }

                ActionResult observed = Observe(entry, memoryStore, out _);
                if (observed.Failed)
                {
                    return observed;
                }

                if (entry.IsVisibleTo(OwnerResidentId))
                {
                    capturedCount++;
                }
            }

            return ActionResult.Success($"Captured {capturedCount} new observations.");
        }

        public ActionResult Observe(
            WorldEventEntry worldEvent,
            MemoryStore memoryStore,
            out MemoryEntry memory)
        {
            memory = null;
            if (memoryStore == null || memoryStore.OwnerResidentId != OwnerResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Observation capture requires a memory store.");
            }

            if (worldEvent.Sequence <= LastObservedWorldEventSequence)
            {
                return ActionResult.Success("World event was already observed.");
            }

            if (!worldEvent.IsVisibleTo(OwnerResidentId))
            {
                LastObservedWorldEventSequence = worldEvent.Sequence;
                return ActionResult.Success("World event is not visible to this resident.");
            }

            ActionResult translated = TryDescribe(
                worldEvent,
                out string observation,
                out int importance);
            if (translated.Failed)
            {
                return translated;
            }

            MemorySourceKind sourceKind = SourceKindFor(worldEvent);
            ResidentId immediateSource = sourceKind == MemorySourceKind.Conversation
                ? ResolveConversationSource(worldEvent)
                : default;
            ActionResult stored = memoryStore.AddObservation(
                worldEvent.GameSeconds,
                observation,
                importance,
                worldEvent.Kind,
                sourceKind,
                worldEvent.EventId,
                worldEvent.FactId,
                parentKnowledgeId: null,
                immediateSource,
                BuildTags(worldEvent),
                // A generic conversation event has no source knowledge id, so it may
                // be remembered but cannot safely be propagated as a sourced fact.
                isShareable: sourceKind != MemorySourceKind.Conversation,
                out memory);
            if (stored.Succeeded)
            {
                LastObservedWorldEventSequence = worldEvent.Sequence;
            }

            return stored;
        }

        public ActionResult TryDescribe(
            WorldEventEntry worldEvent,
            out string observation,
            out int importance)
        {
            observation = string.Empty;
            importance = ImportanceFor(worldEvent.Kind);
            string plot = worldEvent.PlotNumber.HasValue
                ? $"{worldEvent.PlotNumber.Value:00} 号地"
                : "农田";

            switch (worldEvent.Kind)
            {
                case WorldEventKind.System:
                    observation = $"我知道现在的情况：{worldEvent.Message}";
                    break;
                case WorldEventKind.CommandAccepted:
                    observation = $"我收到了新的种田任务：{worldEvent.Message}";
                    break;
                case WorldEventKind.PlanDecision:
                    observation = $"我决定下一步这样做：{RemovePrefix(worldEvent.Message, "规划原因：")}";
                    break;
                case WorldEventKind.ActionStarted:
                    observation = DescribeAction(worldEvent, started: true);
                    break;
                case WorldEventKind.ActionCompleted:
                    observation = DescribeAction(worldEvent, started: false);
                    break;
                case WorldEventKind.ActionFailed:
                    observation = $"工作遇到了问题：{worldEvent.Message}";
                    break;
                case WorldEventKind.MoistureChanged:
                    observation = $"我注意到{plot}水分下降了，得优先补水。";
                    break;
                case WorldEventKind.WeedsAppeared:
                    observation = $"我发现{plot}长出了杂草，得尽快清理。";
                    break;
                case WorldEventKind.CropMatured:
                    observation = $"我看到{plot}的胡萝卜成熟了，可以收获。";
                    break;
                case WorldEventKind.TimeControlChanged:
                    observation = $"我注意到时间设置有变化：{worldEvent.Message}";
                    break;
                case WorldEventKind.GoalCompleted:
                    observation = "我完成了这一轮完整种植，九块地的胡萝卜都收好了。";
                    break;
                case WorldEventKind.ConversationCompleted:
                    observation = $"我记得这次谈话：{worldEvent.Message}";
                    break;
                case WorldEventKind.ConversationInterrupted:
                    observation = $"我记得这段未完成的谈话：{worldEvent.Message}";
                    break;
                case WorldEventKind.DayEnded:
                    observation = $"一天结束了：{worldEvent.Message}";
                    break;
                default:
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"Unsupported world event kind: {worldEvent.Kind}.");
            }

            observation = Bound(observation, MemoryStore.MaximumTextLength);
            return ActionResult.Success("World event converted to a natural-language observation.");
        }

        private static int ImportanceFor(WorldEventKind kind)
        {
            switch (kind)
            {
                case WorldEventKind.ActionFailed:
                case WorldEventKind.GoalCompleted:
                    return 10;
                case WorldEventKind.WeedsAppeared:
                    return 9;
                case WorldEventKind.MoistureChanged:
                    return 8;
                case WorldEventKind.CropMatured:
                    return 7;
                case WorldEventKind.DayEnded:
                    return 7;
                case WorldEventKind.CommandAccepted:
                    return 6;
                case WorldEventKind.ConversationCompleted:
                case WorldEventKind.ConversationInterrupted:
                    return 6;
                case WorldEventKind.ActionCompleted:
                    return 4;
                case WorldEventKind.PlanDecision:
                    return 3;
                case WorldEventKind.ActionStarted:
                case WorldEventKind.System:
                    return 2;
                default:
                    return 1;
            }
        }

        private static string DescribeAction(WorldEventEntry worldEvent, bool started)
        {
            string phase = started ? "开始" : "完成";
            string target = worldEvent.PlotNumber.HasValue
                ? $"{worldEvent.PlotNumber.Value:00} 号地"
                : "作物";
            string message = worldEvent.Message ?? string.Empty;
            if (message.IndexOf("Sow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"我{phase}给{target}播种。";
            }

            if (message.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"我{phase}给{target}浇水。";
            }

            if (message.IndexOf("Fertilize", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"我{phase}给{target}施肥。";
            }

            if (message.IndexOf("Weed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"我{phase}清理{target}的杂草。";
            }

            if (message.IndexOf("Harvest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"我{phase}收获{target}的胡萝卜。";
            }

            if (message.IndexOf("Wait", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return started ? "我先等作物继续生长。" : "等待结束，我继续观察作物。";
            }

            return started
                ? $"我开始执行：{worldEvent.Message}"
                : $"我完成了：{worldEvent.Message}";
        }

        private static string RemovePrefix(string value, string prefix)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.StartsWith(prefix, StringComparison.Ordinal)
                ? normalized.Substring(prefix.Length).Trim()
                : normalized;
        }

        private static string Bound(string value, int maximumLength)
        {
            return value.Length <= maximumLength
                ? value
                : value.Substring(0, maximumLength);
        }

        private MemorySourceKind SourceKindFor(WorldEventEntry worldEvent)
        {
            switch (worldEvent.Visibility)
            {
                case WorldEventVisibility.PublicTownEvent:
                    return MemorySourceKind.PublicTownEvent;
                case WorldEventVisibility.Conversation:
                    return MemorySourceKind.Conversation;
                case WorldEventVisibility.Private:
                    return worldEvent.Kind == WorldEventKind.CommandAccepted
                        ? MemorySourceKind.PlayerInput
                        : MemorySourceKind.Perception;
                case WorldEventVisibility.Perceivable:
                default:
                    return MemorySourceKind.Perception;
            }
        }

        private ResidentId ResolveConversationSource(WorldEventEntry worldEvent)
        {
            if (worldEvent.ActorResidentId.HasValue &&
                worldEvent.ActorResidentId.Value.IsValid &&
                worldEvent.ActorResidentId.Value != OwnerResidentId)
            {
                return worldEvent.ActorResidentId.Value;
            }

            foreach (ResidentId participant in worldEvent.AudienceResidentIds)
            {
                if (participant != OwnerResidentId)
                {
                    return participant;
                }
            }

            return default;
        }

        private static string[] BuildTags(WorldEventEntry worldEvent)
        {
            var tags = new System.Collections.Generic.List<string>();
            if (worldEvent.Tags != null)
            {
                foreach (string tag in worldEvent.Tags)
                {
                    if (!tags.Contains(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }

            string kindTag = worldEvent.Kind.ToString().ToLowerInvariant();
            if (!tags.Contains(kindTag) && tags.Count < MemoryEntry.MaximumTagCount)
            {
                tags.Add(kindTag);
            }

            if (worldEvent.PlotNumber.HasValue && tags.Count < MemoryEntry.MaximumTagCount)
            {
                tags.Add($"plot-{worldEvent.PlotNumber.Value:00}");
            }

            return tags.ToArray();
        }
    }
}
