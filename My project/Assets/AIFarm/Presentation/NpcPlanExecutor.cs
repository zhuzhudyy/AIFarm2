using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class NpcPlanExecutor : MonoBehaviour
    {
        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private NpcNavigator navigator;

        [SerializeField]
        private BlockoutActionFeedback actionFeedback;

        [SerializeField]
        private NpcExecutionStatus status = NpcExecutionStatus.Completed;

        [SerializeField]
        private string residentIdValue = ResidentIds.YayaValue;

        [TextArea]
        [SerializeField]
        private string lastFailureReason = string.Empty;

        private readonly ActionQueue actionQueue = new ActionQueue();
        private NpcActionContext actionContext;
        private INpcNavigationDriver navigationDriver;
        private INpcActionFeedback feedbackDriver;
        private INpcAction currentAction;
        private ActionResult? lastResult;

        public event Action<NpcExecutionStatus> StatusChanged;

        public event Action<INpcAction> ActionStarted;

        public event Action<INpcAction, ActionResult> ActionCompleted;

        public event Action<INpcAction, ActionResult> ActionFailed;

        public ActionQueue Queue => actionQueue;

        public NpcExecutionStatus Status => status;

        public INpcAction CurrentAction => currentAction;

        public ResidentId ResidentId { get; private set; } = ResidentIds.Yaya;

        public ResidentId? CurrentActionResidentId =>
            currentAction == null ? (ResidentId?)null : ResidentId;

        public ActionResult? LastResult => lastResult;

        public string LastFailureReason => lastFailureReason;

        public bool IsInitialized { get; private set; }

        public bool IsBusy => currentAction != null || !actionQueue.IsEmpty;

        public IReadOnlyList<INpcAction> PendingActions => actionQueue.GetSnapshot();

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            NpcNavigator npcNavigator,
            BlockoutActionFeedback feedback)
        {
            return Configure(ResidentIds.Yaya, gameBootstrap, npcNavigator, feedback);
        }

        public ActionResult Configure(
            ResidentId residentId,
            GameBootstrap gameBootstrap,
            NpcNavigator npcNavigator,
            BlockoutActionFeedback feedback)
        {
            if (!residentId.IsValid || gameBootstrap == null || npcNavigator == null || feedback == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NpcPlanExecutor requires bootstrap, navigation, and feedback components.");
            }

            ResidentId = residentId;
            residentIdValue = residentId.Value;
            bootstrap = gameBootstrap;
            navigator = npcNavigator;
            actionFeedback = feedback;
            return ActionResult.Success("NpcPlanExecutor scene references configured.");
        }

        public ActionResult Initialize()
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor requires an initialized GameBootstrap.");
            }

            if (!ResidentId.TryCreate(residentIdValue, out ResidentId configuredResidentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NpcPlanExecutor requires a valid configured ResidentId.");
            }

            ActionResult registered = bootstrap.ResidentRegistry.TryGetRuntimeState(
                configuredResidentId,
                out _);
            if (registered.Failed)
            {
                return registered;
            }

            return Initialize(
                new NpcActionContext(
                    configuredResidentId,
                    bootstrap.Field,
                    bootstrap.Inventory,
                    bootstrap.Clock,
                    bootstrap.Simulation,
                    bootstrap.Events),
                navigator,
                actionFeedback);
        }

        public ActionResult Initialize(
            NpcActionContext context,
            INpcNavigationDriver npcNavigation,
            INpcActionFeedback feedback)
        {
            return Initialize(
                context == null ? default : context.ResidentId,
                context,
                npcNavigation,
                feedback);
        }

        public ActionResult Initialize(
            ResidentId residentId,
            NpcActionContext context,
            INpcNavigationDriver npcNavigation,
            INpcActionFeedback feedback)
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor has already been initialized.");
            }

            if (!residentId.IsValid || context == null ||
                context.ResidentId != residentId ||
                npcNavigation == null || feedback == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NpcPlanExecutor requires action context, navigation, and feedback services.");
            }

            ResidentId = residentId;
            residentIdValue = residentId.Value;
            actionContext = context;
            navigationDriver = npcNavigation;
            feedbackDriver = feedback;
            status = NpcExecutionStatus.Completed;
            lastFailureReason = string.Empty;
            lastResult = null;
            IsInitialized = true;
            return ActionResult.Success("NpcPlanExecutor initialized.");
        }

        public ActionResult Enqueue(INpcAction action)
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor must be initialized before actions are queued.");
            }

            if (status == NpcExecutionStatus.Failed)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor is halted after a failure; reset it before queuing more actions.");
            }

            ActionResult queued = actionQueue.Enqueue(action);
            if (queued.Failed)
            {
                return queued;
            }

            if (currentAction == null)
            {
                SetStatus(NpcExecutionStatus.Pending);
            }

            return queued;
        }

        public ActionResult Tick(float deltaTime)
        {
            if (!IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor is not initialized.");
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Executor delta time must be finite and non-negative.");
            }

            if (status == NpcExecutionStatus.Failed)
            {
                return lastResult ?? ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor is halted after a failure.");
            }

            if (currentAction == null)
            {
                if (actionQueue.IsEmpty)
                {
                    SetStatus(NpcExecutionStatus.Completed);
                    return ActionResult.Success("NPC action queue is complete.");
                }

                ActionResult dequeued = actionQueue.TryDequeue(out currentAction);
                if (dequeued.Failed)
                {
                    return FailCurrent(dequeued);
                }

                SetStatus(NpcExecutionStatus.Pending);
                return ActionResult.Success($"{currentAction.DisplayName} is pending.");
            }

            switch (status)
            {
                case NpcExecutionStatus.Pending:
                    return BeginCurrentAction();
                case NpcExecutionStatus.Moving:
                    return TickMovement(deltaTime);
                case NpcExecutionStatus.Acting:
                    return TickFeedback(deltaTime);
                case NpcExecutionStatus.Completed:
                    return ActionResult.Success("The current NPC action is complete.");
                default:
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        $"Unsupported executor status: {status}.");
            }
        }

        public ActionResult ResetAfterFailure(bool clearPendingActions = true)
        {
            if (status != NpcExecutionStatus.Failed)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor can only be reset after a failure.");
            }

            navigationDriver.CancelMove();
            feedbackDriver.Cancel();
            if (clearPendingActions)
            {
                actionQueue.Clear();
            }

            currentAction = null;
            lastResult = null;
            lastFailureReason = string.Empty;
            SetStatus(actionQueue.IsEmpty ? NpcExecutionStatus.Completed : NpcExecutionStatus.Pending);
            return ActionResult.Success("NpcPlanExecutor failure reset.");
        }

        public ActionResult RestorePendingActions(IEnumerable<INpcAction> actions)
        {
            if (!IsInitialized || actions == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "An initialized executor and saved action list are required.");
            }

            var validated = new List<INpcAction>();
            foreach (INpcAction action in actions)
            {
                if (action == null)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidResponse,
                        "Saved NPC action list contains a null action.");
                }

                validated.Add(action);
            }

            navigationDriver.CancelMove();
            feedbackDriver.Cancel();
            actionQueue.Clear();
            currentAction = null;
            lastResult = null;
            lastFailureReason = string.Empty;
            foreach (INpcAction action in validated)
            {
                actionQueue.Enqueue(action);
            }

            SetStatus(actionQueue.IsEmpty
                ? NpcExecutionStatus.Completed
                : NpcExecutionStatus.Pending);
            return ActionResult.Success(
                actionQueue.IsEmpty
                    ? "NPC executor restored without pending actions."
                    : "Saved NPC actions restored and safely restarted from Pending.");
        }

        private void Start()
        {
            if (IsInitialized)
            {
                return;
            }

            ActionResult initialized = Initialize();
            if (initialized.Failed)
            {
                lastResult = initialized;
                lastFailureReason = initialized.Message;
                SetStatus(NpcExecutionStatus.Failed);
                Debug.LogWarning($"NpcPlanExecutor initialization failed: {initialized.Message}", this);
            }
        }

        private void Update()
        {
            if (IsInitialized && (currentAction != null || !actionQueue.IsEmpty))
            {
                Tick(UnityEngine.Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            navigationDriver.CancelMove();
            feedbackDriver.Cancel();
        }

        private ActionResult BeginCurrentAction()
        {
            ActionResult preconditions = currentAction.CheckPreconditions(actionContext);
            if (preconditions.Failed)
            {
                return FailCurrent(preconditions);
            }

            actionContext.EventLog?.Record(
                actionContext.Clock.ElapsedGameSeconds,
                WorldEventKind.ActionStarted,
                $"开始动作：{currentAction.DisplayName}。",
                currentAction.TargetPlotNumber,
                WorldEventVisibility.Private,
                actionContext.ResidentId,
                new[] { actionContext.ResidentId },
                tags: BuildActionTags(currentAction));
            ActionStarted?.Invoke(currentAction);

            if (!currentAction.TargetPlotNumber.HasValue)
            {
                return BeginActing();
            }

            ActionResult movement = navigationDriver.BeginMove(currentAction.TargetPlotNumber.Value);
            if (movement.Failed)
            {
                return FailCurrent(movement);
            }

            SetStatus(NpcExecutionStatus.Moving);
            return movement;
        }

        private ActionResult TickMovement(float deltaTime)
        {
            ActionResult movement = navigationDriver.Tick(deltaTime, out bool arrived);
            if (movement.Failed)
            {
                return FailCurrent(movement);
            }

            return arrived ? BeginActing() : movement;
        }

        private ActionResult BeginActing()
        {
            ActionResult recheck = currentAction.CheckPreconditions(actionContext);
            if (recheck.Failed)
            {
                return FailCurrent(recheck);
            }

            ActionResult feedback = feedbackDriver.Begin(
                currentAction.DisplayName,
                currentAction.DurationSeconds);
            if (feedback.Failed)
            {
                return FailCurrent(feedback);
            }

            SetStatus(NpcExecutionStatus.Acting);
            return feedback;
        }

        private ActionResult TickFeedback(float deltaTime)
        {
            ActionResult feedback = feedbackDriver.Tick(deltaTime, out bool completed);
            if (feedback.Failed)
            {
                return FailCurrent(feedback);
            }

            if (!completed)
            {
                return feedback;
            }

            ActionResult completion = currentAction.Complete(actionContext);
            if (completion.Failed)
            {
                return FailCurrent(completion);
            }

            INpcAction completedAction = currentAction;
            actionContext.EventLog?.Record(
                actionContext.Clock.ElapsedGameSeconds,
                WorldEventKind.ActionCompleted,
                $"完成动作：{completedAction.DisplayName}。",
                completedAction.TargetPlotNumber,
                WorldEventVisibility.Private,
                actionContext.ResidentId,
                new[] { actionContext.ResidentId },
                tags: BuildActionTags(completedAction));
            lastResult = completion;
            lastFailureReason = string.Empty;
            currentAction = null;
            SetStatus(NpcExecutionStatus.Completed);
            ActionCompleted?.Invoke(completedAction, completion);
            return completion;
        }

        private ActionResult FailCurrent(ActionResult failure)
        {
            if (failure.Succeeded)
            {
                failure = ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NpcPlanExecutor received an invalid successful failure result.");
            }

            navigationDriver?.CancelMove();
            feedbackDriver?.Cancel();
            lastResult = failure;
            lastFailureReason = failure.Message;
            SetStatus(NpcExecutionStatus.Failed);
            string actionName = currentAction == null ? "Unknown action" : currentAction.DisplayName;
            actionContext?.EventLog?.Record(
                actionContext.Clock.ElapsedGameSeconds,
                WorldEventKind.ActionFailed,
                $"动作失败：{actionName}；{failure.Message}",
                currentAction?.TargetPlotNumber,
                WorldEventVisibility.Private,
                actionContext.ResidentId,
                new[] { actionContext.ResidentId },
                tags: BuildActionTags(currentAction));
            ActionFailed?.Invoke(currentAction, failure);
            Debug.LogWarning($"NPC action '{actionName}' failed: {failure.Message}", this);
            return failure;
        }

        private static string[] BuildActionTags(INpcAction action)
        {
            var tags = new List<string> { "farm-action" };
            if (action == null)
            {
                return tags.ToArray();
            }

            string actionTag = action.GetType().Name
                .Replace("Action", string.Empty)
                .ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(actionTag))
            {
                tags.Add(actionTag);
            }

            if (action is HarvestAction)
            {
                tags.Add("harvest");
                tags.Add("carrot");
            }

            return tags.ToArray();
        }

        private void SetStatus(NpcExecutionStatus nextStatus)
        {
            if (status == nextStatus)
            {
                return;
            }

            status = nextStatus;
            StatusChanged?.Invoke(status);
        }
    }
}
