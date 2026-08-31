namespace AIFarm.Npc
{
    public enum NpcReflectionOutcome
    {
        InProgress,
        Completed,
        Failed
    }

    public sealed class NpcReflection
    {
        public NpcReflection(
            string goalId,
            NpcReflectionOutcome outcome,
            NpcMood mood,
            string emoji,
            string text)
        {
            GoalId = goalId ?? string.Empty;
            Outcome = outcome;
            Mood = mood;
            Emoji = emoji ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string GoalId { get; }

        public NpcReflectionOutcome Outcome { get; }

        public NpcMood Mood { get; }

        public string Emoji { get; }

        public string Text { get; }
    }
}
