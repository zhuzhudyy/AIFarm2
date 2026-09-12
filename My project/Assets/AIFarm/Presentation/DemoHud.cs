using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Inventory;
using AIFarm.Npc;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class DemoHud : MonoBehaviour
    {
        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private Text timeText;

        [SerializeField]
        private Text inventoryText;

        [SerializeField]
        private Text goalText;

        [SerializeField]
        private Text actionText;

        [SerializeField]
        private InputField commandInput;

        [SerializeField]
        private Button submitButton;

        [SerializeField]
        private NpcPlanExecutor planExecutor;

        [SerializeField]
        private ReplanController replanController;

        [SerializeField]
        private Text expressionText;

        [SerializeField]
        private Text actionReasonText;

        [SerializeField]
        private Text worldEventsText;

        [SerializeField]
        private Text moodText;

        [SerializeField]
        private Text emojiText;

        [SerializeField]
        private Button pauseButton;

        [SerializeField]
        private Text pauseButtonLabel;

        [SerializeField]
        private Button speed1Button;

        [SerializeField]
        private Button speed5Button;

        [SerializeField]
        private Button speed20Button;

        [SerializeField]
        private Text aiModeText;

        [SerializeField]
        private Button apiSettingsButton;

        [SerializeField]
        private Text recentMemoriesText;

        [SerializeField]
        private Text recentReflectionsText;

        [SerializeField]
        private Text memoryTitleText;

        [SerializeField]
        private Text personaText;

        [SerializeField]
        private Button yayaResidentButton;

        [SerializeField]
        private Button amuResidentButton;

        [SerializeField]
        private Button xiaosuiResidentButton;

        [SerializeField]
        private Button momoResidentButton;

        [SerializeField]
        private string selectedResidentIdValue = ResidentIds.YayaValue;

        private string idleSubmissionMessage = string.Empty;
        private bool residentSelectionFailed;
        private readonly Dictionary<ResidentId, NpcPlanExecutor> executorsByResidentId =
            new Dictionary<ResidentId, NpcPlanExecutor>();
        private readonly Dictionary<ResidentId, ReplanController> replannersByResidentId =
            new Dictionary<ResidentId, ReplanController>();

        public ResidentId SelectedResidentId
        {
            get
            {
                return ResidentId.TryCreate(selectedResidentIdValue, out ResidentId selected)
                    ? selected
                    : ResidentIds.Yaya;
            }
        }

        public void Configure(
            GameBootstrap gameBootstrap,
            Text timeLabel,
            Text inventoryLabel,
            Text goalLabel,
            Text actionLabel,
            InputField input,
            Button button,
            NpcPlanExecutor executor = null,
            ReplanController controller = null,
            Text expressionLabel = null,
            Text actionReasonLabel = null,
            Text eventsLabel = null,
            Text moodLabel = null,
            Text emojiLabel = null,
            Button pauseControl = null,
            Text pauseControlLabel = null,
            Button speed1Control = null,
            Button speed5Control = null,
            Button speed20Control = null,
            Text aiModeLabel = null,
            Text memoriesLabel = null,
            Text reflectionsLabel = null,
            Button apiSettingsControl = null,
            Text memoryTitleLabel = null,
            Text personaLabel = null,
            Button yayaSelectionControl = null,
            Button amuSelectionControl = null,
            Button xiaosuiSelectionControl = null,
            Button momoSelectionControl = null)
        {
            bootstrap = gameBootstrap;
            timeText = timeLabel;
            inventoryText = inventoryLabel;
            goalText = goalLabel;
            actionText = actionLabel;
            commandInput = input;
            submitButton = button;
            planExecutor = executor;
            replanController = controller;
            expressionText = expressionLabel;
            actionReasonText = actionReasonLabel;
            worldEventsText = eventsLabel;
            moodText = moodLabel;
            emojiText = emojiLabel;
            pauseButton = pauseControl;
            pauseButtonLabel = pauseControlLabel;
            speed1Button = speed1Control;
            speed5Button = speed5Control;
            speed20Button = speed20Control;
            aiModeText = aiModeLabel;
            recentMemoriesText = memoriesLabel;
            recentReflectionsText = reflectionsLabel;
            apiSettingsButton = apiSettingsControl;
            memoryTitleText = memoryTitleLabel;
            personaText = personaLabel;
            yayaResidentButton = yayaSelectionControl;
            amuResidentButton = amuSelectionControl;
            xiaosuiResidentButton = xiaosuiSelectionControl;
            momoResidentButton = momoSelectionControl;
            EnsureResidentBindings();
        }

        public ActionResult RegisterResidentBinding(
            ResidentId residentId,
            NpcPlanExecutor executor,
            ReplanController controller)
        {
            if (!residentId.IsValid || executor == null || controller == null ||
                (executor.IsInitialized && executor.ResidentId != residentId) ||
                (controller.IsInitialized && controller.ResidentId != residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "HUD resident bindings require matching ResidentIds and components.");
            }

            if (executorsByResidentId.ContainsKey(residentId) ||
                replannersByResidentId.ContainsKey(residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"HUD already has a binding for ResidentId '{residentId}'.");
            }

            executorsByResidentId.Add(residentId, executor);
            replannersByResidentId.Add(residentId, controller);
            return ActionResult.Success($"HUD binding registered for '{residentId}'.");
        }

        public ActionResult SelectResident(ResidentId residentId)
        {
            ClearResidentScopedPresentation();
            residentSelectionFailed = true;
            SetAllSelectionControlsInteractable(true);
            RefreshCommandAvailability();
            EnsureResidentBindings();
            if (!residentId.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not available in this HUD.");
            }

            ResidentRegistry registry = bootstrap?.ResidentRegistry;
            if (registry != null)
            {
                if (registry.TryGetDefinition(residentId, out _).Failed ||
                    registry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtimeState).Failed ||
                    runtimeState == null ||
                    runtimeState.ResidentId != residentId ||
                    runtimeState.Memories == null ||
                    runtimeState.Memories.OwnerResidentId != residentId)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        $"ResidentId '{residentId}' is not registered with an isolated runtime.");
                }
            }
            else if (!executorsByResidentId.ContainsKey(residentId) ||
                !replannersByResidentId.ContainsKey(residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not available in this HUD.");
            }

            selectedResidentIdValue = residentId.Value;
            residentSelectionFailed = false;
            idleSubmissionMessage = string.Empty;
            RefreshSelectionControls();
            RefreshCommandAvailability();
            RefreshFromExecutor();
            RefreshWorldEvents();
            RefreshMemoriesAndReflections();
            return ActionResult.Success($"HUD selected resident '{residentId}'.");
        }

        private void Start()
        {
            EnsureResidentBindings();
            if (submitButton != null)
            {
                submitButton.onClick.AddListener(HandleSubmit);
            }

            pauseButton?.onClick.AddListener(HandlePause);
            speed1Button?.onClick.AddListener(HandleSpeed1);
            speed5Button?.onClick.AddListener(HandleSpeed5);
            speed20Button?.onClick.AddListener(HandleSpeed20);
            apiSettingsButton?.onClick.AddListener(HandleApiSettings);
            yayaResidentButton?.onClick.AddListener(HandleSelectYaya);
            amuResidentButton?.onClick.AddListener(HandleSelectAmu);
            xiaosuiResidentButton?.onClick.AddListener(HandleSelectXiaosui);
            momoResidentButton?.onClick.AddListener(HandleSelectMomo);
            if (apiSettingsButton != null)
            {
                apiSettingsButton.interactable = TryBuildLocalApiSettingsUrl(
                    bootstrap?.SceneConfig?.AiGatewayBaseUrl,
                    out _);
            }

            RefreshFromDomain();
            RefreshFromExecutor();
            RefreshWorldEvents();
            RefreshMemoriesAndReflections();
            RefreshSelectionControls();
            RefreshCommandAvailability();
        }

        private void Update()
        {
            RefreshFromDomain();
            RefreshFromExecutor();
            RefreshWorldEvents();
            RefreshMemoriesAndReflections();
        }

        private void OnDestroy()
        {
            if (submitButton != null)
            {
                submitButton.onClick.RemoveListener(HandleSubmit);
            }

            pauseButton?.onClick.RemoveListener(HandlePause);
            speed1Button?.onClick.RemoveListener(HandleSpeed1);
            speed5Button?.onClick.RemoveListener(HandleSpeed5);
            speed20Button?.onClick.RemoveListener(HandleSpeed20);
            apiSettingsButton?.onClick.RemoveListener(HandleApiSettings);
            yayaResidentButton?.onClick.RemoveListener(HandleSelectYaya);
            amuResidentButton?.onClick.RemoveListener(HandleSelectAmu);
            xiaosuiResidentButton?.onClick.RemoveListener(HandleSelectXiaosui);
            momoResidentButton?.onClick.RemoveListener(HandleSelectMomo);
        }

        private void RefreshFromDomain()
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return;
            }

            if (timeText != null)
            {
                string pauseState = bootstrap.Clock.IsPaused ? "  PAUSED" : string.Empty;
                timeText.text =
                    $"{FormatGameTime(bootstrap.Clock.ElapsedGameSeconds)}  " +
                    $"[{bootstrap.Clock.TimeScale:0.#}x]{pauseState}";
            }

            if (pauseButtonLabel != null)
            {
                pauseButtonLabel.text = bootstrap.Clock.IsPaused ? "RESUME" : "PAUSE";
            }

            if (inventoryText != null)
            {
                inventoryText.text =
                    $"Seeds: {bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed)}\n" +
                    $"Water: {bootstrap.Inventory.GetCount(InventoryItem.Water)}\n" +
                    $"Fertilizer: {bootstrap.Inventory.GetCount(InventoryItem.Fertilizer)}\n" +
                    $"Carrots: {bootstrap.Inventory.GetCount(InventoryItem.Carrot)}\n" +
                    $"Moisture drops: {bootstrap.Simulation.WaterDecayEventCount}  " +
                    $"Weeds: {bootstrap.Simulation.WeedEventCount}";
            }
        }

        public ActionResult SubmitCommand(string command)
        {
            if (bootstrap != null && bootstrap.IsInitialized && bootstrap.LifeControllers.Count > 0)
            {
                ResidentId owner = SelectedResidentId;
                ActionResult submitted = bootstrap.SubmitResidentCommand(owner, command);
                if (submitted.Succeeded) CompleteSuccessfulSubmission();
                else SetSubmissionFailure(submitted);
                return submitted;
            }
            ReplanController selectedReplanner = SelectedReplanner;
            NpcPlanExecutor selectedExecutor = SelectedExecutor;
            if (selectedReplanner != null)
            {
                ActionResult goalSubmission = selectedReplanner.SubmitGoal(command);
                if (goalSubmission.Succeeded)
                {
                    CompleteSuccessfulSubmission();
                    return goalSubmission;
                }

                if (goalSubmission.FailureReason != ActionFailureReason.UnsupportedIntent)
                {
                    SetSubmissionFailure(goalSubmission);
                    return goalSubmission;
                }
            }

            if (selectedExecutor == null)
            {
                ActionResult unavailable = ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The NPC executor is not connected to the HUD.");
                SetSubmissionFailure(unavailable);
                return unavailable;
            }

            ActionResult parsed = NpcActionCommandParser.TryParse(command, out INpcAction action);
            if (parsed.Failed)
            {
                SetSubmissionFailure(parsed);
                return parsed;
            }

            ActionResult queued = selectedExecutor.Enqueue(action);
            if (queued.Failed)
            {
                SetSubmissionFailure(queued);
                return queued;
            }

            idleSubmissionMessage = string.Empty;
            if (goalText != null)
            {
                goalText.text = $"Goal: Atomic command - {action.DisplayName}";
            }

            if (actionText != null)
            {
                actionText.text = $"Action: Pending - {action.DisplayName}";
            }

            if (commandInput != null)
            {
                commandInput.text = string.Empty;
            }

            return queued;
        }

        private void HandleSubmit()
        {
            string command = commandInput == null ? string.Empty : commandInput.text;
            SubmitCommand(command);
        }

        public ActionResult TogglePause()
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Time controls require an initialized GameBootstrap.");
            }

            ActionResult result = bootstrap.Clock.IsPaused
                ? bootstrap.Clock.Resume()
                : bootstrap.Clock.Pause();
            if (result.Succeeded)
            {
                string state = bootstrap.Clock.IsPaused ? "暂停" : "继续";
                bootstrap.Events.RecordPublicTownEvent(
                    bootstrap.Clock.ElapsedGameSeconds,
                    WorldEventKind.TimeControlChanged,
                    $"游戏时间已{state}。");
            }

            return result;
        }

        public ActionResult SetTimeScale(double timeScale)
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Time controls require an initialized GameBootstrap.");
            }

            if (timeScale != 1d && timeScale != 5d && timeScale != 20d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Demo time scale must be 1x, 5x, or 20x.");
            }

            ActionResult result = bootstrap.Clock.SetTimeScale(timeScale);
            if (result.Succeeded)
            {
                bootstrap.Events.RecordPublicTownEvent(
                    bootstrap.Clock.ElapsedGameSeconds,
                    WorldEventKind.TimeControlChanged,
                    $"时间倍速调整为 {timeScale:0}x。");
            }

            return result;
        }

        private void HandlePause()
        {
            TogglePause();
        }

        private void HandleSpeed1()
        {
            SetTimeScale(1d);
        }

        private void HandleSpeed5()
        {
            SetTimeScale(5d);
        }

        private void HandleSpeed20()
        {
            SetTimeScale(20d);
        }

        public ActionResult OpenApiSettings()
        {
            ApiGatewaySetupPanel panel = GetComponent<ApiGatewaySetupPanel>();
            if (panel != null)
            {
                panel.Show();
                return ActionResult.Success("游戏内模型设置已展开。");
            }
            if (!TryBuildLocalApiSettingsUrl(
                bootstrap?.SceneConfig?.AiGatewayBaseUrl,
                out string settingsUrl))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "API settings are available only through a loopback AI gateway.");
            }

            Application.OpenURL(settingsUrl);
            return ActionResult.Success("Opened the local AI gateway settings page.");
        }

        public static bool TryBuildLocalApiSettingsUrl(
            string gatewayBaseUrl,
            out string settingsUrl)
        {
            settingsUrl = string.Empty;
            string candidate = (gatewayBaseUrl ?? string.Empty).Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri gatewayUri) ||
                (gatewayUri.Scheme != Uri.UriSchemeHttp &&
                    gatewayUri.Scheme != Uri.UriSchemeHttps) ||
                !gatewayUri.IsLoopback ||
                !string.IsNullOrEmpty(gatewayUri.UserInfo) ||
                !string.IsNullOrEmpty(gatewayUri.Query) ||
                !string.IsNullOrEmpty(gatewayUri.Fragment))
            {
                return false;
            }

            var builder = new UriBuilder(gatewayUri)
            {
                Path = $"{gatewayUri.AbsolutePath.TrimEnd('/')}/setup",
                Query = string.Empty,
                Fragment = string.Empty
            };
            settingsUrl = builder.Uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        private void HandleApiSettings()
        {
            ActionResult result = OpenApiSettings();
            if (result.Failed)
            {
                Debug.LogWarning(result.Message, this);
            }
        }

        private void RefreshFromExecutor()
        {
            if (residentSelectionFailed)
            {
                ClearResidentScopedPresentation();
                RefreshCommandAvailability();
                return;
            }

            RefreshGoalAndExpression();
            RefreshCommandAvailability();
            ReplanController selectedReplanner = SelectedReplanner;
            NpcPlanExecutor selectedExecutor = SelectedExecutor;
            if (actionText == null)
            {
                return;
            }

            if (bootstrap != null && bootstrap.LifeControllers.TryGetValue(SelectedResidentId, out TownLifeController life))
            {
                actionText.text = $"{SelectedResidentDisplayName}：{life.CurrentAction}";
                if (goalText != null) goalText.text = "任务：" + life.TaskState;
                if (expressionText != null) expressionText.text = "决策来源：" + life.ExecutionSource;
                if (actionReasonText != null) actionReasonText.text = string.IsNullOrEmpty(life.LastError) ? life.TaskState : life.LastError;
                return;
            }

            if (selectedReplanner != null && selectedReplanner.IsGatewayRequestPending)
            {
                actionText.text = "Action: Waiting for Remote AI";
                return;
            }

            if (selectedReplanner != null &&
                selectedReplanner.Status == ReplanStatus.Idle &&
                selectedReplanner.LastGatewaySubmissionResult.HasValue &&
                selectedReplanner.LastGatewaySubmissionResult.Value.Failed)
            {
                actionText.text =
                    $"Action: Rejected - {selectedReplanner.LastGatewaySubmissionResult.Value.Message}";
                return;
            }

            if (selectedExecutor == null)
            {
                actionText.text =
                    $"Action: {SelectedResidentDisplayName}正按本地日程活动（无玩家指令执行器）";
                return;
            }

            if (selectedExecutor.Status == NpcExecutionStatus.Failed)
            {
                actionText.text = $"Action: Failed - {selectedExecutor.LastFailureReason}";
                return;
            }

            if (selectedExecutor.CurrentAction != null)
            {
                actionText.text =
                    $"Action: {selectedExecutor.Status} - {selectedExecutor.CurrentAction.DisplayName}";
                return;
            }

            if (selectedExecutor.IsBusy)
            {
                actionText.text = $"Action: {selectedExecutor.Status}";
                return;
            }

            if (!string.IsNullOrEmpty(idleSubmissionMessage))
            {
                actionText.text = idleSubmissionMessage;
                return;
            }

            if (selectedExecutor.LastResult.HasValue && selectedExecutor.LastResult.Value.Succeeded)
            {
                actionText.text = $"Action: Completed - {selectedExecutor.LastResult.Value.Message}";
                return;
            }

            actionText.text = "Action: Idle - submit a full-field goal";
        }

        private void RefreshGoalAndExpression()
        {
            ReplanController selectedReplanner = SelectedReplanner;
            if (aiModeText != null)
            {
                AiGatewayMode mode = selectedReplanner != null
                    ? selectedReplanner.CurrentAiMode
                    : bootstrap?.SceneConfig?.AiGatewayMode ?? AiGatewayMode.Local;
                GatewayConnectionController gateway = FindFirstObjectByType<GatewayConnectionController>();
                aiModeText.text = gateway == null ? "AI：未配置" : "AI：" + gateway.StateLabel;
            }

            if (selectedReplanner == null)
            {
                if (goalText != null)
                {
                    goalText.text = $"Goal: {SelectedResidentDisplayName}当前无玩家指定目标";
                }

                if (expressionText != null)
                {
                    expressionText.text = $"{SelectedResidentDisplayName}: 正在执行确定性小镇日程。";
                }

                if (actionReasonText != null)
                {
                    actionReasonText.text = "Reason: 由 Unity 本地日程驱动";
                }

                if (moodText != null)
                {
                    moodText.text = "Mood: Local";
                }

                if (emojiText != null)
                {
                    emojiText.text = "•";
                }

                return;
            }

            if (goalText != null)
            {
                goalText.text = $"Goal: {selectedReplanner.CurrentGoalText}";
            }

            if (expressionText != null)
            {
                expressionText.text = $"{SelectedResidentDisplayName}: {selectedReplanner.NpcExpression}";
            }

            if (actionReasonText != null)
            {
                string reason = string.IsNullOrWhiteSpace(selectedReplanner.CurrentDecisionReason)
                    ? "等待目标"
                    : selectedReplanner.CurrentDecisionReason;
                actionReasonText.text = $"Reason: {reason}";
            }

            if (moodText != null)
            {
                moodText.text = $"Mood: {selectedReplanner.CurrentMood}";
            }

            if (emojiText != null)
            {
                emojiText.text = selectedReplanner.CurrentEmoji;
            }
        }

        private void RefreshWorldEvents()
        {
            if (worldEventsText == null)
            {
                return;
            }

            worldEventsText.text = string.Empty;
            if (residentSelectionFailed)
            {
                return;
            }

            WorldEventLog events = bootstrap?.Events ?? SelectedReplanner?.WorldEvents;
            if (events == null)
            {
                return;
            }

            IReadOnlyList<WorldEventEntry> visibleEntries =
                events.GetVisibleEntries(SelectedResidentId);
            if (visibleEntries == null || visibleEntries.Count == 0)
            {
                worldEventsText.text = "No visible world events yet.";
                return;
            }

            var builder = new StringBuilder();
            for (int index = visibleEntries.Count - 1; index >= 0; index--)
            {
                WorldEventEntry entry = visibleEntries[index];
                builder.Append('[')
                    .Append(FormatEventTime(entry.GameSeconds))
                    .Append("] ")
                    .Append(entry.Message);
                if (index > 0)
                {
                    builder.AppendLine();
                }
            }

            worldEventsText.text = builder.ToString();
        }

        private void RefreshMemoriesAndReflections()
        {
            if (memoryTitleText != null)
            {
                memoryTitleText.text = string.Empty;
            }

            if (personaText != null)
            {
                personaText.text = string.Empty;
            }

            if (recentMemoriesText != null)
            {
                recentMemoriesText.text = string.Empty;
            }

            if (recentReflectionsText != null)
            {
                recentReflectionsText.text = string.Empty;
            }

            if (residentSelectionFailed)
            {
                return;
            }

            if (!TryResolveSelectedRuntime(
                out ResidentDefinition definition,
                out ResidentRuntimeState runtimeState))
            {
                return;
            }

            MemoryStore memoryStore = runtimeState.Memories;
            ResidentId selectedResidentId = SelectedResidentId;
            if (memoryStore == null ||
                memoryStore.OwnerResidentId != selectedResidentId ||
                memoryStore.GetRecent(
                    selectedResidentId,
                    memoryStore.Capacity,
                    out IReadOnlyList<MemoryEntry> recentEntries).Failed)
            {
                return;
            }

            if (memoryTitleText != null)
            {
                memoryTitleText.text = $"{definition.DisplayName} // MEMORY & REFLECTION";
            }

            if (personaText != null)
            {
                NpcPersonaDefinition persona = definition.Persona;
                personaText.text =
                    $"{persona.Role}｜{string.Join("、", persona.PersonalityTraits)}｜" +
                    $"偏好：{persona.Preference}｜不喜欢：{persona.Dislike}";
            }

            if (recentMemoriesText != null)
            {
                var memories = new List<MemoryEntry>(6);
                foreach (MemoryEntry entry in recentEntries)
                {
                    if (entry.Kind != MemoryEntryKind.Reflection)
                    {
                        memories.Add(entry);
                    }

                    if (memories.Count == 6)
                    {
                        break;
                    }
                }

                if (memories.Count == 0)
                {
                    recentMemoriesText.text = $"{SelectedResidentDisplayName}还没有新的观察。";
                }
                else
                {
                    var builder = new StringBuilder();
                    foreach (MemoryEntry memory in memories)
                    {
                        builder.Append("[重要性 ")
                            .Append(memory.Importance)
                            .Append("] ")
                            .Append(memory.Text)
                            .AppendLine();
                    }

                    recentMemoriesText.text = builder.ToString().TrimEnd();
                }
            }

            if (recentReflectionsText != null)
            {
                var reflections = new List<MemoryEntry>(3);
                foreach (MemoryEntry entry in recentEntries)
                {
                    if (entry.Kind == MemoryEntryKind.Reflection)
                    {
                        reflections.Add(entry);
                    }

                    if (reflections.Count == 3)
                    {
                        break;
                    }
                }

                if (reflections.Count == 0)
                {
                    recentReflectionsText.text = "反思：完成一轮种植后生成。";
                }
                else
                {
                    var builder = new StringBuilder();
                    foreach (MemoryEntry reflection in reflections)
                    {
                        builder.Append("• ")
                            .Append(reflection.Text)
                            .AppendLine();
                    }

                    recentReflectionsText.text = builder.ToString().TrimEnd();
                }
            }
        }

        private bool TryResolveSelectedRuntime(
            out ResidentDefinition definition,
            out ResidentRuntimeState runtimeState)
        {
            definition = null;
            runtimeState = null;
            ResidentRegistry registry = bootstrap?.ResidentRegistry;
            ResidentId selectedResidentId = SelectedResidentId;
            if (registry != null)
            {
                if (registry.TryGetDefinition(selectedResidentId, out definition).Failed ||
                    registry.TryGetRuntimeState(selectedResidentId, out runtimeState).Failed)
                {
                    definition = null;
                    runtimeState = null;
                    return false;
                }
            }
            else
            {
                ReplanController selectedReplanner = SelectedReplanner;
                runtimeState = selectedReplanner?.RuntimeState;
                definition = runtimeState?.Definition;
            }

            if (
                definition == null ||
                runtimeState == null ||
                definition.ResidentId != selectedResidentId ||
                runtimeState.ResidentId != selectedResidentId ||
                runtimeState.Memories == null ||
                runtimeState.Memories.OwnerResidentId != selectedResidentId)
            {
                definition = null;
                runtimeState = null;
                return false;
            }

            return true;
        }

        private void ClearResidentScopedPresentation()
        {
            idleSubmissionMessage = string.Empty;
            Text[] residentScopedLabels =
            {
                goalText,
                actionText,
                expressionText,
                actionReasonText,
                worldEventsText,
                moodText,
                emojiText,
                memoryTitleText,
                personaText,
                recentMemoriesText,
                recentReflectionsText
            };
            foreach (Text label in residentScopedLabels)
            {
                if (label != null)
                {
                    label.text = string.Empty;
                }
            }
        }

        private void RefreshCommandAvailability()
        {
            bool canSubmitPlayerCommand = !residentSelectionFailed && (SelectedExecutor != null ||
                (bootstrap != null && bootstrap.LifeControllers.ContainsKey(SelectedResidentId)));
            if (submitButton != null)
            {
                submitButton.interactable = canSubmitPlayerCommand;
            }

            if (commandInput != null)
            {
                commandInput.interactable = canSubmitPlayerCommand;
            }
        }

        private void RefreshSelectionControls()
        {
            SetSelectionControlState(yayaResidentButton, ResidentIds.Yaya);
            SetSelectionControlState(amuResidentButton, ResidentIds.Amu);
            SetSelectionControlState(xiaosuiResidentButton, ResidentIds.Xiaosui);
            SetSelectionControlState(momoResidentButton, ResidentIds.Momo);
        }

        private void SetSelectionControlState(Button button, ResidentId residentId)
        {
            if (button != null)
            {
                button.interactable = residentId != SelectedResidentId;
            }
        }

        private void SetAllSelectionControlsInteractable(bool interactable)
        {
            Button[] selectionControls =
            {
                yayaResidentButton,
                amuResidentButton,
                xiaosuiResidentButton,
                momoResidentButton
            };
            foreach (Button button in selectionControls)
            {
                if (button != null)
                {
                    button.interactable = interactable;
                }
            }
        }

        private void HandleSelectYaya()
        {
            SelectResident(ResidentIds.Yaya);
        }

        private void HandleSelectAmu()
        {
            SelectResident(ResidentIds.Amu);
        }

        private void HandleSelectXiaosui()
        {
            SelectResident(ResidentIds.Xiaosui);
        }

        private void HandleSelectMomo()
        {
            SelectResident(ResidentIds.Momo);
        }

        private NpcPlanExecutor SelectedExecutor
        {
            get
            {
                EnsureResidentBindings();
                executorsByResidentId.TryGetValue(
                    SelectedResidentId,
                    out NpcPlanExecutor selected);
                return selected;
            }
        }

        private ReplanController SelectedReplanner
        {
            get
            {
                EnsureResidentBindings();
                replannersByResidentId.TryGetValue(
                    SelectedResidentId,
                    out ReplanController selected);
                return selected;
            }
        }

        private string SelectedResidentDisplayName
        {
            get
            {
                if (bootstrap?.ResidentRegistry != null &&
                    bootstrap.ResidentRegistry.TryGetDefinition(
                        SelectedResidentId,
                        out ResidentDefinition definition).Succeeded)
                {
                    return definition.DisplayName;
                }

                return SelectedReplanner?.Persona?.Name ?? SelectedResidentId.Value;
            }
        }

        private void EnsureResidentBindings()
        {
            if (!ResidentId.TryCreate(selectedResidentIdValue, out _))
            {
                selectedResidentIdValue = ResidentIds.YayaValue;
            }

            if (planExecutor != null)
            {
                ResidentId owner = planExecutor.ResidentId;
                if (!executorsByResidentId.ContainsKey(owner))
                {
                    executorsByResidentId.Add(owner, planExecutor);
                }
            }

            if (replanController != null)
            {
                ResidentId owner = replanController.ResidentId;
                if (!replannersByResidentId.ContainsKey(owner))
                {
                    replannersByResidentId.Add(owner, replanController);
                }
            }
        }

        private void CompleteSuccessfulSubmission()
        {
            idleSubmissionMessage = string.Empty;
            if (commandInput != null)
            {
                commandInput.text = string.Empty;
            }

            RefreshGoalAndExpression();
        }

        private void SetSubmissionFailure(ActionResult failure)
        {
            idleSubmissionMessage = $"Action: Rejected - {failure.Message}";
            if (actionText != null)
            {
                actionText.text = idleSubmissionMessage;
            }
        }

        private static string FormatGameTime(double elapsedGameSeconds)
        {
            int totalMinutes = (int)(elapsedGameSeconds / 60d);
            int day = totalMinutes / (24 * 60) + 1;
            int minuteOfDay = totalMinutes % (24 * 60);
            int hour = minuteOfDay / 60;
            int minute = minuteOfDay % 60;
            return $"Day {day}  {hour:00}:{minute:00}";
        }

        private static string FormatEventTime(double elapsedGameSeconds)
        {
            int totalMinutes = (int)(elapsedGameSeconds / 60d);
            int day = totalMinutes / (24 * 60) + 1;
            int minuteOfDay = totalMinutes % (24 * 60);
            return $"D{day} {minuteOfDay / 60:00}:{minuteOfDay % 60:00}";
        }
    }
}
