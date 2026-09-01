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
        private string selectedResidentIdValue = ResidentIds.YayaValue;

        private string idleSubmissionMessage = string.Empty;
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
            Button apiSettingsControl = null)
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
            EnsureResidentBindings();
            if (!residentId.IsValid ||
                !executorsByResidentId.ContainsKey(residentId) ||
                !replannersByResidentId.ContainsKey(residentId))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not available in this HUD.");
            }

            if (bootstrap?.ResidentRegistry != null &&
                bootstrap.ResidentRegistry.TryGetDefinition(residentId, out _).Failed)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"ResidentId '{residentId}' is not registered in the active town.");
            }

            selectedResidentIdValue = residentId.Value;
            RefreshFromExecutor();
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
                bootstrap.Events.Record(
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
                bootstrap.Events.Record(
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
            RefreshGoalAndExpression();
            ReplanController selectedReplanner = SelectedReplanner;
            NpcPlanExecutor selectedExecutor = SelectedExecutor;
            if (actionText == null)
            {
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
                aiModeText.text = $"AI: {mode.ToString().ToUpperInvariant()}";
            }

            if (selectedReplanner == null)
            {
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

            WorldEventLog events = bootstrap?.Events ?? SelectedReplanner?.WorldEvents;
            if (events == null || events.Entries.Count == 0)
            {
                worldEventsText.text = "No world events yet.";
                return;
            }

            var builder = new StringBuilder();
            for (int index = events.Entries.Count - 1; index >= 0; index--)
            {
                WorldEventEntry entry = events.Entries[index];
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
            ReplanController selectedReplanner = SelectedReplanner;
            if (recentMemoriesText != null)
            {
                IReadOnlyList<MemoryEntry> memories = selectedReplanner?.RecentMemories;
                if (memories == null || memories.Count == 0)
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
                MemoryStore memoryStore = selectedReplanner?.Memories;
                IReadOnlyList<MemoryEntry> reflections =
                    memoryStore?.GetRecentReflections(3);
                if (reflections == null || reflections.Count == 0)
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
