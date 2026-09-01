using AIFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ApiGatewaySetupPanel : MonoBehaviour
    {
        [SerializeField]
        private LocalAiGatewayProcess gatewayProcess;

        [SerializeField]
        private Button openButton;

        [SerializeField]
        private GameObject panelRoot;

        [SerializeField]
        private InputField apiKeyInput;

        [SerializeField]
        private InputField modelInput;

        [SerializeField]
        private Button startButton;

        [SerializeField]
        private Button stopButton;

        [SerializeField]
        private Button closeButton;

        [SerializeField]
        private Text statusText;

        private bool listenersAttached;

        public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

        public ActionResult Configure(
            LocalAiGatewayProcess process,
            Button openControl,
            GameObject root,
            InputField keyInput,
            InputField sharedModelInput,
            Button startControl,
            Button stopControl,
            Button closeControl,
            Text statusLabel)
        {
            if (process == null || openControl == null || root == null ||
                keyInput == null || sharedModelInput == null ||
                startControl == null || stopControl == null || closeControl == null ||
                statusLabel == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "API gateway setup requires a complete set of UI controls.");
            }

            bool reattachListeners = listenersAttached;
            if (reattachListeners)
            {
                DetachListeners();
            }

            gatewayProcess = process;
            openButton = openControl;
            panelRoot = root;
            apiKeyInput = keyInput;
            modelInput = sharedModelInput;
            startButton = startControl;
            stopButton = stopControl;
            closeButton = closeControl;
            statusText = statusLabel;

            apiKeyInput.contentType = InputField.ContentType.Password;
            apiKeyInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumApiKeyLength;
            modelInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumModelIdLength;
            if (string.IsNullOrWhiteSpace(modelInput.text))
            {
                modelInput.text = LocalAiGatewayProcess.DefaultModelId;
            }

            RefreshPresentation();
            if (reattachListeners)
            {
                AttachListeners();
            }

            return ActionResult.Success("In-game API gateway setup configured.");
        }

        public void Show()
        {
            if (panelRoot == null)
            {
                return;
            }

            panelRoot.SetActive(true);
            RefreshPresentation();
            apiKeyInput?.ActivateInputField();
        }

        public void Hide()
        {
            panelRoot?.SetActive(false);
        }

        public ActionResult StartConfiguredGateway()
        {
            if (gatewayProcess == null || apiKeyInput == null || modelInput == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The API gateway setup panel is not connected.");
            }

            string apiKey = apiKeyInput.text;
            string modelId = modelInput.text;
            ActionResult result = gatewayProcess.StartGateway(apiKey, modelId);

            // The secret is single-use UI input. Clear it after every submission,
            // including rejected submissions, so it never remains visible or serialized.
            apiKeyInput.text = string.Empty;
            RefreshPresentation();
            return result;
        }

        private void Start()
        {
            AttachListeners();
            RefreshPresentation();
        }

        private void AttachListeners()
        {
            if (listenersAttached)
            {
                return;
            }

            openButton?.onClick.AddListener(Show);
            startButton?.onClick.AddListener(HandleStart);
            stopButton?.onClick.AddListener(HandleStop);
            closeButton?.onClick.AddListener(Hide);
            if (gatewayProcess != null)
            {
                gatewayProcess.StatusChanged += HandleGatewayStatusChanged;
            }

            listenersAttached = true;
        }

        private void DetachListeners()
        {
            if (!listenersAttached)
            {
                return;
            }

            openButton?.onClick.RemoveListener(Show);
            startButton?.onClick.RemoveListener(HandleStart);
            stopButton?.onClick.RemoveListener(HandleStop);
            closeButton?.onClick.RemoveListener(Hide);
            if (gatewayProcess != null)
            {
                gatewayProcess.StatusChanged -= HandleGatewayStatusChanged;
            }

            listenersAttached = false;
        }

        private void HandleStart()
        {
            ActionResult result = StartConfiguredGateway();
            if (result.Failed && statusText != null &&
                (gatewayProcess == null || string.IsNullOrWhiteSpace(gatewayProcess.StatusMessage)))
            {
                statusText.text = result.Message;
            }
        }

        private void HandleStop()
        {
            gatewayProcess?.StopOwnedGateway();
            RefreshPresentation();
        }

        private void HandleGatewayStatusChanged(
            LocalAiGatewayState state,
            string message)
        {
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            bool busy = gatewayProcess != null && gatewayProcess.IsBusy;
            if (apiKeyInput != null)
            {
                apiKeyInput.interactable = !busy;
            }

            if (modelInput != null)
            {
                modelInput.interactable = !busy;
            }

            if (startButton != null)
            {
                startButton.interactable = !busy && gatewayProcess != null;
            }

            if (stopButton != null)
            {
                stopButton.interactable = gatewayProcess != null &&
                    gatewayProcess.OwnsProcess;
            }

            if (statusText != null)
            {
                statusText.text = gatewayProcess == null
                    ? "网关控制器未连接；居民使用本地 AI。"
                    : gatewayProcess.StatusMessage;
            }
        }

        private void OnDestroy()
        {
            DetachListeners();
        }
    }
}
