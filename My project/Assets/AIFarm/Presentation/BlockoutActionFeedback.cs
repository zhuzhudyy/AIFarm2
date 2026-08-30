using AIFarm.Core;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class BlockoutActionFeedback : MonoBehaviour, INpcActionFeedback
    {
        [SerializeField]
        private Transform visualRoot;

        [SerializeField]
        private GameObject progressRoot;

        [SerializeField]
        private Transform progressFill;

        private Vector3 baseVisualScale;
        private Quaternion baseVisualRotation;
        private Vector3 fullFillScale;
        private Vector3 fullFillPosition;
        private float elapsedSeconds;
        private float durationSeconds;
        private bool hasBaseline;

        public bool IsPlaying { get; private set; }

        public float Progress { get; private set; }

        public string CurrentActionName { get; private set; } = string.Empty;

        public ActionResult Configure(Transform npcVisual, GameObject progressBarRoot, Transform fillTransform)
        {
            if (npcVisual == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Blockout feedback requires an NPC visual transform.");
            }

            if ((progressBarRoot == null) != (fillTransform == null))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Progress feedback requires both a root object and a fill transform.");
            }

            visualRoot = npcVisual;
            progressRoot = progressBarRoot;
            progressFill = fillTransform;
            CaptureBaseline();
            ResetVisuals();
            return ActionResult.Success("Blockout NPC action feedback configured.");
        }

        public ActionResult Begin(string actionName, float actionDurationSeconds)
        {
            if (IsPlaying)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC action feedback is already playing.");
            }

            if (visualRoot == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC action feedback has no visual transform.");
            }

            if (string.IsNullOrWhiteSpace(actionName) ||
                float.IsNaN(actionDurationSeconds) ||
                float.IsInfinity(actionDurationSeconds) ||
                actionDurationSeconds <= 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC feedback requires an action name and a finite, positive duration.");
            }

            if (!hasBaseline)
            {
                CaptureBaseline();
            }

            CurrentActionName = actionName;
            durationSeconds = actionDurationSeconds;
            elapsedSeconds = 0f;
            Progress = 0f;
            IsPlaying = true;
            if (progressRoot != null)
            {
                progressRoot.SetActive(true);
            }

            ApplyProgress(0f);
            return ActionResult.Success($"Started feedback for {actionName}.");
        }

        public ActionResult Tick(float deltaTime, out bool completed)
        {
            completed = false;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC feedback delta time must be finite and non-negative.");
            }

            if (!IsPlaying)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "NPC action feedback is not playing.");
            }

            elapsedSeconds = Mathf.Min(durationSeconds, elapsedSeconds + deltaTime);
            Progress = durationSeconds <= 0f ? 1f : elapsedSeconds / durationSeconds;
            ApplyProgress(Progress);
            if (Progress < 1f)
            {
                return ActionResult.Success();
            }

            completed = true;
            string completedActionName = CurrentActionName;
            ResetVisuals();
            return ActionResult.Success($"Completed feedback for {completedActionName}.");
        }

        public ActionResult Cancel()
        {
            ResetVisuals();
            return ActionResult.Success("NPC action feedback cancelled.");
        }

        private void Awake()
        {
            if (visualRoot != null)
            {
                CaptureBaseline();
                ResetVisuals();
            }
        }

        private void CaptureBaseline()
        {
            baseVisualScale = visualRoot.localScale;
            baseVisualRotation = visualRoot.localRotation;
            if (progressFill != null)
            {
                fullFillScale = progressFill.localScale;
                fullFillPosition = progressFill.localPosition;
            }

            hasBaseline = true;
        }

        private void ApplyProgress(float normalizedProgress)
        {
            float pulse = 1f + Mathf.Sin(normalizedProgress * Mathf.PI * 4f) * 0.1f;
            visualRoot.localScale = baseVisualScale * pulse;
            visualRoot.localRotation = baseVisualRotation * Quaternion.Euler(0f, normalizedProgress * 45f, 0f);

            if (progressFill == null)
            {
                return;
            }

            float visibleProgress = Mathf.Max(0.001f, normalizedProgress);
            Vector3 scale = fullFillScale;
            scale.x = fullFillScale.x * visibleProgress;
            progressFill.localScale = scale;

            Vector3 position = fullFillPosition;
            position.x = fullFillPosition.x - (fullFillScale.x - scale.x) * 0.5f;
            progressFill.localPosition = position;
        }

        private void ResetVisuals()
        {
            if (visualRoot != null && hasBaseline)
            {
                visualRoot.localScale = baseVisualScale;
                visualRoot.localRotation = baseVisualRotation;
            }

            if (progressFill != null && hasBaseline)
            {
                progressFill.localScale = fullFillScale;
                progressFill.localPosition = fullFillPosition;
            }

            if (progressRoot != null)
            {
                progressRoot.SetActive(false);
            }

            elapsedSeconds = 0f;
            durationSeconds = 0f;
            Progress = 0f;
            IsPlaying = false;
            CurrentActionName = string.Empty;
        }
    }
}
