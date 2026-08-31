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
        private Transform animatedVisualRoot;

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private bool baselineCaptured;
        private bool isWorking;
        private bool isConversing;

        public ResidentDefinitionAsset DefinitionAsset => definitionAsset;

        public ResidentId ResidentId => definitionAsset == null
            ? default
            : definitionAsset.ResidentId;

        public TextMesh NameLabel => nameLabel;

        public TextMesh StatusIcon => statusIcon;

        public bool IsPlayingWorkAnimation => isWorking;

        public bool IsShowingConversation => isConversing;

        public ActionResult Configure(
            ResidentDefinitionAsset residentDefinition,
            Renderer[] renderers,
            TextMesh residentNameLabel,
            TextMesh residentStatusIcon,
            Transform visualRoot,
            Material residentMaterial = null)
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
            return ActionResult.Success($"Resident view configured for {ResidentId}.");
        }

        public void SetScheduleState(
            ResidentScheduleState state,
            ResidentActivityKind? activity)
        {
            if (isConversing)
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
            isConversing = active;
            isWorking = false;
            RestoreBaseline();
            if (statusIcon != null && definitionAsset != null)
            {
                statusIcon.text = active
                    ? $"{definitionAsset.StatusIcon}#"
                    : $"{definitionAsset.StatusIcon}.";
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
    }
}
