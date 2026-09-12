using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Ai
{
    public interface IResidentTaskGateway
    {
        IEnumerator InterpretResidentTask(ResidentId owner, string command, string[] targets,
            string[] residents, Action<AiGatewayResult<ResidentTaskSpec>> completed);
    }

    [Serializable]
    public sealed class ResidentTaskSpec
    {
        public string resident_id;
        public string task_id;
        public string task_type;
        public string target_id;
        public string target_resident_id;
        public int[] target_plot_numbers = Array.Empty<int>();
        public string crop = "carrot";
        public bool repeat;
        public int quantity = 1;
        public string summary;
        public string provider = "local";
        public long config_version;
        public string request_id;
        public string model;
        public string error_code;
        public string error_message;

        public ActionResult Validate(ResidentId owner)
        {
            if (resident_id != owner.Value || string.IsNullOrWhiteSpace(task_id) ||
                task_id.Length > 100 || string.IsNullOrWhiteSpace(summary) || summary.Length > 500 ||
                quantity < 1 || quantity > 99 || !AllowedTypes.Contains(task_type ?? "") ||
                (target_id ?? "").Length > 100 || (crop != "carrot" &&
                    (task_type == "Sow" || task_type == "TendFarm")))
            {
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "任务身份、类型或参数无效。");
            }
            var seen = new HashSet<int>();
            foreach (int plot in target_plot_numbers ?? Array.Empty<int>())
            {
                if (plot < 1 || plot > 9 || !seen.Add(plot))
                {
                    return ActionResult.Failure(ActionFailureReason.InvalidResponse, "地块必须在 1—9 范围且不能重复。");
                }
            }
            if (!string.IsNullOrEmpty(target_resident_id) &&
                (!ResidentId.TryCreate(target_resident_id, out ResidentId other) || other == owner))
            {
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "交谈对象无效。");
            }
            if (task_type == "Chat" && (repeat || quantity != 1))
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "聊天任务目前支持一次完整会话，不支持循环或多次数量。");
            return ActionResult.Success();
        }

        public static readonly HashSet<string> AllowedTypes = new HashSet<string>
        { "Move", "Sow", "Water", "Fertilize", "Weed", "Harvest", "TendFarm", "Fish", "PickFruit", "Chat", "Stop" };

        public static ResidentTaskSpec Create(ResidentId owner, string type, string target = null)
        {
            return new ResidentTaskSpec { resident_id = owner.Value, task_id = Guid.NewGuid().ToString("N"),
                task_type = type, target_id = target ?? "", summary = type };
        }
    }

    public static class LocalResidentTaskParser
    {
        private const string QuantityNumber = @"[+-]?\d+(?:\.\d+)?|[零〇一二两三四五六七八九十百千]+";
        private static readonly Regex QuantityPhrase = new Regex(
            @"(?<n>" + QuantityNumber + @")\s*(?:次|轮|遍|条|尾|个|颗|枚|份|times?\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static ActionResult TryParse(ResidentId owner, string command, out ResidentTaskSpec task)
        {
            task = null;
            string input = (command ?? "").Trim();
            if (input.Length == 0 || input.Length > 500)
                return ActionResult.Failure(ActionFailureReason.InvalidArgument, "请输入 1—500 字的居民指令。");
            string type = Contains(input, "停止", "取消", "stop", "自由活动") ? "Stop" :
                Contains(input, "钓鱼", "垂钓", "fish") ||
                    Regex.IsMatch(input, @"(?:钓|捕)\s*(?:" + QuantityNumber + @")\s*(?:条|尾)\s*鱼") ? "Fish" :
                Contains(input, "摘果", "采果", "摘苹果", "采摘", "pick") ||
                    Regex.IsMatch(input, @"(?:摘|采)\s*(?:" + QuantityNumber + @")\s*(?:个|颗|枚|份)\s*(?:苹果|水果|果实|果)") ? "PickFruit" :
                Contains(input, "聊天", "交谈", "聊聊", "chat") ? "Chat" :
                Contains(input, "照料", "照顾", "种满", "循环", "全周期", "从种", "种田", "持续", "tend") ? "TendFarm" :
                Contains(input, "播种", "种植", "种胡萝卜", "sow") ? "Sow" :
                Contains(input, "浇水", "water") ? "Water" : Contains(input, "施肥", "fertilize") ? "Fertilize" :
                Contains(input, "除草", "weed") ? "Weed" : Contains(input, "收获", "harvest") ? "Harvest" :
                Contains(input, "前往", "移动", "去", "move") ? "Move" : null;
            if (type == null)
                return ActionResult.Failure(ActionFailureReason.InvalidArgument, "暂不支持这条指令。可下令移动、农活、照料、钓鱼、摘果、聊天或停止。");
            task = ResidentTaskSpec.Create(owner, type);
            task.summary = input;
            task.repeat = Contains(input, "持续", "循环", "一直", "反复");
            MatchCollection quantities = QuantityPhrase.Matches(input);
            bool farmTask = type == "Sow" || type == "Water" || type == "Fertilize" ||
                type == "Weed" || type == "Harvest" || type == "TendFarm";
            if (farmTask)
            {
                foreach (Match quantity in quantities)
                    if (!TryParseQuantity(quantity.Groups["n"].Value, out int count) || count != 1)
                        return ActionResult.Failure(ActionFailureReason.InvalidArgument,
                            "农事请指定地块，或使用持续照料；有限次数参数目前仅支持钓鱼和摘果。");
                var plots = new List<int>();
                // Read complete numbers, and never mistake "浇水3次" for plot 3.
                foreach (Match match in Regex.Matches(input,
                    @"(?:第|地块|地|田|plot|播种|浇水|施肥|除草|收获)[ #第]*(?<n>\d+)(?!\d)(?!\s*(?:次|轮|遍|条|尾|个|颗|枚|份))|(?<n>\d+)\s*[号块]",
                    RegexOptions.IgnoreCase))
                {
                    if (!int.TryParse(match.Groups["n"].Value, out int plot) || plot < 1 || plot > 9)
                        return ActionResult.Failure(ActionFailureReason.InvalidArgument, "不存在该地块；农田编号为 1—9。");
                    if (!plots.Contains(plot)) plots.Add(plot);
                }
                task.target_plot_numbers = plots.ToArray();
            }
            if (type == "Fish" || type == "PickFruit")
            {
                int? requestedCount = null;
                foreach (Match quantity in quantities)
                {
                    if (!TryParseQuantity(quantity.Groups["n"].Value, out int count))
                        return ActionResult.Failure(ActionFailureReason.InvalidArgument, "钓鱼和摘果数量必须是 1—99 的整数。");
                    if (requestedCount.HasValue && requestedCount.Value != count)
                        return ActionResult.Failure(ActionFailureReason.InvalidArgument, "请为一次指令指定一个明确的总数量。");
                    requestedCount = count;
                }
                if (requestedCount.HasValue)
                {
                    task.quantity = requestedCount.Value;
                    // An explicit total is finite even when phrased as "持续钓鱼3次".
                    task.repeat = false;
                }
                Match target = Regex.Match(input, "(?<n>\\d+)号?(?:钓位|果树)|(?:钓位|果树|fishing-|fruit-)[ #第]*(?<n>\\d+)");
                if (target.Success && (!int.TryParse(target.Groups["n"].Value, out int targetNumber) || targetNumber < 1 || targetNumber > (type == "Fish" ? 2 : 4)))
                    return ActionResult.Failure(ActionFailureReason.InvalidArgument, "指定的钓位或果树不存在。");
                if (target.Success) task.target_id = (type == "Fish" ? "fishing-" : "fruit-") + target.Groups["n"].Value;
                task.target_plot_numbers = Array.Empty<int>();
            }
            if (type == "Move")
            {
                task.target_id = Contains(input, "井", "补给") ? "well" : Contains(input, "果园", "果树") ? "orchard" :
                    Contains(input, "池塘", "河岸", "钓鱼") ? "pond" : Contains(input, "农田", "田里") ? "farm" :
                    Contains(input, "广场") ? "plaza" : Contains(input, "家", "住宅", "休息") ? "home" :
                    Contains(input, "工坊", "workshop") ? "workshop" : Contains(input, "图书", "library") ? "library" : "";
                if (task.target_id.Length == 0) return ActionResult.Failure(ActionFailureReason.InvalidArgument, "未找到地点，请指定农田、广场、水井、池塘、果园或住宅。");
            }
            if (type == "Chat")
            {
                foreach (Match quantity in quantities)
                    if (!TryParseQuantity(quantity.Groups["n"].Value, out int count) || count != 1)
                        return ActionResult.Failure(ActionFailureReason.InvalidArgument, "聊天任务目前支持一次完整会话，请勿指定多次或循环。");
                foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
                    if (input.Contains(definition.Persona.Name)) task.target_resident_id = definition.ResidentId.Value;
                if (string.IsNullOrEmpty(task.target_resident_id))
                    return ActionResult.Failure(ActionFailureReason.InvalidArgument, "请写明聊天居民的姓名。");
            }
            return task.Validate(owner);
        }

        private static bool TryParseQuantity(string text, out int quantity)
        {
            if (int.TryParse(text, out quantity)) return quantity >= 1 && quantity <= 99;
            string normalized = text.Replace('两', '二');
            if (!Regex.IsMatch(normalized, @"^(?:[一二三四五六七八九]|[一二三四五六七八九]?十[一二三四五六七八九]?)$"))
                return false;
            const string digits = "零一二三四五六七八九";
            int tens = normalized.IndexOf('十');
            quantity = tens < 0 ? digits.IndexOf(normalized[0]) :
                (tens == 0 ? 1 : digits.IndexOf(normalized[0])) * 10 +
                (tens == normalized.Length - 1 ? 0 : digits.IndexOf(normalized[tens + 1]));
            return quantity >= 1 && quantity <= 99;
        }

        private static bool Contains(string value, params string[] tokens)
        {
            foreach (string token in tokens) if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
