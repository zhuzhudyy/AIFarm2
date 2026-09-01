using System;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ResidentBlockoutView : MonoBehaviour
    {
        [SerializeField]
        private ResidentDefinitionAsset definitionAsset;

        [SerializeField]
        private Renderer[] bodyRenderers = Array.Empty<Renderer>();

        [SerializeField]
        private TextMesh nameLabel;

        [SerializeField]
        private TextMesh statusIcon;

        [SerializeField]
        private TextMesh conversationText;

        [SerializeField]
        private Transform animatedVisualRoot;

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private bool baselineCaptured;
        private bool isWorking;
        private bool isConversing;
        private bool isAttendingTownEvent;

        public ResidentDefinitionAsset DefinitionAsset => definitionAsset;

        public ResidentId ResidentId => definitionAsset == null
            ? default
            : definitionAsset.ResidentId;

        public TextMesh NameLabel => nameLabel;

        public TextMesh StatusIcon => statusIcon;

        public TextMesh ConversationText => conversationText;

        public bool IsPlayingWorkAnimation => isWorking;

        public bool IsShowingConversation => isConversing;

        public bool IsAttendingTownEvent => isAttendingTownEvent;

        public string LastConversationLine { get; private set; } = string.Empty;

        public string LastConversationEmoji { get; private set; } = string.Empty;

        public NpcMood? LastConversationMood { get; private set; }

        public ActionResult Configure(
            ResidentDefinitionAsset residentDefinition,
            Renderer[] renderers,
            TextMesh residentNameLabel,
            TextMesh residentStatusIcon,
            Transform visualRoot,
            Material residentMaterial = null,
            TextMesh residentConversationText = null)
        {
            if (residentDefinition == null || !residentDefinition.ResidentId.IsValid ||
                renderers == null || renderers.Length == 0 || residentNameLabel == null ||
                residentStatusIcon == null || visualRoot == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Resident view requires a definition, body renderers, labels, and visual root.");
            }

            foreach (Renderer bodyRenderer in renderers)
            {
                if (bodyRenderer == null)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidArgument,
                        "Resident body renderers cannot contain null values.");
                }
            }

            definitionAsset = residentDefinition;
            bodyRenderers = (Renderer[])renderers.Clone();
            nameLabel = residentNameLabel;
            statusIcon = residentStatusIcon;
            conversationText = residentConversationText;
            animatedVisualRoot = visualRoot;
            if (residentMaterial != null)
            {
                foreach (Renderer bodyRenderer in bodyRenderers)
                {
                    bodyRenderer.sharedMaterial = residentMaterial;
                }
            }

            CaptureBaseline();
            ApplyDefinition();
            SetScheduleState(ResidentScheduleState.WaitingForSchedule, null);
            ClearConversationLine();
            return ActionResult.Success($"Resident view configured for {ResidentId}.");
        }

        public void SetScheduleState(
            ResidentScheduleState state,
            ResidentActivityKind? activity)
        {
            if (isConversing || isAttendingTownEvent)
            {
                return;
            }

            isWorking = state == ResidentScheduleState.Working;
            if (statusIcon != null && definitionAsset != null)
            {
                string stateMark;
                switch (state)
                {
                    case ResidentScheduleState.Moving:
                        stateMark = ">";
                        break;
                    case ResidentScheduleState.Working:
                        stateMark = activity == ResidentActivityKind.Home ? "H" : "*";
                        break;
                    case ResidentScheduleState.WaitingToRetry:
                        stateMark = "!";
                        break;
                    case ResidentScheduleState.Suspended:
                        stateMark = "F";
                        break;
                    default:
                        stateMark = ".";
                        break;
                }

                statusIcon.text = $"{definitionAsset.StatusIcon}{stateMark}";
            }

            if (!isWorking)
            {
                RestoreBaseline();
            }
        }

        public void SetConversationState(bool active)
        {
            if (active && isAttendingTownEvent)
            {
                return;
            }

            isConversing = active;
            isWorking = false;
            RestoreBaseline();
            if (statusIcon != null && definitionAsset != null)
            {
                statusIcon.text = active
                    ? $"{definitionAsset.StatusIcon}#"
                    : $"{definitionAsset.StatusIcon}.";
            }

            if (!active)
            {
                ClearConversationLine();
            }
        }

        public void ShowConversationLine(string text, string emoji, NpcMood mood)
        {
            string boundedText = (text ?? string.Empty).Trim();
            string boundedEmoji = (emoji ?? string.Empty).Trim();
            if (boundedText.Length == 0 || boundedText.Length > 300 ||
                boundedEmoji.Length == 0 || boundedEmoji.Length > 8 ||
                !Enum.IsDefined(typeof(NpcMood), mood))
            {
                return;
            }

            EnsureConversationText();
            LastConversationLine = boundedText;
            LastConversationEmoji = boundedEmoji;
            LastConversationMood = mood;
            if (conversationText != null)
            {
                conversationText.text = $"{boundedEmoji} {boundedText}\n[{mood}]";
                conversationText.gameObject.SetActive(true);
            }

            if (statusIcon != null && definitionAsset != null)
            {
                statusIcon.text = $"{definitionAsset.StatusIcon}{boundedEmoji}";
            }
        }

        public void ClearConversationLine()
        {
            LastConversationLine = string.Empty;
            LastConversationEmoji = string.Empty;
            LastConversationMood = null;
            if (conversationText != null)
            {
                conversationText.text = string.Empty;
                conversationText.gameObject.SetActive(false);
            }
        }

        public void SetTownEventState(TownEventState state)
        {
            bool active = state == TownEventState.Gathering ||
                state == TownEventState.Active;
            isAttendingTownEvent = active;
            isWorking = state == TownEventState.Active;
            if (!isWorking)
            {
                RestoreBaseline();
            }

            if (statusIcon != null && definitionAsset != null)
            {
                statusIcon.text = active
                    ? $"{definitionAsset.StatusIcon}🍲"
                    : $"{definitionAsset.StatusIcon}.";
            }

            if (!active)
            {
                ClearConversationLine();
            }
        }

        public void ShowTownEventLine(string text, string emoji, NpcMood mood)
        {
            if (isAttendingTownEvent)
            {
                ShowConversationLine(text, emoji, mood);
            }
        }

        private void Awake()
        {
            if (animatedVisualRoot != null)
            {
                CaptureBaseline();
            }

            ApplyDefinition();
        }

        private void LateUpdate()
        {
            if (!isWorking || animatedVisualRoot == null)
            {
                return;
            }

            if (!baselineCaptured)
            {
                CaptureBaseline();
            }

            float phase = UnityEngine.Time.time * 4f + ResidentId.GetHashCode() * 0.01f;
            float wave = Mathf.Sin(phase);
            animatedVisualRoot.localPosition = baseLocalPosition + Vector3.up * (wave * 0.07f);
            animatedVisualRoot.localRotation = baseLocalRotation *
                Quaternion.Euler(0f, wave * 7f, wave * 2f);
        }

        private void OnDisable()
        {
            isWorking = false;
            isConversing = false;
            isAttendingTownEvent = false;
            ClearConversationLine();
            RestoreBaseline();
        }

        private void CaptureBaseline()
        {
            if (animatedVisualRoot == null)
            {
                return;
            }

            baseLocalPosition = animatedVisualRoot.localPosition;
            baseLocalRotation = animatedVisualRoot.localRotation;
            baselineCaptured = true;
        }

        private void RestoreBaseline()
        {
            if (animatedVisualRoot != null && baselineCaptured)
            {
                animatedVisualRoot.localPosition = baseLocalPosition;
                animatedVisualRoot.localRotation = baseLocalRotation;
            }
        }

        private void ApplyDefinition()
        {
            if (definitionAsset == null)
            {
                return;
            }

            if (nameLabel != null)
            {
                nameLabel.text = definitionAsset.DisplayName;
            }

            if (statusIcon != null && string.IsNullOrWhiteSpace(statusIcon.text))
            {
                statusIcon.text = definitionAsset.StatusIcon;
            }
        }

        private void EnsureConversationText()
        {
            if (conversationText != null)
            {
                return;
            }

            var labelObject = new GameObject("Resident_Conversation_Label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 4.05f, 0f);
            conversationText = labelObject.AddComponent<TextMesh>();
            conversationText.anchor = TextAnchor.LowerCenter;
            conversationText.alignment = TextAlignment.Center;
            conversationText.fontSize = 42;
            conversationText.characterSize = 0.045f;
            conversationText.color = Color.white;
            labelObject.AddComponent<WorldSpaceBillboard>();
        }
    }
}
