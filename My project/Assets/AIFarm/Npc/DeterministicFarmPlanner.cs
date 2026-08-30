using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;

namespace AIFarm.Npc
{
    public enum FarmPlanDecisionKind
    {
        ExecuteAction,
        WaitAndRecheck,
        GoalCompleted
    }

    public readonly struct FarmPlanDecision
    {
        public FarmPlanDecision(FarmPlanDecisionKind kind, INpcAction action, string reason)
        {
            Kind = kind;
            Action = action;
            Reason = reason ?? string.Empty;
        }

        public FarmPlanDecisionKind Kind { get; }

        public INpcAction Action { get; }

        public string Reason { get; }
    }

    public sealed class DeterministicFarmPlanner
    {
        private readonly WorldStateQuery world;
        private readonly DemoMode demoMode;

        public DeterministicFarmPlanner(WorldStateQuery world, DemoMode demoMode)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.demoMode = demoMode ?? throw new ArgumentNullException(nameof(demoMode));
        }

        public ActionResult DecideNext(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers,
            out FarmPlanDecision decision)
        {
            decision = default;
            if (goal == null || harvestedPlotNumbers == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Planning requires a goal and harvested-plot progress.");
            }

            if (world.AreAllTargetsHarvested(goal, harvestedPlotNumbers))
            {
                decision = new FarmPlanDecision(
                    FarmPlanDecisionKind.GoalCompleted,
                    null,
                    "所有目标土地已经收获。");
                return ActionResult.Success(decision.Reason);
            }

            FarmPlot target = world.FindFirstEmptyTarget(goal, harvestedPlotNumbers);
            if (target != null)
            {
                if (!world.Inventory.Has(InventoryItem.CarrotSeed))
                {
                    return MissingResource("胡萝卜种子");
                }

                return Select(
                    new SowAction(target.PlotNumber, demoMode.SowActionSeconds),
                    "存在需要播种的空地。",
                    out decision);
            }

            target = world.FindFirstUnfertilizedTarget(goal, harvestedPlotNumbers);
            if (target != null)
            {
                if (!world.Inventory.Has(InventoryItem.Fertilizer))
                {
                    return MissingResource("肥料");
                }

                return Select(
                    new FertilizeAction(target.PlotNumber, demoMode.FertilizeActionSeconds),
                    "已播种土地尚未施肥。",
                    out decision);
            }

            target = world.FindFirstUnderwateredTarget(goal, harvestedPlotNumbers);
            if (target != null)
            {
                if (!world.Inventory.Has(InventoryItem.Water))
                {
                    return MissingResource("水");
                }

                return Select(
                    new WaterAction(target.PlotNumber, demoMode.WaterActionSeconds),
                    "土地水分不足。",
                    out decision);
            }

            target = world.FindFirstWeedyTarget(goal, harvestedPlotNumbers);
            if (target != null)
            {
                return Select(
                    new WeedAction(target.PlotNumber, demoMode.WeedActionSeconds),
                    "检测到杂草。",
                    out decision);
            }

            target = world.FindFirstMatureTarget(goal, harvestedPlotNumbers);
            if (target != null)
            {
                if (world.Inventory.GetCount(InventoryItem.Carrot) == int.MaxValue)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.CapacityExceeded,
                        "胡萝卜背包已满，无法完成收获。");
                }

                return Select(
                    new HarvestAction(target.PlotNumber, demoMode.HarvestActionSeconds),
                    "检测到成熟胡萝卜。",
                    out decision);
            }

            var wait = new WaitAction(demoMode.WaitActionSeconds);
            decision = new FarmPlanDecision(
                FarmPlanDecisionKind.WaitAndRecheck,
                wait,
                "当前没有合法农事动作，等待世界状态变化后重新检查。");
            return ActionResult.Success(decision.Reason);
        }

        private static ActionResult Select(
            INpcAction action,
            string reason,
            out FarmPlanDecision decision)
        {
            decision = new FarmPlanDecision(FarmPlanDecisionKind.ExecuteAction, action, reason);
            return ActionResult.Success(reason);
        }

        private static ActionResult MissingResource(string resourceName)
        {
            return ActionResult.Failure(
                ActionFailureReason.InsufficientResource,
                $"完成目标所需的{resourceName}不足。");
        }
    }
}
