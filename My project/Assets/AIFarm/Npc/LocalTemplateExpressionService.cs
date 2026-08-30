using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class LocalTemplateExpressionService : ICharacterExpressionService
    {
        public bool IsExternalAiRequired => false;

        public ActionResult CreateExpression(
            NpcExpressionTrigger trigger,
            string context,
            int variantIndex,
            out NpcExpression expression)
        {
            expression = null;
            int variant = System.Math.Abs(variantIndex) % 2;
            switch (trigger)
            {
                case NpcExpressionTrigger.CommandAccepted:
                    expression = Create(
                        trigger,
                        NpcMood.Happy,
                        "🙂",
                        variant == 0
                            ? "收到！我会把九块地照顾到全部收获。"
                            : "明白，整块农田交给我吧！");
                    break;
                case NpcExpressionTrigger.SowingStarted:
                    expression = Create(
                        trigger,
                        NpcMood.Focused,
                        "🌱",
                        variant == 0
                            ? "先从播种开始，一块一块来。"
                            : "种子准备好了，我开始播种。");
                    break;
                case NpcExpressionTrigger.WaterNeeded:
                    expression = Create(
                        trigger,
                        NpcMood.Worried,
                        "💧",
                        variant == 0
                            ? "土壤有点干，我去补水。"
                            : "发现缺水，得马上浇一浇。");
                    break;
                case NpcExpressionTrigger.WeedsFound:
                    expression = Create(
                        trigger,
                        NpcMood.Worried,
                        "🌿",
                        variant == 0
                            ? "发现杂草，我来清理。"
                            : "杂草冒出来了，不能让它抢养分。");
                    break;
                case NpcExpressionTrigger.ActionFailed:
                    string failure = string.IsNullOrWhiteSpace(context) ? "原因未知。" : context;
                    expression = Create(
                        trigger,
                        NpcMood.Worried,
                        "⚠",
                        $"这一步没成功：{failure}");
                    break;
                case NpcExpressionTrigger.WaitingForGrowth:
                    expression = Create(
                        trigger,
                        NpcMood.Tired,
                        "⏳",
                        variant == 0
                            ? "现在要给作物一点成长时间。"
                            : "暂时没有可做的事，我等它们长大。");
                    break;
                case NpcExpressionTrigger.HarvestStarted:
                    expression = Create(
                        trigger,
                        NpcMood.Happy,
                        "🥕",
                        variant == 0
                            ? "胡萝卜成熟了，开始收获！"
                            : "长得不错，该把胡萝卜收起来了。");
                    break;
                case NpcExpressionTrigger.GoalCompleted:
                    expression = Create(
                        trigger,
                        NpcMood.Proud,
                        "★",
                        variant == 0
                            ? "九块地全部完成，胡萝卜已经收好！"
                            : "任务完成！这片农田照顾得很漂亮。");
                    break;
                default:
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"Unsupported expression trigger: {trigger}.");
            }

            return ActionResult.Success("Local NPC expression generated.");
        }

        private static NpcExpression Create(
            NpcExpressionTrigger trigger,
            NpcMood mood,
            string emoji,
            string text)
        {
            return new NpcExpression(trigger, mood, emoji, text);
        }
    }
}
