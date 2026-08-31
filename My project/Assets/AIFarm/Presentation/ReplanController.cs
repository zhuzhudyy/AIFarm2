using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
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
        private readonly HashSet<NpcExpressionTrigger> pendingExpressionTriggers =
            new HashSet<NpcExpressionTrigger>();
        private DeterministicFarmPlanner planner;
        private NpcActionContext actionContext;
        private WorldEventLog eventLog;
        private NpcExpressionDirector expressionDirector;
        private ObservationService observationService;
        private ResidentRuntimeState runtimeState;
        private ResidentRegistry residentRegistry;
        private ReflectionService reflectionService;
        private IAiGatewayClient aiGatewayClient;
        private DemoMode demoMode;
        private bool gatewayRequestPending;
        private bool reflectionRequestPending;
        private int activeCycleNumber;
        private string fallbackExpressionText = "等待你的种田目标。";

        public ReplanStatus Status { get; private set; } = ReplanStatus.Idle;

        public FarmGoalSpec ActiveGoal => runtimeState?.CurrentGoal;

        public bool IsGoalActive => Status == ReplanStatus.Running;

        public int HarvestedPlotCount => harvestedPlotNumbers.Count;

        public IReadOnlyCollection<int> HarvestedPlotNumbers => harvestedPlotNumbers;

        public ResidentRuntimeState RuntimeState => runtimeState;

        public ResidentId ResidentId =>
            runtimeState?.ResidentId ?? executor?.ResidentId ?? ResidentIds.Yaya;

        public ResidentRegistry ResidentRegistry => residentRegistry;

        public int ActiveCycleNumber => activeCycleNumber;

        public string CurrentGoalText { get; private set; } = "Idle";

        public string CurrentDecisionReason { get; private set; } = string.Empty;

        public string NpcExpression => expressionDirector?.Current?.Text ?? fallbackExpressionText;

        public string CurrentEmoji =>
            (expressionDirector?.Current ?? expressionDirector?.Latest)?.Emoji ?? "…";

        public NpcMood CurrentMood =>
            (expressionDirector?.Current ?? expressionDirector?.Latest)?.Mood ?? NpcMood.Focused;

        public IReadOnlyList<NpcExpression> RecentExpressions =>
            expressionDirector?.RecentExpressions ?? Array.Empty<NpcExpression>();

        public WorldEventLog WorldEvents => eventLog;

        public NpcPersonaDefinition Persona =>
            runtimeState?.Persona ?? NpcPersonaDefinition.Yaya;

        public MemoryStore Memories => runtimeState?.Memories;

        public IReadOnlyList<MemoryEntry> RecentMemories => runtimeState == null
            ? Array.Empty<MemoryEntry>()
            : runtimeState.Memories.GetRecentObservations(6);

        public IReadOnlyList<NpcReflection> RecentReflections => runtimeState == null
            ? Array.Empty<NpcReflection>()
            : runtimeState.RecentReflections;

        public NpcReflection LatestReflection => runtimeState?.LatestReflection;

        public int CompletedReflectionCount => runtimeState?.CompletedCycleCount ?? 0;

        public string LastFailureReason { get; private set; } = string.Empty;

        public ActionResult? LastGatewaySubmissionResult { get; private set; }

        public AiGatewayMode ConfiguredAiMode =>
            aiGatewayClient?.ConfiguredMode ?? AiGatewayMode.Local;

        public AiGatewayMode CurrentAiMode =>
            aiGatewayClient?.ActiveMode ?? AiGatewayMode.Local;

        public bool IsGatewayRequestPending => gatewayRequestPending || reflectionRequestPending;

        public bool IsReflectionPending => reflectionRequestPending;

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

        public ActionResult ConfigureAiGateway(IAiGatewayClient gatewayClient)
        {
            if (gatewayClient == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "AI gateway client cannot be null.");
            }

            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "AI gateway mode must be configured before initialization.");
            }

            aiGatewayClient = gatewayClient;
            return ActionResult.Success("AI gateway client configured.");
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
                executor == null ? ResidentIds.Yaya : executor.ResidentId,
                bootstrap.Field,
                bootstrap.Inventory,
                bootstrap.Clock,
                bootstrap.Simulation,
                bootstrap.Events);

            try
            {
                IAiGatewayClient configuredClient = aiGatewayClient ??
                    CreateGatewayClient(bootstrap.SceneConfig);
                return Initialize(
                    context,
                    executor,
                    bootstrap.Mode,
                    configuredClient,
                    bootstrap.ResidentRegistry);
            }
            catch (ArgumentException exception)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    exception.Message);
            }
        }

        public ActionResult Initialize(
            NpcActionContext context,
            NpcPlanExecutor planExecutor,
            DemoMode demoMode,
            IAiGatewayClient gatewayClient = null,
            ResidentRegistry registry = null)
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "ReplanController has already been initialized.");
            }

            if (context == null || planExecutor == null || !planExecutor.IsInitialized ||
                planExecutor.ResidentId != context.ResidentId || demoMode == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "ReplanController requires an action context, initialized executor, and DemoMode.");
            }

            executor = planExecutor;
            actionContext = context;
            this.demoMode = demoMode;
            eventLog = context.EventLog ?? new WorldEventLog();
            aiGatewayClient = gatewayClient ?? aiGatewayClient ?? new LocalAiGatewayClient();
            residentRegistry = registry ?? new ResidentRegistry();
            ActionResult resolved = residentRegistry.TryGetRuntimeState(
                context.ResidentId,
                out runtimeState);
            if (resolved.Failed)
            {
                if (registry != null)
                {
                    return resolved;
                }

                ResidentDefinition definition = context.ResidentId == ResidentIds.Yaya
                    ? ResidentDefinition.Yaya
                    : new ResidentDefinition(
                        context.ResidentId,
                        context.ResidentId.Value,
                        NpcPersonaDefinition.Yaya);
                runtimeState = new ResidentRuntimeState(definition);
                ActionResult registered = residentRegistry.Register(definition, runtimeState);
                if (registered.Failed)
                {
                    return registered;
                }
            }

            observationService = new ObservationService(runtimeState.ResidentId);
            reflectionService = new ReflectionService(runtimeState);
            observationService.CaptureNewObservations(
                eventLog,
                runtimeState.Memories,
                out _);
            eventLog.EntryRecorded += HandleWorldEventRecorded;
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
            return ActionResult.Success($"{ConfiguredAiMode} AI replanning initialized.");
        }

        public ActionResult RestoreFromSave(
            FarmGoalSpec savedGoal,
            ReplanStatus savedStatus,
            IEnumerable<int> savedHarvestedPlotNumbers,
            ResidentRuntimeState savedRuntimeState,
            int savedActiveCycleNumber,
            string savedGoalText,
            string savedDecisionReason,
            string savedFailureReason,
            NpcMood savedMood,
            string savedEmoji,
            string savedExpression)
        {
            if (!IsInitialized || savedRuntimeState == null ||
                savedRuntimeState.ResidentId != ResidentId ||
                savedHarvestedPlotNumbers == null ||
                !Enum.IsDefined(typeof(ReplanStatus), savedStatus))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid initialized replanner and saved NPC state are required.");
            }

            if ((savedStatus == ReplanStatus.Running && savedGoal == null) ||
                savedActiveCycleNumber < 0 ||
                savedActiveCycleNumber > savedRuntimeState.StartedCycleCount ||
                (savedGoal != null && savedActiveCycleNumber == 0))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "Saved farm goal and NPC cycle state are inconsistent.");
            }

            var validatedHarvests = new HashSet<int>();
            foreach (int plotNumber in savedHarvestedPlotNumbers)
            {
                if (plotNumber < 1 || plotNumber > AIFarm.Farming.FarmField.PlotCount ||
                    !validatedHarvests.Add(plotNumber))
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Saved harvested plot numbers are invalid.");
                }
            }

            StopAllCoroutines();
            gatewayRequestPending = false;
            reflectionRequestPending = false;
            pendingExpressionTriggers.Clear();
            ActionResult goalRestore = savedGoal == null
                ? savedRuntimeState.ClearCurrentGoal()
                : savedRuntimeState.SetCurrentGoal(savedGoal);
            if (goalRestore.Failed)
            {
                return goalRestore;
            }

            ActionResult runtimeReplace = residentRegistry.ReplaceRuntimeState(
                ResidentId,
                savedRuntimeState);
            if (runtimeReplace.Failed)
            {
                return runtimeReplace;
            }

            runtimeState = savedRuntimeState;
            observationService = new ObservationService(runtimeState.ResidentId);
            reflectionService = new ReflectionService(runtimeState);
            expressionDirector = new NpcExpressionDirector(
                new LocalTemplateExpressionService(),
                demoMode.ExpressionCooldownSeconds,
                demoMode.ExpressionDisplaySeconds);
            activeCycleNumber = savedActiveCycleNumber;
            harvestedPlotNumbers.Clear();
            foreach (int plotNumber in validatedHarvests)
            {
                harvestedPlotNumbers.Add(plotNumber);
            }

            Status = savedStatus;
            CurrentGoalText = string.IsNullOrWhiteSpace(savedGoalText)
                ? (savedGoal?.Summary ?? "Idle")
                : savedGoalText;
            CurrentDecisionReason = savedStatus == ReplanStatus.Running
                ? "存档已加载；正在根据权威世界状态安全重新规划。"
                : (savedDecisionReason ?? string.Empty);
            LastFailureReason = savedFailureReason ?? string.Empty;
            fallbackExpressionText = string.IsNullOrWhiteSpace(savedExpression)
                ? "状态恢复好了，我们继续吧。"
                : savedExpression.Trim();
            LastGatewaySubmissionResult = null;

            if (Enum.IsDefined(typeof(NpcMood), savedMood))
            {
                expressionDirector.Trigger(
                    new NpcExpression(
                        NpcExpressionTrigger.CommandAccepted,
                        savedMood,
                        savedEmoji,
                        fallbackExpressionText),
                    UnityEngine.Time.unscaledTime,
                    true);
            }

            eventLog.Clear();
            return ActionResult.Success("NPC replanning state restored.");
        }

        public ActionResult ResetForNewDemo()
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "ReplanController must be initialized before starting a new demo.");
            }

            StopAllCoroutines();
            gatewayRequestPending = false;
            reflectionRequestPending = false;
            pendingExpressionTriggers.Clear();
            harvestedPlotNumbers.Clear();
            activeCycleNumber = 0;
            var resetRuntimeState = new ResidentRuntimeState(runtimeState.Definition);
            ActionResult runtimeReplace = residentRegistry.ReplaceRuntimeState(
                ResidentId,
                resetRuntimeState);
            if (runtimeReplace.Failed)
            {
                return runtimeReplace;
            }

            runtimeState = resetRuntimeState;
            observationService = new ObservationService(runtimeState.ResidentId);
            reflectionService = new ReflectionService(runtimeState);
            expressionDirector = new NpcExpressionDirector(
                new LocalTemplateExpressionService(),
                demoMode.ExpressionCooldownSeconds,
                demoMode.ExpressionDisplaySeconds);
            Status = ReplanStatus.Idle;
            CurrentGoalText = "Idle";
            CurrentDecisionReason = string.Empty;
            LastFailureReason = string.Empty;
            fallbackExpressionText = "新的 Demo 准备好了，交给我吧！🌱";
            LastGatewaySubmissionResult = null;
            return ActionResult.Success("NPC state reset for a new demo.");
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

            if (gatewayRequestPending || reflectionRequestPending || IsGoalActive || executor.IsBusy)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "当前种田目标或 AI 请求尚未完成，请等待后再提交。");
            }

            LastGatewaySubmissionResult = null;
            if (ConfiguredAiMode == AiGatewayMode.Remote)
            {
                gatewayRequestPending = true;
                CurrentGoalText = "Contacting Remote AI…";
                CurrentDecisionReason = "正在请求远程 AI 网关解释命令。";
                StartCoroutine(RequestGoalInterpretation(command));
                return ActionResult.Success("Remote AI interpretation requested.");
            }

            AiGatewayResult<FarmGoalSpec> gatewayResult = null;
            IEnumerator request = aiGatewayClient.InterpretCommand(
                ResidentId,
                command,
                result => gatewayResult = result);
            while (request.MoveNext())
            {
                if (request.Current != null)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        "Local AI gateway unexpectedly required asynchronous execution.");
                }
            }

            return CompleteGoalInterpretation(gatewayResult);
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
                return ActionResult.Success("No farm goal requires replanning.");
            }

            if (executor.Status == NpcExecutionStatus.Failed)
            {
                return FailGoal(executor.LastFailureReason);
            }

            if (executor.IsBusy)
            {
                return ActionResult.Success("Waiting for the current atomic action to complete.");
            }

            ActionResult planned = planner.DecideNext(ActiveGoal, harvestedPlotNumbers, out FarmPlanDecision decision);
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
                CurrentGoalText = $"Completed: {ActiveGoal.Summary}";
                fallbackExpressionText = "九块地都完成了，胡萝卜已经收好。";
                RecordEvent(
                    WorldEventKind.GoalCompleted,
                    "九块目标土地已经全部收获，任务完成。");
                string reflectionContext = reflectionService.BuildReflectionContext(
                    ActiveGoal.Summary);
                TriggerReflection(
                    NpcReflectionOutcome.Completed,
                    reflectionContext,
                    NpcExpressionTrigger.GoalCompleted,
                    activeCycleNumber,
                    interrupt: true);
                return ActionResult.Success("端到端种田目标已完成。");
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
                fallbackExpressionText = $"无法启动 AI 规划：{result.Message}";
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

            if (eventLog != null)
            {
                eventLog.EntryRecorded -= HandleWorldEventRecorded;
            }
        }

        private IEnumerator RequestGoalInterpretation(string command)
        {
            AiGatewayResult<FarmGoalSpec> gatewayResult = null;
            yield return aiGatewayClient.InterpretCommand(
                ResidentId,
                command,
                result => gatewayResult = result);
            gatewayRequestPending = false;
            CompleteGoalInterpretation(gatewayResult);
        }

        private ActionResult CompleteGoalInterpretation(
            AiGatewayResult<FarmGoalSpec> gatewayResult)
        {
            if (gatewayResult == null)
            {
                ActionResult missing = ActionResult.Failure(
                    ActionFailureReason.ServiceUnavailable,
                    "AI gateway did not return an interpretation result.");
                LastGatewaySubmissionResult = missing;
                CurrentGoalText = "Rejected";
                CurrentDecisionReason = missing.Message;
                LastFailureReason = missing.Message;
                Status = ReplanStatus.Idle;
                return missing;
            }

            if (gatewayResult.ResidentId != ResidentId)
            {
                ActionResult mismatch = ActionResult.Failure(
                    ActionFailureReason.InvalidResponse,
                    "AI gateway returned an interpretation for another resident.");
                LastGatewaySubmissionResult = mismatch;
                CurrentGoalText = "Rejected";
                CurrentDecisionReason = mismatch.Message;
                LastFailureReason = mismatch.Message;
                Status = ReplanStatus.Idle;
                return mismatch;
            }

            LastGatewaySubmissionResult = gatewayResult.Outcome;
            if (gatewayResult.Failed)
            {
                CurrentGoalText = "Rejected";
                CurrentDecisionReason = gatewayResult.Outcome.Message;
                LastFailureReason = gatewayResult.Outcome.Message;
                Status = ReplanStatus.Idle;
                return gatewayResult.Outcome;
            }

            FarmGoalSpec goal = gatewayResult.Value;
            harvestedPlotNumbers.Clear();
            ActionResult goalAssigned = runtimeState.SetCurrentGoal(goal);
            if (goalAssigned.Failed)
            {
                CurrentGoalText = "Rejected";
                CurrentDecisionReason = goalAssigned.Message;
                LastFailureReason = goalAssigned.Message;
                Status = ReplanStatus.Idle;
                return goalAssigned;
            }

            activeCycleNumber = runtimeState.BeginCycle();
            Status = ReplanStatus.Running;
            CurrentGoalText = goal.Summary;
            CurrentDecisionReason = DescribeInterpretationSource(gatewayResult.Source);
            fallbackExpressionText = "收到，我会照顾九块胡萝卜直到全部收获。";
            LastFailureReason = string.Empty;
            RecordEvent(
                WorldEventKind.CommandAccepted,
                $"[{gatewayResult.Source}] 接受用户命令：{goal.Summary}。");
            TriggerExpression(NpcExpressionTrigger.CommandAccepted, goal.Summary);
            return TickReplan();
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
            if (IsGoalActive)
            {
                FailGoal(result.Message, result.FailureReason);
                return;
            }

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
            LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "规划失败。" : reason;
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

            double requestTime = UnityEngine.Time.unscaledTime;
            if (CurrentAiMode == AiGatewayMode.Local)
            {
                return expressionDirector.Trigger(trigger, context, requestTime, interrupt);
            }

            string generationContext = reflectionService.BuildExpressionContext(context);

            ActionResult available = expressionDirector.CanTrigger(trigger, requestTime);
            if (available.Failed)
            {
                return available;
            }

            if (!pendingExpressionTriggers.Add(trigger))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Expression trigger {trigger} already has a pending AI request.");
            }

            StartCoroutine(RequestExpression(trigger, context, generationContext, interrupt));
            return ActionResult.Success("Remote NPC utterance requested.");
        }

        private ActionResult TriggerReflection(
            NpcReflectionOutcome outcome,
            string eventSummary,
            NpcExpressionTrigger trigger,
            int cycleNumber,
            bool interrupt)
        {
            if (CurrentAiMode == AiGatewayMode.Local)
            {
                return CreateAndApplyLocalReflection(
                    outcome,
                    eventSummary,
                    trigger,
                    cycleNumber,
                    interrupt);
            }

            if (!pendingExpressionTriggers.Add(trigger))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Expression trigger {trigger} already has a pending AI request.");
            }

            reflectionRequestPending = true;
            StartCoroutine(RequestReflection(
                ActiveGoal,
                outcome,
                eventSummary,
                trigger,
                cycleNumber,
                interrupt));
            return ActionResult.Success("Remote NPC reflection requested.");
        }

        private IEnumerator RequestExpression(
            NpcExpressionTrigger trigger,
            string localContext,
            string generationContext,
            bool interrupt)
        {
            AiGatewayResult<NpcExpression> result = null;
            yield return aiGatewayClient.GenerateUtterance(
                ResidentId,
                trigger,
                generationContext,
                gatewayResult => result = gatewayResult);
            pendingExpressionTriggers.Remove(trigger);

            if (result != null && result.ResidentId == ResidentId &&
                result.Succeeded && result.Source == AiGatewayMode.Remote)
            {
                expressionDirector.Trigger(result.Value, UnityEngine.Time.unscaledTime, interrupt);
                yield break;
            }

            expressionDirector.Trigger(
                trigger,
                localContext,
                UnityEngine.Time.unscaledTime,
                interrupt);
        }

        private IEnumerator RequestReflection(
            FarmGoalSpec goal,
            NpcReflectionOutcome outcome,
            string eventSummary,
            NpcExpressionTrigger trigger,
            int cycleNumber,
            bool interrupt)
        {
            AiGatewayResult<NpcReflection> result = null;
            yield return aiGatewayClient.Reflect(
                ResidentId,
                goal,
                outcome,
                eventSummary,
                gatewayResult => result = gatewayResult);
            pendingExpressionTriggers.Remove(trigger);
            reflectionRequestPending = false;

            if (result != null && result.ResidentId == ResidentId &&
                result.Succeeded && result.Source == AiGatewayMode.Remote)
            {
                ApplyCompletedReflection(result.Value, trigger, cycleNumber, interrupt);
                yield break;
            }

            CreateAndApplyLocalReflection(
                outcome,
                eventSummary,
                trigger,
                cycleNumber,
                interrupt);
        }

        private ActionResult CreateAndApplyLocalReflection(
            NpcReflectionOutcome outcome,
            string eventSummary,
            NpcExpressionTrigger trigger,
            int cycleNumber,
            bool interrupt)
        {
            ActionResult created = reflectionService.CreateLocalReflection(
                ActiveGoal,
                outcome,
                eventSummary,
                out NpcReflection reflection);
            if (created.Failed)
            {
                return created;
            }

            return ApplyCompletedReflection(reflection, trigger, cycleNumber, interrupt);
        }

        private ActionResult ApplyCompletedReflection(
            NpcReflection reflection,
            NpcExpressionTrigger trigger,
            int cycleNumber,
            bool interrupt)
        {
            ActionResult recorded = runtimeState.RecordCompletedCycleReflection(
                cycleNumber,
                reflection,
                actionContext.Clock.ElapsedGameSeconds);
            if (recorded.Failed)
            {
                return recorded;
            }

            fallbackExpressionText = reflection.Text;
            var expression = new NpcExpression(
                trigger,
                reflection.Mood,
                reflection.Emoji,
                reflection.Text);
            return expressionDirector.Trigger(
                expression,
                UnityEngine.Time.unscaledTime,
                interrupt);
        }

        private static IAiGatewayClient CreateGatewayClient(DemoSceneConfig config)
        {
            if (config == null || config.AiGatewayMode == AiGatewayMode.Local)
            {
                return new LocalAiGatewayClient();
            }

            return new RemoteAiGatewayClient(
                config.AiGatewayBaseUrl,
                config.AiRequestTimeoutSeconds);
        }

        private string DescribeInterpretationSource(AiGatewayMode source)
        {
            if (source == AiGatewayMode.Remote)
            {
                return "目标已由远程 AI 网关解释并通过本地 JSON 校验。";
            }

            return ConfiguredAiMode == AiGatewayMode.Remote
                ? "远程服务不可用或响应无效，已自动回退本地解释。"
                : "目标已在本地解释，准备查询世界状态。";
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

        private void HandleWorldEventRecorded(WorldEventEntry worldEvent)
        {
            observationService?.Observe(worldEvent, runtimeState?.Memories, out _);
        }
    }
}
