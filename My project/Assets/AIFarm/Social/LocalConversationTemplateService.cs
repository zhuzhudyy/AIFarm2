using System;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Social
{
    public sealed class LocalConversationTemplateService
    {
        private readonly Func<ConversationSession, ConversationOutcome> outcomeSelector;

        public LocalConversationTemplateService(
            Func<ConversationSession, ConversationOutcome> deterministicOutcomeSelector = null)
        {
            outcomeSelector = deterministicOutcomeSelector;
        }

        public ActionResult CreateLine(
            ConversationSession session,
            ResidentDefinition speaker,
            ResidentDefinition listener,
            out string line)
        {
            line = string.Empty;
            if (session == null || session.State != ConversationState.Active ||
                speaker == null || listener == null ||
                speaker.ResidentId != session.TurnOwnerResidentId ||
                !session.IsParticipant(listener.ResidentId) ||
                speaker.ResidentId == listener.ResidentId)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A local line requires the active speaker and the other participant.");
            }

            int templateIndex = session.DeliveredSentenceCount;
            switch (templateIndex)
            {
                case 0:
                    line = $"{listener.DisplayName}，我是{speaker.DisplayName}。作为{speaker.Persona.Role}，今天想和你聊聊{speaker.Persona.Preference}。";
                    break;
                case 1:
                    line = $"{speaker.DisplayName}，我记得你喜欢{listener.Persona.Preference}；我的习惯是{speaker.Persona.WorkHabit}。";
                    break;
                case 2:
                    line = $"{listener.DisplayName}，我说话通常{speaker.Persona.SpeakingStyle}，但很愿意听听你的看法。";
                    break;
                case 3:
                    line = $"{speaker.DisplayName}，如果遇到{listener.Persona.Dislike}，我们可以按各自擅长的方式互相帮忙。";
                    break;
                case 4:
                    line = $"{listener.DisplayName}，从{speaker.Persona.Role}的角度，我会留意这件事。";
                    break;
                default:
                    line = $"{speaker.DisplayName}，今天聊得很具体；下次也请把{listener.Persona.Preference}的近况告诉我。";
                    break;
            }

            if (line.Length > 300)
            {
                line = line.Substring(0, 300);
            }

            return ActionResult.Success("A deterministic persona-aware local line was created.");
        }

        public ConversationOutcome SelectOutcome(ConversationSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (outcomeSelector != null)
            {
                ConversationOutcome selected = outcomeSelector(session);
                if (!Enum.IsDefined(typeof(ConversationOutcome), selected))
                {
                    throw new InvalidOperationException(
                        "The deterministic outcome selector returned an unsupported tag.");
                }

                return selected;
            }

            string key = session.FirstResidentId.Value + "|" +
                session.SecondResidentId.Value + "|" + session.TargetSentenceCount;
            uint hash = 2166136261u;
            for (int index = 0; index < key.Length; index++)
            {
                hash ^= key[index];
                hash *= 16777619u;
            }

            return (ConversationOutcome)(hash % 5u);
        }
    }
}
