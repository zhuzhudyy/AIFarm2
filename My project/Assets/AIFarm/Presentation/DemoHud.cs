using AIFarm.Core;
using AIFarm.Inventory;
using AIFarm.Npc;
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
            Text expressionLabel = null)
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
        }

        private void Start()
        {
            if (submitButton != null)
            {
                submitButton.onClick.AddListener(HandleSubmit);
            }

            RefreshFromDomain();
            RefreshFromExecutor();
        }

        private void Update()
        {
            RefreshFromDomain();
            RefreshFromExecutor();
        }

        private void OnDestroy()
        {
            if (submitButton != null)
            {
                submitButton.onClick.RemoveListener(HandleSubmit);
            }
        }

        private void RefreshFromDomain()
        {
            if (bootstrap == null || !bootstrap.IsInitialized)
            {
                return;
            }

            if (timeText != null)
            {
                timeText.text = FormatGameTime(bootstrap.Clock.ElapsedGameSeconds);
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
    }
}
