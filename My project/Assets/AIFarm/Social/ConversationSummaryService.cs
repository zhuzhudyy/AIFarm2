using System;
using System.Collections.Generic;
using System.Text;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class ConversationSummaryService
    {
        private const int SummaryImportance = 6;
        private const int MaximumSummaryTags = 8;

        private readonly ResidentRegistry residentRegistry;

        public ConversationSummaryService(ResidentRegistry registry)
        {
            residentRegistry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public ActionResult RecordCompletedConversation(
            ConversationSession session,
            double gameSeconds)
        {
            if (session == null || session.State != ConversationState.Completed ||
                !session.Outcome.HasValue || session.Utterances.Count == 0 ||
                !IsFiniteNonNegative(gameSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Only the actual utterances of a completed conversation can be summarized.");
            }

            ActionResult firstStored = RecordPerspective(
                session,
                session.FirstResidentId,
                session.SecondResidentId,
                gameSeconds);
            ActionResult secondStored = RecordPerspective(
                session,
                session.SecondResidentId,
                session.FirstResidentId,
                gameSeconds);
            return firstStored.Failed ? firstStored : secondStored;
        }

        private ActionResult RecordPerspective(
            ConversationSession session,
            ResidentId ownerResidentId,
            ResidentId otherResidentId,
            double gameSeconds)
        {
            ActionResult ownerDefinitionResolved = residentRegistry.TryGetDefinition(
                ownerResidentId,
                out ResidentDefinition ownerDefinition);
            ActionResult otherDefinitionResolved = residentRegistry.TryGetDefinition(
                otherResidentId,
                out ResidentDefinition otherDefinition);
            ActionResult ownerRuntimeResolved = residentRegistry.TryGetRuntimeState(
                ownerResidentId,
                out ResidentRuntimeState ownerRuntime);
            if (ownerDefinitionResolved.Failed || otherDefinitionResolved.Failed ||
                ownerRuntimeResolved.Failed)
            {
                return FirstFailure(
                    ownerDefinitionResolved,
                    otherDefinitionResolved,
                    ownerRuntimeResolved);
            }

            IReadOnlyList<string> tags = BuildSummaryTags(session.Outcome.Value);
            string summary = CreatePerspectiveSummary(
                session,
                ownerDefinition,
                otherDefinition);

            ActionResult summaryStored = ownerRuntime.Memories.AddConversationSummary(
                ownerResidentId,
                gameSeconds,
                summary,
                SummaryImportance,
                otherResidentId,
                session.ConversationId.Value,
                rootFactId: null,
                parentKnowledgeId: null,
                tags,
                isShareable: false,
                out MemoryEntry _);
            if (summaryStored.Failed)
            {
                return summaryStored;
            }

            return RecordDeliveredKnowledge(
                session,
                ownerResidentId,
                ownerRuntime,
                gameSeconds);
        }

        private ActionResult RecordDeliveredKnowledge(
            ConversationSession session,
            ResidentId listenerResidentId,
            ResidentRuntimeState listenerRuntime,
            double gameSeconds)
        {
            var deliveredKnowledgeIds = new HashSet<string>(StringComparer.Ordinal);
            var deliveredRootFactIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConversationUtterance utterance in session.Utterances)
            {
                if (utterance.SpeakerResidentId == listenerResidentId ||
                    string.IsNullOrWhiteSpace(utterance.SharedKnowledgeId) ||
                    !deliveredKnowledgeIds.Add(utterance.SharedKnowledgeId))
                {
                    continue;
                }

                if (!session.TryGetPreparedKnowledgeSnapshot(
                        utterance.SpeakerResidentId,
                        utterance.SharedKnowledgeId,
                        out MemoryEntry knowledge) ||
                    knowledge == null ||
                    knowledge.OwnerResidentId != utterance.SpeakerResidentId ||
                    !knowledge.IsShareable)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "A played knowledge reference lost its immutable source snapshot.");
                }

                string rootFactId = string.IsNullOrWhiteSpace(knowledge.RootFactId)
                    ? knowledge.KnowledgeId
                    : knowledge.RootFactId;
                if (!deliveredRootFactIds.Add(rootFactId))
                {
                    continue;
                }

                ActionResult checkedExisting = listenerRuntime.Memories.ContainsRootFact(
                    listenerResidentId,
                    rootFactId,
                    out bool listenerAlreadyKnows);
                if (checkedExisting.Failed)
                {
                    return checkedExisting;
                }

                if (listenerAlreadyKnows)
                {
                    continue;
                }

                ActionResult speakerResolved = residentRegistry.TryGetDefinition(
                    utterance.SpeakerResidentId,
                    out ResidentDefinition speakerDefinition);
                if (speakerResolved.Failed)
                {
                    return speakerResolved;
                }

                ActionResult stored = listenerRuntime.Memories.AddObservation(
                    listenerResidentId,
                    gameSeconds,
                    CreateReceivedFactText(speakerDefinition, utterance.Text),
                    Math.Max(SummaryImportance, knowledge.Importance),
                    WorldEventKind.ConversationCompleted,
                    MemorySourceKind.Conversation,
                    session.ConversationId.Value,
                    rootFactId,
                    knowledge.KnowledgeId,
                    utterance.SpeakerResidentId,
                    BuildDeliveredKnowledgeTags(knowledge),
                    isShareable: true,
                    out MemoryEntry _);
                if (stored.Failed)
                {
                    return stored;
                }
            }

            return ActionResult.Success(
                "Actual played knowledge was recorded as isolated received facts.");
        }

        private static IReadOnlyList<string> BuildSummaryTags(ConversationOutcome outcome)
        {
            var tags = new List<string>(MaximumSummaryTags);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTag("conversation", tags, unique);
            AddTag($"outcome-{outcome.ToString().ToLowerInvariant()}", tags, unique);
            return tags;
        }

        private static IReadOnlyList<string> BuildDeliveredKnowledgeTags(
            MemoryEntry knowledge)
        {
            var tags = new List<string>(MemoryEntry.MaximumTagCount);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTag("conversation", tags, unique, MemoryEntry.MaximumTagCount);
            AddTag("received-fact", tags, unique, MemoryEntry.MaximumTagCount);
            foreach (string tag in knowledge.Tags)
            {
                AddTag(tag, tags, unique, MemoryEntry.MaximumTagCount);
                if (tags.Count >= MemoryEntry.MaximumTagCount)
                {
                    break;
                }
            }

            return tags;
        }

        private static void AddTag(
            string tag,
            List<string> tags,
            HashSet<string> unique,
            int maximumTags = MaximumSummaryTags)
        {
            string normalized = (tag ?? string.Empty).Trim();
            if (tags.Count < maximumTags && normalized.Length > 0 &&
                unique.Add(normalized))
            {
                tags.Add(normalized);
            }
        }

        private static string CreateReceivedFactText(
            ResidentDefinition speaker,
            string playedText)
        {
            string prefix = $"{speaker.DisplayName}告诉我：";
            int availableLength = Math.Max(
                1,
                MemoryStore.MaximumTextLength - prefix.Length);
            string normalized = (playedText ?? string.Empty).Trim();
            string factText = normalized.Length <= availableLength
                ? normalized
                : normalized.Substring(0, availableLength);
            return prefix + factText;
        }

        private static string CreatePerspectiveSummary(
            ConversationSession session,
            ResidentDefinition owner,
            ResidentDefinition other)
        {
            var text = new StringBuilder();
            text.Append("我与")
                .Append(other.DisplayName)
                .Append("完成了一段对话：");
            foreach (ConversationUtterance utterance in session.Utterances)
            {
                if (text.Length > 0 && text[text.Length - 1] != '：')
                {
                    text.Append('；');
                }

                if (utterance.SpeakerResidentId == owner.ResidentId)
                {
                    text.Append("我说「");
                }
                else
                {
                    text.Append(other.DisplayName).Append("告诉我「");
                }

                text.Append(utterance.Text).Append('」');
            }

            text.Append("；结果是").Append(session.Outcome.Value).Append('。');
            string bounded = text.ToString();
            return bounded.Length <= MemoryStore.MaximumTextLength
                ? bounded
                : bounded.Substring(0, MemoryStore.MaximumTextLength);
        }

        private static ActionResult FirstFailure(params ActionResult[] results)
        {
            foreach (ActionResult result in results)
            {
                if (result.Failed)
                {
                    return result;
                }
            }

            return ActionResult.Success();
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }
    }
}
