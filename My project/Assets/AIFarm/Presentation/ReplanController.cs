using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using UnityEngine;

namespace AIFarm.Presentation
{
    public enum ReplanStatus
    {
        Idle,
        Running,
        Completed,
        Failed
    }

    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    public sealed class ReplanController : MonoBehaviour
    {
        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private NpcPlanExecutor executor;

        private readonly HashSet<int> harvestedPlotNumbers = new HashSet<int>();
        private LocalIntentInterpreter interpreter;
        private DeterministicFarmPlanner planner;
        private FarmGoalSpec activeGoal;

        public ReplanStatus Status { get; private set; } = ReplanStatus.Idle;

        public FarmGoalSpec ActiveGoal => activeGoal;

        public bool IsGoalActive => Status == ReplanStatus.Running;

        public int HarvestedPlotCount => harvestedPlotNumbers.Count;

        public string CurrentGoalText { get; private set; } = "Idle";

        public string CurrentDecisionReason { get; private set; } = string.Empty;

        public string NpcExpression { get; private set; } = "等待你的种田目标。";

        public string LastFailureReason { get; private set; } = string.Empty;

        public bool IsInitialized { get; private set; }

        public ActionResult Configure(GameBootstrap gameBootstrap, NpcPlanExecutor planExecutor)
        {
            if (gameBootstrap == null || planExecutor == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "ReplanController requires bootstrap and executor components.");
            }

            bootstrap = gameBootstrap;
            executor = planExecutor;
            return ActionResult.Success("ReplanController scene references configured.");
        }

        public ActionResult Initialize()
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "ReplanController requires an initialized GameBootstrap.");
            }

            var context = new NpcActionContext(
                bootstrap.Field,
                bootstrap.Inventory,
                bootstrap.Clock,
                bootstrap.Simulation);
            return Initialize(context, executor, bootstrap.Mode);
        }

        public ActionResult Initialize(
            NpcActionContext context,
            NpcPlanExecutor planExecutor,
            DemoMode demoMode)
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "ReplanController has already been initialized.");
            }

            if (context == null || planExecutor == null || !planExecutor.IsInitialized || demoMode == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "ReplanController requires an action context, initialized executor, and DemoMode.");
            }

            executor = planExecutor;
            interpreter = new LocalIntentInterpreter();
            var world = new WorldStateQuery(context.Field, context.Inventory, context.Clock);
            planner = new DeterministicFarmPlanner(world, demoMode);
            executor.ActionCompleted += HandleActionCompleted;
            IsInitialized = true;
            return ActionResult.Success("Offline replanning initialized.");
        }

        public ActionResult SubmitGoal(string command)
        {
            if (!IsInitialized)
            {
                ActionResult initialization = Initialize();
                if (initialization.Failed)
                {
                    return initialization;
                }
            }

            if (IsGoalActive || executor.IsBusy)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "当前种田目标尚未完成，请等待后再提交。");
            }

            ActionResult interpretation = interpreter.TryInterpret(command, out FarmGoalSpec goal);
            if (interpretation.Failed)
            {
                return interpretation;
            }

            harvestedPlotNumbers.Clear();
            activeGoal = goal;
            Status = ReplanStatus.Running;
            CurrentGoalText = goal.Summary;
            CurrentDecisionReason = "目标已在本地解释，准备查询世界状态。";
            NpcExpression = "收到，我会照顾九块胡萝卜直到全部收获。";
            LastFailureReason = string.Empty;
            return TickReplan();
        }

        public ActionResult TickReplan()
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "ReplanController is not initialized.");
            }

            if (!IsGoalActive)
            {
                return ActionResult.Success("No offline goal requires replanning.");
            }

            if (executor.Status == NpcExecutionStatus.Failed)
            {
                return FailGoal(executor.LastFailureReason);
            }

            if (executor.IsBusy)
            {
                return ActionResult.Success("Waiting for the current atomic action to complete.");
            }

            ActionResult planned = planner.DecideNext(activeGoal, harvestedPlotNumbers, out FarmPlanDecision decision);
            if (planned.Failed)
            {
                return FailGoal(planned.Message, planned.FailureReason);
            }

            CurrentDecisionReason = decision.Reason;
            if (decision.Kind == FarmPlanDecisionKind.GoalCompleted)
            {
                Status = ReplanStatus.Completed;
                CurrentGoalText = $"Completed: {activeGoal.Summary}";
                NpcExpression = "九块地都完成了，胡萝卜已经收好。";
                return ActionResult.Success("离线端到端种田目标已完成。");
            }

            ActionResult queued = executor.Enqueue(decision.Action);
            if (queued.Failed)
            {
                return FailGoal(queued.Message, queued.FailureReason);
            }

            NpcExpression = ExpressionFor(decision.Action, decision.Kind);
            return queued;
        }

        private void Start()
        {
            if (IsInitialized)
            {
                return;
            }

            ActionResult result = Initialize();
            if (result.Failed)
            {
                Status = ReplanStatus.Failed;
                LastFailureReason = result.Message;
                NpcExpression = $"无法启动离线规划：{result.Message}";
                Debug.LogWarning(result.Message, this);
            }
        }

        private void Update()
        {
            if (IsInitialized && IsGoalActive && !executor.IsBusy)
            {
                TickReplan();
            }
        }

        private void OnDestroy()
        {
            if (executor != null)
            {
                executor.ActionCompleted -= HandleActionCompleted;
            }
        }

        private void HandleActionCompleted(INpcAction action, ActionResult result)
        {
            if (!IsGoalActive || result.Failed || action == null)
            {
                return;
            }

            if (action is HarvestAction harvest)
            {
                harvestedPlotNumbers.Add(harvest.PlotNumber);
            }
        }

        private ActionResult FailGoal(
            string reason,
            ActionFailureReason failureReason = ActionFailureReason.InvalidState)
        {
            Status = ReplanStatus.Failed;
            LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "离线规划失败。" : reason;
            CurrentDecisionReason = LastFailureReason;
            NpcExpression = $"目标失败：{LastFailureReason}";
            return ActionResult.Failure(failureReason, LastFailureReason);
        }

        private static string ExpressionFor(INpcAction action, FarmPlanDecisionKind kind)
        {
            if (kind == FarmPlanDecisionKind.WaitAndRecheck)
            {
                return "作物还在变化，我会稍等后重新检查。";
            }

            if (action is SowAction)
            {
                return "先把空地逐块种上胡萝卜。";
            }

            if (action is FertilizeAction)
            {
                return "正在给已播种的土地施肥。";
            }

            if (action is WaterAction)
            {
                return "检测到水分不足，正在浇水。";
            }

            if (action is WeedAction)
            {
                return "杂草出现了，马上清理。";
            }

            if (action is HarvestAction)
            {
                return "胡萝卜成熟了，开始收获。";
            }

            return "正在执行下一步农事动作。";
        }
    }
}
