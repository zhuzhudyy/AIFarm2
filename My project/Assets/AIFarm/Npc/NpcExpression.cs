namespace AIFarm.Npc
{
    public enum NpcMood
    {
        Happy,
        Focused,
        Worried,
        Tired,
        Proud
    }

    public enum NpcExpressionTrigger
    {
        CommandAccepted,
        SowingStarted,
        WaterNeeded,
        WeedsFound,
        ActionFailed,
        WaitingForGrowth,
        HarvestStarted,
        GoalCompleted
    }

    public sealed class NpcExpression
    {
        public NpcExpression(
            NpcExpressionTrigger trigger,
            NpcMood mood,
            string emoji,
            string text)
        {
            Trigger = trigger;
            Mood = mood;
            Emoji = emoji ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public NpcExpressionTrigger Trigger { get; }

        public NpcMood Mood { get; }

        public string Emoji { get; }

        public string Text { get; }
    }
}
