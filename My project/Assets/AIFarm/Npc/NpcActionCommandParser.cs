using System;
using System.Globalization;
using AIFarm.Core;
using AIFarm.Farming;

namespace AIFarm.Npc
{
    public static class NpcActionCommandParser
    {
        private const string Usage =
            "Use one atomic command: move 1, sow 1, water 1, fertilize 1, weed 1, " +
            "harvest 1, or wait 1. Chinese verbs are also supported.";

        public static ActionResult TryParse(string command, out INpcAction action)
        {
            action = null;
            if (string.IsNullOrWhiteSpace(command))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Command is empty. {Usage}");
            }

            string input = command.Trim();
            if (TryExtractArgument(input, new[] { "move to", "goto", "move", "移动到", "移动", "前往" }, out string moveArgument))
            {
                return CreatePlotAction(moveArgument, plotNumber => new MoveToPlotAction(plotNumber), out action);
            }

            if (TryExtractArgument(input, new[] { "sow", "plant", "播种" }, out string sowArgument))
            {
                return CreatePlotAction(sowArgument, plotNumber => new SowAction(plotNumber), out action);
            }

            if (TryExtractArgument(input, new[] { "water", "浇水" }, out string waterArgument))
            {
                return CreatePlotAction(waterArgument, plotNumber => new WaterAction(plotNumber), out action);
            }

            if (TryExtractArgument(input, new[] { "fertilize", "fertilise", "施肥" }, out string fertilizerArgument))
            {
                return CreatePlotAction(
                    fertilizerArgument,
                    plotNumber => new FertilizeAction(plotNumber),
                    out action);
            }

            if (TryExtractArgument(input, new[] { "weed", "除草" }, out string weedArgument))
            {
                return CreatePlotAction(weedArgument, plotNumber => new WeedAction(plotNumber), out action);
            }

            if (TryExtractArgument(input, new[] { "harvest", "收获" }, out string harvestArgument))
            {
                return CreatePlotAction(harvestArgument, plotNumber => new HarvestAction(plotNumber), out action);
            }

            if (TryExtractArgument(input, new[] { "wait", "等待" }, out string waitArgument))
            {
                return CreateWaitAction(waitArgument, out action);
            }

            return ActionResult.Failure(
                ActionFailureReason.InvalidArgument,
                $"Unsupported command '{command}'. {Usage}");
        }

        private static ActionResult CreatePlotAction(
            string argument,
            Func<int, INpcAction> factory,
            out INpcAction action)
        {
            action = null;
            string normalizedArgument = NormalizePlotArgument(argument);
            if (!int.TryParse(normalizedArgument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plotNumber) ||
                plotNumber < 1 ||
                plotNumber > FarmField.PlotCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Plot number must be between 1 and {FarmField.PlotCount}. {Usage}");
            }

            action = factory(plotNumber);
            return ActionResult.Success($"Parsed {action.DisplayName}.");
        }

        private static ActionResult CreateWaitAction(string argument, out INpcAction action)
        {
            action = null;
            string normalizedArgument = argument.Trim().Trim(':', '：');
            if (!float.TryParse(
                    normalizedArgument,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float seconds) ||
                float.IsNaN(seconds) ||
                float.IsInfinity(seconds) ||
                seconds <= 0f ||
                seconds > 60f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Wait duration must be greater than 0 and no more than 60 seconds.");
            }

            action = new WaitAction(seconds);
            return ActionResult.Success($"Parsed {action.DisplayName}.");
        }

        private static bool TryExtractArgument(string input, string[] aliases, out string argument)
        {
            foreach (string alias in aliases)
            {
                if (!input.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                argument = input.Substring(alias.Length).Trim();
                return true;
            }

            argument = string.Empty;
            return false;
        }

        private static string NormalizePlotArgument(string argument)
        {
            return argument
                .Trim()
                .Trim(':', '：')
                .ToLowerInvariant()
                .Replace("plot", string.Empty)
                .Replace("第", string.Empty)
                .Replace("号地", string.Empty)
                .Replace("地块", string.Empty)
                .Replace("号", string.Empty)
                .Trim();
        }
    }
}
