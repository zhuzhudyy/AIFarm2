using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Town;
using UnityEngine;
using UnityEngine.AI;

namespace AIFarm.Presentation
{
    [Serializable]
    public sealed class ResidentTaskSnapshot
    {
        public string residentId;
        public bool hasTask;
        public ResidentTaskSpec task;
        public int[] completedPlots = Array.Empty<int>();
        public int completedCycles;
        public int completedActivityCount;
        public float energy = 90f;
        public float hunger = 15f;
        public float social = 30f;

        public ActionResult NormalizeTaskPresence(ResidentId owner)
        {
            if (task == null)
                return hasTask ? ActionResult.Failure(ActionFailureReason.InvalidResponse,
                    "存档声明有任务，但任务内容缺失。") : ActionResult.Success();
            // JsonUtility serializes a null inline class as an empty/default object.
            // Only that exact empty shape is absent; malformed nonempty tasks must
            // still fail validation, including a hasTask=true empty task.
            bool emptyInlineObject = string.IsNullOrEmpty(task.resident_id) && string.IsNullOrEmpty(task.task_id) &&
                string.IsNullOrEmpty(task.task_type) && string.IsNullOrEmpty(task.summary) &&
                string.IsNullOrEmpty(task.target_id) && string.IsNullOrEmpty(task.target_resident_id) &&
                (task.target_plot_numbers == null || task.target_plot_numbers.Length == 0) && !task.repeat &&
                (task.quantity == 0 || task.quantity == 1) && (string.IsNullOrEmpty(task.crop) || task.crop == "carrot") &&
                (string.IsNullOrEmpty(task.provider) || task.provider == "local") && task.config_version == 0 &&
                string.IsNullOrEmpty(task.request_id) && string.IsNullOrEmpty(task.model) &&
                string.IsNullOrEmpty(task.error_code) && string.IsNullOrEmpty(task.error_message);
            if (!hasTask && emptyInlineObject)
            {
                task = null;
                return ActionResult.Success("已还原没有玩家任务的居民状态。");
            }
            ActionResult valid = task.Validate(owner);
            if (valid.Failed) return valid;
            // Version 5 saves written before hasTask was added retain valid tasks.
            hasTask = true;
            return ActionResult.Success();
        }

        public ActionResult ValidateActivityProgress()
        {
            if (completedActivityCount < 0)
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "存档活动完成次数不能为负数。");
            bool hasCountedActivity = task != null &&
                (task.task_type == "Move" || task.task_type == "Fish" || task.task_type == "PickFruit");
            if (!hasCountedActivity)
                return completedActivityCount == 0 ? ActionResult.Success() :
                    ActionResult.Failure(ActionFailureReason.InvalidResponse, "没有数量活动的任务不能含已完成采集次数。");
            if (!task.repeat && completedActivityCount >= task.quantity)
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "存档中的一次性活动已达到目标数量，却仍被标记为进行中。");
            return ActionResult.Success();
        }
    }

    /// <summary>Owns one resident's perception, plans, actions and recoverable player task.</summary>
    [DisallowMultipleComponent]
    public sealed class TownLifeController : MonoBehaviour
    {
        private GameBootstrap bootstrap;
        private TownResidentScheduleController schedule;
        private TownScheduleCoordinator town;
        private TownSocialCoordinator socialCoordinator;
        private NpcPlanExecutor executor;
        private ReplanController legacyReplanner;
        private TownResidentNavigator navigator;
        private BlockoutActionFeedback feedback;
        private ResidentBlockoutView view;
        private NavMeshAgent agent;
        private LocationArrivalPoint[] points;
        private readonly HashSet<int> completedPlots = new HashSet<int>();
        private InteractionPointReservation reservation;
        private ResidentTaskSpec task;
        private string taskConversationId = "";
        private string conversationOwnerTaskId = "";
        private bool initialized;
        private bool decisionPending;
        private bool commandPending;
        private long generation;
        private int decisionIndex;
        private int completedCycles;
        private int activityCount;
        private int activityFailures;
        private int farmActionFailures;
        private string activeActivity = "";
        private string activeTarget = "";
        private string queuedDecision;
        private bool queuedDecisionIsRemote;
        private string queuedDecisionSource = "本地规则";
        private string recentDecisionSource = "本地规则";
        private bool moving;
        private double movementStarted;
        private double activityStarted;
        private double previousActivityGameSeconds;
        private double activityDuration;
        private double nextDecisionAt;
        private double previousGameSeconds;
        private double previousRealSeconds;
        private double lastDecisionGameSeconds;
        private double farmRetryAt;
        private float energy = 90f;
        private float hunger = 15f;
        private float socialNeed = 30f;
        private string source = "本地规则";
        private string lastError = "";
        private string reason = "准备今日生活";

        public ResidentId ResidentId => schedule == null ? default : schedule.ResidentId;
        public bool IsInitialized => initialized;
        public bool HasPlayerTask => task != null || commandPending;
        public bool IsBusy => executor != null && executor.IsBusy || activeActivity.Length > 0;
        public bool IsAvailableForConversation => initialized && !IsBusy && !commandPending &&
            (task == null || task.task_type == "Chat" || TaskState == "等待作物") && !IsNight;
        public string TaskState { get; private set; } = "自主生活";
        public string CurrentAction => executor != null && executor.IsBusy
            ? executor.CurrentAction?.DisplayName ?? "准备农事" : activeActivity.Length > 0 ? activeActivity : TaskState;
        public int CompletedCycles => completedCycles;
        public string LastError => lastError;
        public string ExecutionSource => source;
        public double LastDecisionGameSeconds => lastDecisionGameSeconds;
        public double NextDecisionRealSeconds => nextDecisionAt;
        public string LastExecutedIntent { get; private set; } = "";
        public int RemoteDecisionExecutionCount { get; private set; }
        public float ActivityProgress => activeActivity.Length == 0 || activityDuration <= 0 || moving ? 0f :
            Mathf.Clamp01((float)((bootstrap.Clock.ElapsedGameSeconds - activityStarted) / activityDuration));
        public string Diagnostics => $"行为：{CurrentAction} → {activeTarget}\n任务：{TaskState}；轮次：{completedCycles}\n" +
            $"精力 {energy:0} / 饥饿 {hunger:0} / 社交 {socialNeed:0}\n决策：{reason}\n" +
            $"最近决策：第 {(int)(lastDecisionGameSeconds / 86400) + 1} 日 {(int)(lastDecisionGameSeconds / 3600) % 24:00}:{(int)(lastDecisionGameSeconds / 60) % 60:00}；{recentDecisionSource}\n执行来源：{source}\n" +
            $"下次计划：{Math.Max(0, nextDecisionAt - UnityEngine.Time.realtimeSinceStartupAsDouble):0.0}s；请求：{decisionPending || commandPending}\n" +
            $"会话：{socialCoordinator?.GetConversationId(ResidentId) ?? "无"}\n阻塞：{(lastError.Length == 0 ? "无" : lastError)}";
        private bool IsNight => (bootstrap.Clock.ElapsedGameSeconds % 86400) / 3600 >= 22 + ResidentIndex * .15 ||
            (bootstrap.Clock.ElapsedGameSeconds % 86400) / 3600 < 6 + ResidentIndex * .2;
        private int ResidentIndex => Math.Max(0, Array.IndexOf(ResidentIds.TownResidents.ToArray(), ResidentId));

        public ActionResult Initialize(GameBootstrap game, TownResidentScheduleController resident)
        {
            if (initialized) return ActionResult.Success();
            bootstrap = game;
            schedule = resident;
            town = FindFirstObjectByType<TownScheduleCoordinator>();
            socialCoordinator = FindFirstObjectByType<TownSocialCoordinator>();
            navigator = GetComponent<TownResidentNavigator>();
            view = GetComponent<ResidentBlockoutView>();
            agent = GetComponent<NavMeshAgent>();
            if (bootstrap == null || schedule == null || town == null || navigator == null)
                return ActionResult.Failure(ActionFailureReason.InvalidState, "生活执行器缺少已注册居民、导航或日程。");
            points = FindObjectsByType<LocationArrivalPoint>(FindObjectsSortMode.None)
                .OrderBy(point => point.InteractionPointId, StringComparer.Ordinal).ToArray();
            navigator.Configure(agent, points, agent != null, 4f);
            NpcNavigator farmNavigator = GetComponent<NpcNavigator>();
            if (farmNavigator == null) farmNavigator = gameObject.AddComponent<NpcNavigator>();
            farmNavigator.Configure(agent, FindObjectsByType<PlotInteractionPoint>(FindObjectsSortMode.None), agent != null, 4f);
            feedback = GetComponent<BlockoutActionFeedback>();
            if (feedback == null)
            {
                feedback = gameObject.AddComponent<BlockoutActionFeedback>();
                Transform visual = transform.Find("Visual") ?? transform.Find("Model") ?? transform;
                feedback.Configure(visual, null, null);
            }
            executor = GetComponent<NpcPlanExecutor>();
            if (executor == null) executor = gameObject.AddComponent<NpcPlanExecutor>();
            executor.Configure(ResidentId, bootstrap, farmNavigator, feedback);
            if (!executor.IsInitialized)
            {
                ActionResult created = executor.Initialize();
                if (created.Failed) return created;
            }
            schedule.AttachLifeController(this, executor);
            executor.ActionCompleted += OnFarmActionCompleted;
            executor.ActionFailed += OnFarmActionFailed;
            legacyReplanner = FindObjectsByType<ReplanController>(FindObjectsSortMode.None)
                .FirstOrDefault(replanner => replanner.ResidentId == ResidentId);
            bootstrap.AuthoritativeStateResetting += ResetTransientState;
            previousGameSeconds = bootstrap.Clock.ElapsedGameSeconds;
            previousRealSeconds = UnityEngine.Time.realtimeSinceStartupAsDouble;
            nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + ResidentIndex * 1.1;
            initialized = true;
            StartCoroutine(LifeLoop());
            return ActionResult.Success();
        }

        public ActionResult SubmitCommand(string command)
        {
            if (!initialized) return ActionResult.Failure(ActionFailureReason.InvalidState, "居民未初始化。");
            if (string.IsNullOrWhiteSpace(command)) return ActionResult.Failure(ActionFailureReason.InvalidArgument, "请输入指令。");
            // Stop is immediate and never requires a working network.
            if (LocalResidentTaskParser.TryParse(ResidentId, command, out ResidentTaskSpec local).Succeeded && local.task_type == "Stop")
            { StopTask(); return ActionResult.Success("已停止，恢复自主生活。"); }
            StopTask();
            commandPending = true;
            TaskState = "解释玩家指令";
            StartCoroutine(InterpretCommand(command, generation, bootstrap.GatewayConfigurationVersion));
            return ActionResult.Success($"已向 {NpcPersonaDefinition.ForResident(ResidentId).Name} 提交指令。");
        }

        private IEnumerator InterpretCommand(string command, long expectedGeneration, long configVersion)
        {
            yield return null;
            AiGatewayResult<ResidentTaskSpec> result = null;
            yield return bootstrap.AiRequests.InterpretResidentTask(ResidentId, command,
                points.Select(point => point.InteractionPointId).Concat(points.Select(point => point.LocationId.Value)).Distinct().ToArray(),
                ResidentIds.TownResidents.Where(id => id != ResidentId).Select(id => id.Value).ToArray(), value => result = value);
            if (expectedGeneration != generation || configVersion != bootstrap.GatewayConfigurationVersion) yield break;
            commandPending = false;
            if (result == null || result.Failed || result.ResidentId != ResidentId)
            { FailTask(result?.Outcome.Message ?? "指令请求已取消或没有响应。"); yield break; }
            source = result.Source == AiGatewayMode.Remote ? "真实模型" : string.IsNullOrEmpty(result.DiagnosticError) ? "本地规则" : "本地规则 / 降级";
            lastError = result.DiagnosticError;
            ApplyTask(result.Value);
        }

        public ActionResult ApplyTask(ResidentTaskSpec value)
        {
            ActionResult valid = value == null ? ActionResult.Failure(ActionFailureReason.InvalidArgument, "任务为空。") : value.Validate(ResidentId);
            if (valid.Failed) return valid;
            if (value.task_type == "Move" && ResolvePoint(value.target_id) == null)
                return FailTask("目标地点不存在：" + value.target_id);
            if ((value.task_type == "Fish" || value.task_type == "PickFruit") && !string.IsNullOrEmpty(value.target_id) &&
                (!bootstrap.Activities.Contains(value.target_id) || ResolvePoint(value.target_id) == null ||
                    !value.target_id.StartsWith(value.task_type == "Fish" ? "fishing-" : "fruit-", StringComparison.Ordinal)))
                return FailTask("目标活动位置不存在或类型不匹配：" + value.target_id);
            if (value.task_type == "Chat" && !ResidentId.TryCreate(value.target_resident_id, out _))
                return FailTask("请指定有效的聊天对象。");
            if (value.task_type == "Chat" && bootstrap.ResidentRegistry.TryGetRuntimeState(new ResidentId(value.target_resident_id), out _).Failed)
                return FailTask("聊天对象尚未在小镇注册。");
            if (IsFarmTask(value.task_type))
            {
                int[] requestedPlots = value.target_plot_numbers != null && value.target_plot_numbers.Length > 0
                    ? value.target_plot_numbers : Enumerable.Range(1, 9).ToArray();
                int[] freePlots = requestedPlots.Where(plot => bootstrap.CanWorkPlot(plot, ResidentId)).ToArray();
                if (freePlots.Length == 0)
                    return FailTask("这些地块已有居民负责。请选择其他地块，或先停止原居民的照料任务。");
                if (freePlots.Length != requestedPlots.Length)
                {
                    value.target_plot_numbers = freePlots;
                    value.summary += "；已接管空闲地块 " + string.Join("、", freePlots);
                }
            }
            task = value;
            ClearTaskConversationBinding();
            foreach (int plot in TaskPlots())
                if (IsFarmTask(value.task_type)) bootstrap.ClaimTaskPlot(plot, ResidentId);
            completedPlots.Clear();
            activityCount = 0;
            activityFailures = farmActionFailures = 0;
            TaskState = "执行玩家任务";
            reason = value.summary;
            nextDecisionAt = 0;
            return ActionResult.Success("任务已绑定居民：" + ResidentId);
        }

        public void StopTask()
        {
            generation++;
            decisionPending = commandPending = false;
            queuedDecision = null;
            ClearTaskConversationBinding();
            socialCoordinator?.CancelResidentConversation(ResidentId);
            CancelActivity();
            if (executor != null && executor.IsInitialized) executor.RestorePendingActions(Array.Empty<INpcAction>());
            ReplanController legacy = legacyReplanner;
            if (legacy != null && legacy.IsInitialized) legacy.ResetForNewDemo();
            task = null;
            bootstrap.ReleaseTaskPlots(ResidentId);
            completedPlots.Clear();
            activityCount = 0;
            TaskState = "自主生活";
            lastError = "";
            nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 1;
        }

        public void RefreshGateway(long version, bool online, string error)
        {
            generation++;
            if (commandPending)
            {
                TaskState = "配置已更新，旧指令请求已取消";
                lastError = "请重新提交尚未完成解析的指令。";
            }
            decisionPending = false;
            commandPending = false;
            queuedDecision = null;
            nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + ResidentIndex * .75;
            if (!string.IsNullOrEmpty(error)) lastError = error;
        }

        private IEnumerator LifeLoop()
        {
            // Remote operations originate only from this paced coroutine, never Update.
            while (initialized)
            {
                if (isActiveAndEnabled)
                {
                    try { TickLife(); }
                    catch (Exception exception)
                    {
                        lastError = "动作异常，已释放目标并重新规划：" + exception.Message;
                        CancelActivity();
                        nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 5;
                        Debug.LogWarning($"Resident {ResidentId}: {lastError}", this);
                    }
                }
                yield return new WaitForSecondsRealtime(.2f);
            }
        }

        private void TickLife()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            double realDelta = Math.Max(0, now - previousRealSeconds);
            previousRealSeconds = now;
            double gameNow = bootstrap.Clock.ElapsedGameSeconds;
            double elapsed = Math.Max(0, gameNow - previousGameSeconds);
            previousGameSeconds = gameNow;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = bootstrap.Clock.IsPaused;
                agent.speed = 4f * (float)bootstrap.Clock.TimeScale;
                agent.acceleration = 24f * (float)bootstrap.Clock.TimeScale;
                agent.angularSpeed = 480f * (float)bootstrap.Clock.TimeScale;
            }
            if (reservation.IsValid)
            {
                ActionResult lease = town.ReservationService.TryReserve(ResidentId,
                    reservation.InteractionPointId, now, 120, out InteractionPointReservation renewed);
                if (lease.Succeeded) reservation = renewed;
                else { lastError = lease.Message; CancelActivity(); return; }
            }
            if (bootstrap.Clock.IsPaused)
            {
                movementStarted += realDelta;
                return;
            }
            hunger = Mathf.Clamp(hunger + (float)(elapsed / 1800), 0, 100);
            socialNeed = Mathf.Clamp(socialNeed + (float)(elapsed / 2400), 0, 100);
            energy = Mathf.Clamp(energy - (float)(elapsed / 2400), 0, 100);
            if (TickOwnedTaskConversation()) return;
            if (schedule.IsConversationSuspended || schedule.IsTownEventSuspended)
            {
                socialNeed = Math.Max(0, socialNeed - (float)(elapsed / 600));
                return;
            }
            ReplanController legacy = legacyReplanner;
            if (legacy != null && legacy.IsGoalActive) return;
            if (executor.Status == NpcExecutionStatus.Failed)
            {
                lastError = executor.LastFailureReason;
                executor.ResetAfterFailure();
                ReleaseReservation();
            }
            if (executor.IsBusy) return;
            if (activeActivity.Length > 0) { TickActivity(now, gameNow); return; }
            if (commandPending) return;
            if (task != null && TaskState == "等待路径重试" && now < farmRetryAt) return;
            if (IsNight && task == null)
            {
                BeginActivity("Sleep", ResolveHome(), 3600);
                return;
            }
            if (task != null && now >= farmRetryAt)
            {
                if (TryExecutePlayerTask()) return;
            }
            if (now < nextDecisionAt) return;
            var candidates = BuildCandidates();
            string selected = queuedDecision;
            bool fromModel = queuedDecisionIsRemote && !string.IsNullOrEmpty(selected) && candidates.Contains(selected);
            queuedDecision = null;
            queuedDecisionIsRemote = false;
            if (string.IsNullOrEmpty(selected) || !candidates.Contains(selected))
            { selected = candidates[0]; source = "本地规则"; }
            else source = queuedDecisionSource;
            if (!decisionPending && bootstrap.AiRequests.ConfiguredMode == AiGatewayMode.Remote)
            {
                decisionPending = true;
                StartCoroutine(RequestDecision(candidates, generation, bootstrap.GatewayConfigurationVersion));
            }
            nextDecisionAt = now + 12 + ResidentIndex * 1.3;
            decisionIndex++;
            lastDecisionGameSeconds = gameNow;
            ExecuteIntent(selected);
            if (fromModel && (IsBusy || schedule.IsConversationSuspended))
            {
                RemoteDecisionExecutionCount++;
                bootstrap.Events.Record(gameNow, WorldEventKind.System,
                    "经过网关校验的模型决策已进入执行器：" + selected, null,
                    WorldEventVisibility.Private, ResidentId, new[] { ResidentId }, tags: new[] { "model-action", selected });
            }
        }

        private List<string> BuildCandidates()
        {
            var available = new List<string>();
            if (energy < 35) available.Add("Rest");
            if (hunger > 40 && HasFood()) available.Add("Eat");
            if ((!bootstrap.Inventory.Has(InventoryItem.Water) || (!bootstrap.Inventory.Has(InventoryItem.Fertilizer) &&
                bootstrap.Inventory.Has(InventoryItem.Compost))) && bootstrap.Activities.IsAvailable("well")) available.Add("Refill");
            if (socialNeed > 45 && FindConversationPartner().IsValid) available.Add("RequestConversation");
            string[] preferred = ResidentIndex % 3 == 0 ? new[] { "TendFarm", "Fish", "PickFruit", "Walk", "Rest" } :
                ResidentIndex % 3 == 1 ? new[] { "Fish", "Walk", "PickFruit", "TendFarm", "Rest" } :
                new[] { "PickFruit", "Rest", "TendFarm", "Fish", "Walk" };
            for (int i = 0; i < preferred.Length; i++)
            {
                string option = preferred[(decisionIndex + i) % preferred.Length];
                bool executable = option == "TendFarm" ? FindFarmAction(null, true) != null :
                    option == "Fish" ? AvailableActivityTarget("fishing-") != null :
                    option == "PickFruit" ? AvailableActivityTarget("fruit-") != null : true;
                if (executable && !available.Contains(option)) available.Add(option);
            }
            if (FindConversationPartner().IsValid && !available.Contains("RequestConversation")) available.Add("RequestConversation");
            if (HasFood() && !available.Contains("Eat")) available.Add("Eat");
            return available;
        }

        private IEnumerator RequestDecision(List<string> candidates, long requestGeneration, long version)
        {
            yield return null;
            bootstrap.ResidentRegistry.TryGetRuntimeState(ResidentId, out ResidentRuntimeState runtime);
            ResidentId partner = FindConversationPartner();
            ResidentContext context = null;
            if (partner.IsValid) socialCoordinator?.TryBuildResidentContext(ResidentId, partner, out context);
            if (context == null)
            {
                var memories = new List<ResidentMemorySnapshot>();
                foreach (MemoryEntry memory in runtime.Memories.GetRecentObservations(6))
                    memories.Add(new ResidentMemorySnapshot(ResidentId, memory.Text, memory.Importance,
                        memory.KnowledgeId, memory.RootFactId, memory.Tags, memory.IsShareable,
                        memory.ImmediateSourceResidentId.IsValid ? (ResidentId?)memory.ImmediateSourceResidentId : null));
                context = new ResidentContext(ResidentId, ResidentPersonaSnapshot.FromDefinition(runtime.Definition),
                    $"{CurrentAction};精力{energy:0};饥饿{hunger:0};社交{socialNeed:0};{TaskState}",
                    Array.Empty<RelationshipSnapshot>(), memories);
            }
            else
            {
                context = new ResidentContext(ResidentId, context.Persona,
                    $"{CurrentAction};精力{energy:0};饥饿{hunger:0};社交{socialNeed:0};{TaskState};{task?.summary}",
                    context.RelationshipSnapshots, context.RelevantMemories);
            }
            var request = new ResidentDecisionRequest(ResidentId, context,
                $"游戏{bootstrap.Clock.ElapsedGameSeconds / 3600:0.0}小时；可执行候选来自真实资源；最近行动{reason}。只选择高层活动。",
                candidates, partner.IsValid ? new[] { partner } : Array.Empty<ResidentId>());
            AiGatewayResult<ResidentDecisionSpec> result = null;
            yield return bootstrap.AiRequests.DecideResident(request, value => result = value);
            if (requestGeneration != generation || version != bootstrap.GatewayConfigurationVersion) yield break;
            decisionPending = false;
            if (result == null || result.Failed || result.ResidentId != ResidentId)
            { lastError = result?.Outcome.Message ?? "决策没有结果，继续本地生活。"; returnToLocal(); yield break; }
            queuedDecisionSource = result.Source == AiGatewayMode.Remote ? "真实模型" : string.IsNullOrEmpty(result.DiagnosticError) ? "本地规则" : "本地规则 / 降级";
            recentDecisionSource = queuedDecisionSource;
            reason = result.Value.Reason;
            lastDecisionGameSeconds = bootstrap.Clock.ElapsedGameSeconds;
            lastError = result.DiagnosticError;
            queuedDecision = result.Value.Intent;
            queuedDecisionIsRemote = result.Source == AiGatewayMode.Remote;
            nextDecisionAt = Math.Max(nextDecisionAt, UnityEngine.Time.realtimeSinceStartupAsDouble + 3);
        }

        private void returnToLocal() { recentDecisionSource = "本地规则 / 降级"; }

        private void ExecuteIntent(string intent)
        {
            LastExecutedIntent = intent;
            switch (intent)
            {
                case "TendFarm": BeginFarmAction(FindFarmAction(null, true)); break;
                case "Fish": BeginActivity("Fish", AvailableActivityTarget("fishing-"), 1200); break;
                case "PickFruit": BeginActivity("PickFruit", AvailableActivityTarget("fruit-"), 360); break;
                case "Refill": BeginActivity("Refill", ResolvePoint("well"), 240); break;
                case "Eat": BeginActivity("Eat", ResolvePoint("plaza"), 300); break;
                case "Rest": BeginActivity("Rest", ResolveHome(), 1800); break;
                case "RequestConversation":
                    ResidentId partner = FindConversationPartner();
                    if (partner.IsValid) socialCoordinator?.RequestConversation(ResidentId, partner);
                    break;
                default:
                    LocationArrivalPoint[] walkPoints = points.Where(point => !point.InteractionPointId.StartsWith("fruit-") &&
                        !point.InteractionPointId.StartsWith("fishing-")).ToArray();
                    BeginActivity("Walk", walkPoints.Length == 0 ? null : walkPoints[(decisionIndex * 3 + ResidentIndex) % walkPoints.Length], 180);
                    break;
            }
        }

        private bool TryExecutePlayerTask()
        {
            source = task.provider == "openai" ? "真实模型" : "本地规则";
            if (task.task_type == "Stop") { StopTask(); return true; }
            if (task.task_type == "Move") { BeginActivity("Move", ResolvePoint(task.target_id), 1); return true; }
            if (task.task_type == "Fish" || task.task_type == "PickFruit")
            {
                string prefix = task.task_type == "Fish" ? "fishing-" : "fruit-";
                LocationArrivalPoint point = string.IsNullOrEmpty(task.target_id) ? AvailableActivityTarget(prefix) : ResolvePoint(task.target_id);
                if (point == null || !bootstrap.Activities.IsAvailable(point.InteractionPointId))
                { TaskState = "等待资源"; lastError = "目标暂被占用或果实未再生。"; farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 5; return false; }
                BeginActivity(task.task_type, point, task.task_type == "Fish" ? 1200 : 360); return true;
            }
            if (task.task_type == "Chat")
            {
                if (ResidentId.TryCreate(task.target_resident_id, out ResidentId other))
                {
                    ActionResult started = socialCoordinator.RequestConversation(ResidentId, other);
                    if (started.Succeeded)
                    {
                        taskConversationId = socialCoordinator.GetConversationId(ResidentId);
                        conversationOwnerTaskId = task.task_id;
                        if (string.IsNullOrEmpty(taskConversationId))
                            FailTask("交谈未创建有效会话，任务没有完成。");
                        else
                            TaskState = "前往交谈 / 等待对话";
                        return true;
                    }
                    lastError = started.Message;
                }
                TaskState = "等待交谈对象"; farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 5; return false;
            }
            INpcAction action = FindFarmAction(task, false);
            if (action != null)
            {
                if ((action is WaterAction && !bootstrap.Inventory.Has(InventoryItem.Water)) ||
                    (action is FertilizeAction && !bootstrap.Inventory.Has(InventoryItem.Fertilizer)))
                {
                    if (action is FertilizeAction && !bootstrap.Inventory.Has(InventoryItem.Compost))
                    {
                        TaskState = "等待肥料 / 可堆肥杂草";
                        lastError = "肥料和堆肥都已用完，等待其他作物长草后除草制肥。";
                        farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 5;
                        return false;
                    }
                    BeginActivity("Refill", ResolvePoint("well"), 240); return true;
                }
                TaskState = "执行玩家任务";
                return BeginFarmAction(action);
            }
            int targetCount = TaskPlots().Length;
            if (completedPlots.Count >= targetCount)
            {
                completedCycles++;
                if (task.repeat) { completedPlots.Clear(); TaskState = "开始下一轮"; return true; }
                CompleteTask(); return true;
            }
            if (task.task_type != "TendFarm")
            {
                if (TaskPlots().Any(plot => !bootstrap.CanWorkPlot(plot, ResidentId) ||
                    town.ReservationService.IsReserved("farm-plot-" + plot, UnityEngine.Time.realtimeSinceStartupAsDouble)))
                { TaskState = "等待地块预约"; farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 3; return false; }
                // A one-shot command does not invent prerequisites or turn into full lifecycle farming.
                FailTask("目标当前没有可执行的" + task.task_type + "动作；请检查作物状态和物品。"); return true;
            }
            TaskState = bootstrap.Field.Plots.Any(plot => TaskPlots().Contains(plot.PlotNumber) && plot.State == PlotState.Empty) &&
                !bootstrap.Inventory.Has(InventoryItem.CarrotSeed) ? "等待种子资源" : "等待作物";
            farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 2;
            return false;
        }

        private bool TickOwnedTaskConversation()
        {
            if (string.IsNullOrEmpty(taskConversationId)) return false;
            if (task == null || task.task_type != "Chat" || task.task_id != conversationOwnerTaskId)
            {
                ClearTaskConversationBinding();
                return false;
            }
            ActionResult resolved = socialCoordinator.ConversationCoordinator.TryGetSession(
                new ConversationId(taskConversationId), ResidentId, out ConversationSession session);
            if (resolved.Failed)
            {
                FailTask("交谈会话已失效，任务没有完成：" + resolved.Message);
                return true;
            }
            if (!session.IsTerminal)
            {
                TaskState = navigator.IsMoving ? "前往交谈" :
                    session.Utterances.Count == 0 ? "等待对话" : "正在交谈";
                return true;
            }
            if (session.State == ConversationState.Completed)
            {
                reason = "已完成与指定居民的完整交谈。";
                CompleteTask();
            }
            else
                FailTask("交谈中断，任务没有完成：" + session.EndReason +
                    (session.EndReason == ConversationEndReason.NavigationFailed ? "；" + socialCoordinator.LastConversationError : ""));
            return true;
        }

        private void ClearTaskConversationBinding()
        {
            taskConversationId = "";
            conversationOwnerTaskId = "";
        }

        private int[] TaskPlots() => task?.target_plot_numbers != null && task.target_plot_numbers.Length > 0
            ? task.target_plot_numbers : Enumerable.Range(1, 9).ToArray();

        private INpcAction FindFarmAction(ResidentTaskSpec plan, bool autonomous)
        {
            int[] targetPlots = plan?.target_plot_numbers != null && plan.target_plot_numbers.Length > 0
                ? plan.target_plot_numbers : Enumerable.Range(1, 9).ToArray();
            string requested = plan?.task_type ?? "TendFarm";
            string[] operations = requested == "TendFarm" ? new[] { "Harvest", "Weed", "Water", "Fertilize", "Sow" } : new[] { requested };
            foreach (string operation in operations)
            foreach (FarmPlot plot in bootstrap.Field.Plots.OrderBy(plot => (plot.PlotNumber + ResidentIndex * 2) % 9))
            {
                if (!targetPlots.Contains(plot.PlotNumber) || plan != null && completedPlots.Contains(plot.PlotNumber)) continue;
                if (!bootstrap.CanWorkPlot(plot.PlotNumber, ResidentId)) continue;
                if (plan != null && !bootstrap.ClaimTaskPlot(plot.PlotNumber, ResidentId)) continue;
                if (town.ReservationService.IsReserved("farm-plot-" + plot.PlotNumber, UnityEngine.Time.realtimeSinceStartupAsDouble)) continue;
                if (operation == "Harvest" && plot.State == PlotState.Mature)
                    return new HarvestAction(plot.PlotNumber, bootstrap.Mode.HarvestActionSeconds);
                if (operation == "Water" && plot.State == PlotState.Growing && plot.WaterLevel < 1 &&
                    (!autonomous || bootstrap.Inventory.Has(InventoryItem.Water))) return new WaterAction(plot.PlotNumber, bootstrap.Mode.WaterActionSeconds);
                if (operation == "Fertilize" && plot.State == PlotState.Growing && plot.WaterLevel > 0 && !plot.IsFertilized &&
                    (!autonomous || bootstrap.Inventory.Has(InventoryItem.Fertilizer))) return new FertilizeAction(plot.PlotNumber, bootstrap.Mode.FertilizeActionSeconds);
                if (operation == "Weed" && plot.HasWeeds) return new WeedAction(plot.PlotNumber, bootstrap.Mode.WeedActionSeconds);
                if (operation == "Sow" && plot.State == PlotState.Empty && bootstrap.Inventory.Has(InventoryItem.CarrotSeed))
                    return new SowAction(plot.PlotNumber, bootstrap.Mode.SowActionSeconds);
            }
            return null;
        }

        private bool BeginFarmAction(INpcAction action)
        {
            if (action == null || !action.TargetPlotNumber.HasValue) return false;
            schedule.SuspendForLifeAction();
            ActionResult reserved = town.ReservationService.TryReserve(ResidentId, "farm-plot-" + action.TargetPlotNumber.Value,
                UnityEngine.Time.realtimeSinceStartupAsDouble, 120, out reservation);
            if (reserved.Failed) { lastError = reserved.Message; schedule.ResumeAfterLifeAction(); return false; }
            activeTarget = "地块 " + action.TargetPlotNumber.Value;
            executor.Enqueue(action);
            return true;
        }

        private void OnFarmActionCompleted(INpcAction action, ActionResult result)
        {
            farmActionFailures = 0;
            ReleaseReservation();
            schedule.ResumeAfterLifeAction();
            energy = Math.Max(0, energy - .6f);
            reason = result.Message;
            if (task != null && action.TargetPlotNumber.HasValue &&
                (task.task_type == "TendFarm" ? action is HarvestAction : action.GetType().Name == task.task_type + "Action"))
                completedPlots.Add(action.TargetPlotNumber.Value);
            if (action is HarvestAction) Announce("刚收获了胡萝卜，作物和下一轮种子已经送入公共仓库。", "harvest");
            nextDecisionAt = Math.Min(nextDecisionAt, UnityEngine.Time.realtimeSinceStartupAsDouble + 1);
        }

        private void OnFarmActionFailed(INpcAction action, ActionResult result)
        {
            lastError = result.Message;
            ReleaseReservation();
            schedule.ResumeAfterLifeAction();
            farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 3;
            farmActionFailures++;
            if (task != null)
            {
                TaskState = "等待路径重试";
                if (farmActionFailures >= 3) FailTask("农事动作连续失败三次，已恢复自主生活：" + result.Message);
            }
        }

        private void BeginActivity(string kind, LocationArrivalPoint point, double duration)
        {
            if (kind == "Fish") duration = bootstrap.Activities.Rules.fishingGameSeconds;
            if (kind == "PickFruit") duration = bootstrap.Activities.Rules.pickingGameSeconds;
            if (kind == "Refill") duration = bootstrap.Activities.Rules.supplyGameSeconds;
            if (point == null) { HandleActivityFailure("缺少可达交互点：" + kind); return; }
            schedule.SuspendForLifeAction();
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            ActionResult reserved = town.ReservationService.TryReserve(ResidentId, point.InteractionPointId, now, 120, out reservation);
            if (reserved.Failed)
            {
                lastError = reserved.Message; schedule.ResumeAfterLifeAction();
                farmRetryAt = now + 3;
                if (task != null) TaskState = "等待位置释放";
                return;
            }
            if (kind == "Fish" || kind == "PickFruit" || kind == "Refill")
            {
                ActionResult resource = bootstrap.Activities.TryReserve(point.InteractionPointId, ResidentId);
                if (resource.Failed) { lastError = resource.Message; ReleaseReservation(); schedule.ResumeAfterLifeAction(); return; }
            }
            navigator.CancelMove();
            ActionResult movement = navigator.BeginMove(point.InteractionPointId);
            if (movement.Failed) { HandleActivityFailure(movement.Message); return; }
            activeActivity = kind;
            activeTarget = point.InteractionPointId;
            movementStarted = now;
            activityDuration = duration;
            moving = true;
        }

        private void TickActivity(double now, double gameNow)
        {
            if (moving)
            {
                ActionResult movement = navigator.Tick(.2f, out bool arrived);
                if (movement.Failed || now - movementStarted > 45)
                {
                    HandleActivityFailure(movement.Failed ? movement.Message : "导航超时，已释放位置重新规划。");
                    return;
                }
                if (!arrived) return;
                moving = false;
                activityStarted = gameNow;
                previousActivityGameSeconds = gameNow;
                // Resource timing starts on actual arrival, not while walking.
                if (activeActivity == "Fish" || activeActivity == "PickFruit" || activeActivity == "Refill")
                {
                    bootstrap.Activities.BeginInteraction(activeTarget, ResidentId);
                }
                ActionResult feedbackStarted = feedback.Begin(activeActivity, (float)activityDuration);
                if (feedbackStarted.Failed) { HandleActivityFailure(feedbackStarted.Message); return; }
            }
            if (feedback.IsPlaying) feedback.Tick((float)Math.Max(0, gameNow - previousActivityGameSeconds), out _);
            previousActivityGameSeconds = gameNow;
            if (gameNow - activityStarted < activityDuration) return;
            string finished = activeActivity;
            ActionResult result = ActionResult.Success();
            if (finished == "Fish") result = bootstrap.Activities.TryFish(activeTarget, ResidentId);
            else if (finished == "PickFruit") result = bootstrap.Activities.TryPickFruit(activeTarget, ResidentId);
            else if (finished == "Refill") result = bootstrap.Activities.RefillSupplies(ResidentId);
            else if (finished == "Eat")
            { result = bootstrap.Activities.ConsumeFood(); if (result.Succeeded) hunger = Math.Max(0, hunger - 50); }
            else if (finished == "Rest" || finished == "Sleep") energy = Math.Min(100, energy + 30);
            CancelActivity();
            if (result.Failed) { HandleActivityFailure(result.Message); return; }
            activityFailures = 0;
            reason = result.Message;
            if (finished == "Fish") Announce("钓到一条鱼！已送到公共仓库，饿的时候可以吃。", "fish");
            if (finished == "PickFruit") Announce("刚摘了成熟果实，已经放进公共仓库。果树需要时间重新结果。", "fruit");
            if (task != null && task.task_type == finished)
            {
                activityCount++;
                if (!task.repeat && activityCount >= task.quantity) CompleteTask();
            }
            nextDecisionAt = Math.Max(nextDecisionAt, now + 1);
        }

        private void CancelActivity()
        {
            navigator?.CancelMove();
            feedback?.Cancel();
            bootstrap?.Activities?.ReleaseAll(ResidentId);
            ReleaseReservation();
            activeActivity = activeTarget = "";
            moving = false;
            schedule?.ResumeAfterLifeAction();
        }

        private void HandleActivityFailure(string error)
        {
            CancelActivity();
            lastError = error;
            activityFailures++;
            farmRetryAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 5;
            nextDecisionAt = Math.Max(nextDecisionAt, farmRetryAt);
            if (task != null)
            {
                TaskState = "等待路径重试";
                if (activityFailures >= 3) FailTask("活动连续失败三次，已释放位置并恢复自主生活：" + error);
            }
        }

        private void ReleaseReservation()
        {
            if (reservation.IsValid) town.ReservationService.Release(reservation);
            reservation = default;
        }

        private LocationArrivalPoint AvailableActivityTarget(string prefix)
        {
            return points.Where(point => point.InteractionPointId.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(point => (Vector3.Distance(transform.position, point.Position) + ResidentIndex * 3) % 40)
                .FirstOrDefault(point => bootstrap.Activities.IsAvailable(point.InteractionPointId) &&
                    (prefix != "fruit-" || bootstrap.Activities.GetFruitRemaining(point.InteractionPointId) > 0) &&
                    !town.ReservationService.IsReserved(point.InteractionPointId, UnityEngine.Time.realtimeSinceStartupAsDouble));
        }

        private LocationArrivalPoint ResolvePoint(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == "home") return ResolveHome();
            if (id == "orchard") return points.Where(point => point.InteractionPointId.StartsWith("fruit-"))
                .OrderBy(point => Vector3.Distance(transform.position, point.Position)).FirstOrDefault();
            if (id == "pond") return points.Where(point => point.InteractionPointId.StartsWith("fishing-"))
                .OrderBy(point => Vector3.Distance(transform.position, point.Position)).FirstOrDefault();
            return points.FirstOrDefault(point => point.InteractionPointId == id) ??
                points.Where(point => point.LocationId.Value.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(point => Vector3.Distance(transform.position, point.Position)).FirstOrDefault();
        }

        private LocationArrivalPoint ResolveHome() => points.FirstOrDefault(point =>
            point.InteractionPointId.IndexOf("home", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (point.InteractionPointId.Contains(ResidentId.Value) || point.InteractionPointId.Contains(new[] { "yaya", "amu", "xiaosui", "momo" }[ResidentIndex]))) ??
            points.Where(point => point.LocationId.Value.IndexOf("home", StringComparison.OrdinalIgnoreCase) >= 0).Skip(ResidentIndex).FirstOrDefault() ?? ResolvePoint("plaza");

        private bool HasFood() => bootstrap.Inventory.Has(InventoryItem.Fruit) || bootstrap.Inventory.Has(InventoryItem.Fish) || bootstrap.Inventory.Has(InventoryItem.Carrot);

        private ResidentId FindConversationPartner()
        {
            foreach (TownLifeController other in bootstrap.LifeControllers.Values.OrderBy(other => Vector3.Distance(transform.position, other.transform.position)))
                if (other != this && other.IsAvailableForConversation && !other.schedule.IsConversationSuspended &&
                    Vector3.Distance(transform.position, other.transform.position) < 30f) return other.ResidentId;
            return default;
        }

        private void Announce(string text, string tag)
        {
            bootstrap.Events.Record(bootstrap.Clock.ElapsedGameSeconds, WorldEventKind.ActionCompleted, text, null,
                WorldEventVisibility.Private, ResidentId, new[] { ResidentId }, tags: new[] { tag, "life-activity" });
            TownDialogueOverlay.Publish(ResidentId, text, source: "local");
        }

        private void CompleteTask()
        {
            ClearTaskConversationBinding();
            task = null;
            activityCount = 0;
            bootstrap.ReleaseTaskPlots(ResidentId);
            TaskState = "任务完成，恢复自主生活";
            lastError = "";
            nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 2;
        }

        private ActionResult FailTask(string error)
        {
            ClearTaskConversationBinding();
            task = null;
            activityCount = 0;
            bootstrap.ReleaseTaskPlots(ResidentId);
            TaskState = "任务失败，恢复自主生活";
            lastError = error;
            nextDecisionAt = UnityEngine.Time.realtimeSinceStartupAsDouble + 3;
            return ActionResult.Failure(ActionFailureReason.InvalidState, error);
        }

        public ResidentTaskSnapshot CaptureTask() => new ResidentTaskSnapshot { residentId = ResidentId.Value,
            hasTask = task != null, task = task, completedPlots = completedPlots.ToArray(), completedCycles = completedCycles,
            completedActivityCount = task == null ? 0 : activityCount,
            energy = energy, hunger = hunger, social = socialNeed };

        public ActionResult RestoreTask(ResidentTaskSnapshot snapshot)
        {
            if (snapshot == null || snapshot.residentId != ResidentId.Value)
                return ActionResult.Failure(ActionFailureReason.InvalidResponse, "存档任务居民不匹配。");
            ActionResult taskPresence = snapshot.NormalizeTaskPresence(ResidentId);
            if (taskPresence.Failed) return taskPresence;
            ActionResult activityProgress = snapshot.ValidateActivityProgress();
            if (activityProgress.Failed) return activityProgress;
            StopTask();
            task = snapshot.task;
            activityCount = task == null ? 0 : snapshot.completedActivityCount;
            if (task != null && IsFarmTask(task.task_type))
                foreach (int plot in TaskPlots()) bootstrap.ClaimTaskPlot(plot, ResidentId);
            foreach (int number in snapshot.completedPlots ?? Array.Empty<int>()) if (number >= 1 && number <= 9) completedPlots.Add(number);
            completedCycles = Math.Max(0, snapshot.completedCycles);
            energy = Mathf.Clamp(snapshot.energy, 0, 100); hunger = Mathf.Clamp(snapshot.hunger, 0, 100); socialNeed = Mathf.Clamp(snapshot.social, 0, 100);
            TaskState = task == null ? "自主生活" : "恢复保存的任务";
            previousGameSeconds = bootstrap.Clock.ElapsedGameSeconds;
            return ActionResult.Success();
        }

        private void ResetTransientState()
        {
            generation++; decisionPending = commandPending = false;
            ClearTaskConversationBinding();
            CancelActivity();
            previousGameSeconds = bootstrap.Clock.ElapsedGameSeconds;
        }

        private static bool IsFarmTask(string kind) => kind == "TendFarm" || kind == "Sow" || kind == "Water" ||
            kind == "Fertilize" || kind == "Weed" || kind == "Harvest";

        private void OnDisable()
        {
            if (!initialized) return;
            generation++;
            decisionPending = commandPending = false;
            CancelActivity();
        }

        private void OnDestroy()
        {
            if (bootstrap != null) bootstrap.AuthoritativeStateResetting -= ResetTransientState;
            if (executor != null) { executor.ActionCompleted -= OnFarmActionCompleted; executor.ActionFailed -= OnFarmActionFailed; }
        }
    }
}
