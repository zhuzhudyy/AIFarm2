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
        private NpcActionContext actionContext;
        private WorldEventLog eventLog;
        private NpcExpressionDirector expressionDirector;
        private string fallbackExpressionText = "等待你的种田目标。";

        public ReplanStatus Status { get; private set; } = ReplanStatus.Idle;

        public FarmGoalSpec ActiveGoal => activeGoal;

        public bool IsGoalActive => Status == ReplanStatus.Running;

        public int HarvestedPlotCount => harvestedPlotNumbers.Count;

        public string CurrentGoalText { get; private set; } = "Idle";

        public string CurrentDecisionReason { get; private set; } = string.Empty;

        public string NpcExpression => expressionDirector?.Current?.Text ?? fallbackExpressionText;

        public string CurrentEmoji =>
            (expressionDirector?.Current ?? expressionDirector?.Latest)?.Emoji ?? "…";

        public NpcMood CurrentMood =>
            (expressionDirector?.Current ?? expressionDirector?.Latest)?.Mood ?? NpcMood.Focused;

        public IReadOnlyList<NpcExpression> RecentExpressions =>
            expressionDirector?.RecentExpressions ?? System.Array.Empty<NpcExpression>();

        public WorldEventLog WorldEvents => eventLog;

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
                bootstrap.Simulation,
                bootstrap.Events);
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
            actionContext = context;
            eventLog = context.EventLog ?? new WorldEventLog();
            interpreter = new LocalIntentInterpreter();
            var world = new WorldStateQuery(context.Field, context.Inventory, context.Clock);
            planner = new DeterministicFarmPlanner(world, demoMode);
            expressionDirector = new NpcExpressionDirector(
                new LocalTemplateExpressionService(),
                demoMode.ExpressionCooldownSeconds,
                demoMode.ExpressionDisplaySeconds);
            executor.ActionStarted += HandleActionStarted;
            executor.ActionCompleted += HandleActionCompleted;
            executor.ActionFailed += HandleActionFailed;
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
            fallbackExpressionText = "收到，我会照顾九块胡萝卜直到全部收获。";
            LastFailureReason = string.Empty;
            RecordEvent(
                WorldEventKind.CommandAccepted,
                $"接受用户命令：{goal.Summary}。");
            TriggerExpression(NpcExpressionTrigger.CommandAccepted, goal.Summary);
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
            RecordEvent(
                WorldEventKind.PlanDecision,
                $"规划原因：{decision.Reason}",
                decision.Action?.TargetPlotNumber);
            if (decision.Kind == FarmPlanDecisionKind.GoalCompleted)
            {
                Status = ReplanStatus.Completed;
                CurrentGoalText = $"Completed: {activeGoal.Summary}";
                fallbackExpressionText = "九块地都完成了，胡萝卜已经收好。";
                RecordEvent(
                    WorldEventKind.GoalCompleted,
                    "九块目标土地已经全部收获，离线任务完成。");
                TriggerExpression(
                    NpcExpressionTrigger.GoalCompleted,
                    activeGoal.Summary,
                    interrupt: true);
                return ActionResult.Success("离线端到端种田目标已完成。");
            }

            ActionResult queued = executor.Enqueue(decision.Action);
            if (queued.Failed)
            {
                return FailGoal(queued.Message, queued.FailureReason);
            }

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
                fallbackExpressionText = $"无法启动离线规划：{result.Message}";
                Debug.LogWarning(result.Message, this);
            }
        }

        private void Update()
        {
            expressionDirector?.Advance(UnityEngine.Time.unscaledDeltaTime);
            if (IsInitialized && IsGoalActive && !executor.IsBusy)
            {
                TickReplan();
            }
        }

        private void OnDestroy()
        {
            if (executor != null)
            {
                executor.ActionStarted -= HandleActionStarted;
                executor.ActionCompleted -= HandleActionCompleted;
                executor.ActionFailed -= HandleActionFailed;
            }
        }

        private void HandleActionStarted(INpcAction action)
        {
            if (action is SowAction)
            {
                TriggerExpression(NpcExpressionTrigger.SowingStarted, CurrentDecisionReason);
            }
            else if (action is WaterAction)
            {
                TriggerExpression(NpcExpressionTrigger.WaterNeeded, CurrentDecisionReason);
            }
            else if (action is WeedAction)
            {
                TriggerExpression(NpcExpressionTrigger.WeedsFound, CurrentDecisionReason);
            }
            else if (action is WaitAction)
            {
                TriggerExpression(NpcExpressionTrigger.WaitingForGrowth, CurrentDecisionReason);
            }
            else if (action is HarvestAction)
            {
                TriggerExpression(NpcExpressionTrigger.HarvestStarted, CurrentDecisionReason);
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

        private void HandleActionFailed(INpcAction action, ActionResult result)
        {
            TriggerExpression(
                NpcExpressionTrigger.ActionFailed,
                result.Message,
                interrupt: true);
        }

        private ActionResult FailGoal(
            string reason,
            ActionFailureReason failureReason = ActionFailureReason.InvalidState)
        {
            Status = ReplanStatus.Failed;
            LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "离线规划失败。" : reason;
            CurrentDecisionReason = LastFailureReason;
            fallbackExpressionText = $"目标失败：{LastFailureReason}";
            RecordEvent(
                WorldEventKind.ActionFailed,
                $"目标失败：{LastFailureReason}");
            TriggerExpression(
                NpcExpressionTrigger.ActionFailed,
                LastFailureReason,
                interrupt: true);
            return ActionResult.Failure(failureReason, LastFailureReason);
        }

        private ActionResult TriggerExpression(
            NpcExpressionTrigger trigger,
            string context,
            bool interrupt = false)
        {
            if (expressionDirector == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC expression director is not initialized.");
            }

            return expressionDirector.Trigger(
                trigger,
                context,
                UnityEngine.Time.unscaledTime,
                interrupt);
        }

        private void RecordEvent(
            WorldEventKind kind,
            string message,
            int? plotNumber = null)
        {
            if (eventLog == null || actionContext == null)
            {
                return;
            }

            eventLog.Record(
                actionContext.Clock.ElapsedGameSeconds,
                kind,
                message,
                plotNumber);
        }
    }
}
