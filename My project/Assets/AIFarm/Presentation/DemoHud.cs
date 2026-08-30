using AIFarm.Core;
using AIFarm.Inventory;
using AIFarm.Npc;
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

        private string idleSubmissionMessage = string.Empty;

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
            Button speed20Control = null)
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
        }

        private void Start()
        {
            if (submitButton != null)
            {
                submitButton.onClick.AddListener(HandleSubmit);
            }

            pauseButton?.onClick.AddListener(HandlePause);
            speed1Button?.onClick.AddListener(HandleSpeed1);
            speed5Button?.onClick.AddListener(HandleSpeed5);
            speed20Button?.onClick.AddListener(HandleSpeed20);

            RefreshFromDomain();
            RefreshFromExecutor();
            RefreshWorldEvents();
        }

        private void Update()
        {
            RefreshFromDomain();
            RefreshFromExecutor();
            RefreshWorldEvents();
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
            if (replanController != null)
            {
                ActionResult goalSubmission = replanController.SubmitGoal(command);
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

            if (planExecutor == null)
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

            ActionResult queued = planExecutor.Enqueue(action);
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

        private void RefreshFromExecutor()
        {
            RefreshGoalAndExpression();
            if (actionText == null || planExecutor == null)
            {
                return;
            }

            if (planExecutor.Status == NpcExecutionStatus.Failed)
            {
                actionText.text = $"Action: Failed - {planExecutor.LastFailureReason}";
                return;
            }

            if (planExecutor.CurrentAction != null)
            {
                actionText.text =
                    $"Action: {planExecutor.Status} - {planExecutor.CurrentAction.DisplayName}";
                return;
            }

            if (planExecutor.IsBusy)
            {
                actionText.text = $"Action: {planExecutor.Status}";
                return;
            }

            if (!string.IsNullOrEmpty(idleSubmissionMessage))
            {
                actionText.text = idleSubmissionMessage;
                return;
            }

            if (planExecutor.LastResult.HasValue && planExecutor.LastResult.Value.Succeeded)
            {
                actionText.text = $"Action: Completed - {planExecutor.LastResult.Value.Message}";
                return;
            }

            actionText.text = "Action: Idle - submit an offline full-field goal";
        }

        private void RefreshGoalAndExpression()
        {
            if (replanController == null)
            {
                return;
            }

            if (goalText != null)
            {
                goalText.text = $"Goal: {replanController.CurrentGoalText}";
            }

            if (expressionText != null)
            {
                expressionText.text = $"NPC: {replanController.NpcExpression}";
            }

            if (actionReasonText != null)
            {
                string reason = string.IsNullOrWhiteSpace(replanController.CurrentDecisionReason)
                    ? "等待目标"
                    : replanController.CurrentDecisionReason;
                actionReasonText.text = $"Reason: {reason}";
            }

            if (moodText != null)
            {
                moodText.text = $"Mood: {replanController.CurrentMood}";
            }

            if (emojiText != null)
            {
                emojiText.text = replanController.CurrentEmoji;
            }
        }

        private void RefreshWorldEvents()
        {
            if (worldEventsText == null)
            {
                return;
            }

            WorldEventLog events = bootstrap?.Events ?? replanController?.WorldEvents;
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
