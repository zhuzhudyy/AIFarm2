using System;
using AIFarm.Core;

namespace AIFarm.Npc
{
    public sealed class LocalIntentInterpreter
    {
        public ActionResult TryInterpret(string input, out FarmGoalSpec goal)
        {
            goal = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "请输入离线种田目标。");
            }

            string normalized = Normalize(input);
            bool asksForCare = normalized.Contains("照顾好") || normalized.Contains("照顾到收获");
            bool asksForFullHarvest =
                (normalized.Contains("种满") &&
                    (normalized.Contains("收掉") || normalized.Contains("收获") || normalized.Contains("收成"))) ||
                (normalized.Contains("所有空地") &&
                    (normalized.Contains("收掉") || normalized.Contains("收获") || normalized.Contains("收成"))) ||
                (normalized.Contains("胡萝卜") &&
                    (normalized.Contains("成熟后收") || normalized.Contains("全部收") || normalized.Contains("完成浇水"))) ||
                (normalized.Contains("全部种上胡萝卜") && normalized.Contains("收获"));
            bool namesRequiredCare =
                normalized.Contains("胡萝卜") &&
                normalized.Contains("浇水") &&
                normalized.Contains("施肥") &&
                normalized.Contains("除草") &&
                (normalized.Contains("收掉") || normalized.Contains("收获") || normalized.Contains("收成"));

            if (!asksForCare && !asksForFullHarvest && !namesRequiredCare)
            {
                return ActionResult.Failure(
                    ActionFailureReason.UnsupportedIntent,
                    "当前离线模式只支持整块 3×3 胡萝卜全周期目标。");
            }

            goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            return ActionResult.Success("已转换为整块农田胡萝卜全周期目标。");
        }

        private static string Normalize(string input)
        {
            return input
                .Trim()
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("，", string.Empty)
                .Replace(",", string.Empty)
                .Replace("。", string.Empty)
                .Replace(".", string.Empty)
                .Replace("！", string.Empty)
                .Replace("!", string.Empty)
                .Replace("？", string.Empty)
                .Replace("?", string.Empty);
        }
    }
}
