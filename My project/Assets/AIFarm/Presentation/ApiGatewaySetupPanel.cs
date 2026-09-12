using AIFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ApiGatewaySetupPanel : MonoBehaviour
    {
        [SerializeField] private LocalAiGatewayProcess gatewayProcess;
        [SerializeField] private Button openButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private InputField apiKeyInput;
        [SerializeField] private InputField modelInput;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        private Button clearButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text statusText;
        [SerializeField] private InputField upstreamInput;
        [SerializeField] private Text protocolText;
        [SerializeField] private Text rememberText;
        [SerializeField] private Text gatewayText;
        private GatewayConnectionController connection;
        private int protocolIndex;
        private bool remember = true;
        private bool loaded;
        private readonly string[] protocols = { "chat_completions", "responses", "anthropic", "gemini" };
        private readonly string[] protocolNames = { "OpenAI Chat Completions", "OpenAI Responses", "Anthropic Messages", "Gemini 原生" };
        public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

        public ActionResult Configure(LocalAiGatewayProcess process, Button openControl, GameObject root,
            InputField keyInput, InputField sharedModelInput, Button startControl, Button stopControl,
            Button closeControl, Text statusLabel)
        {
            if (process == null || openControl == null || root == null || keyInput == null || sharedModelInput == null || startControl == null || stopControl == null || closeControl == null || statusLabel == null)
                return ActionResult.Failure(ActionFailureReason.InvalidArgument, "模型设置控件不完整。");
            gatewayProcess = process;
            openButton = openControl;
            panelRoot = root;
            apiKeyInput = keyInput;
            modelInput = sharedModelInput;
            startButton = startControl;
            stopButton = stopControl;
            closeButton = closeControl;
            statusText = statusLabel;
            return ActionResult.Success("模型设置已连接。");
        }

        public void BuildControls()
        {
            if (upstreamInput != null)
                return;
            if (panelRoot != null)
                panelRoot.SetActive(false);
            var root = TownUi.Panel("UnifiedModelSettings", transform, 460, 125, 1000, 800);
            panelRoot = root.gameObject;
            Canvas modalCanvas = root.gameObject.AddComponent<Canvas>();
            modalCanvas.overrideSorting = true;
            modalCanvas.sortingOrder = 60;
            root.gameObject.AddComponent<GraphicRaycaster>();
            TownUi.Label("Title", root.transform, "全镇共享模型设置", 28, 18, 800, 48, 32);
            closeButton = TownUi.Button("Close", root.transform, "关闭", 866, 18, 106, 44, Hide);
            gatewayText = TownUi.Label("LocalGateway", root.transform, "Unity → 本机 Python 网关", 28, 76, 944, 50, 22);
            TownUi.Label("UpstreamLabel", root.transform, "Python → 上游模型 Base URL / 完整 Endpoint", 28, 135, 930, 36);
            upstreamInput = TownUi.Input("UpstreamUrl", root.transform, "https://your-server.example/v1 或本地模型地址", 28, 177, 944, 56);
            TownUi.Label("ModelLabel", root.transform, "模型名称 Model（自由填写）", 28, 246, 900, 36);
            modelInput = TownUi.Input("Model", root.transform, "服务提供的模型名称或别名", 28, 287, 944, 56);
            modelInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumModelIdLength;
            TownUi.Label("KeyLabel", root.transform, "API Key（无鉴权服务可留空）", 28, 357, 720, 36);
            apiKeyInput = TownUi.Input("ApiKey", root.transform, "已保存的 Key 不回显；清除后可使用无鉴权服务", 28, 399, 698, 56);
            apiKeyInput.contentType = InputField.ContentType.Password;
            apiKeyInput.characterLimit = LocalAiGatewayLaunchSpec.MaximumApiKeyLength;
            TownUi.Button("Paste", root.transform, "粘贴", 742, 400, 106, 54, () => apiKeyInput.text = GUIUtility.systemCopyBuffer);
            TownUi.Button("Reveal", root.transform, "显/隐", 862, 400, 110, 54, () =>
            {
                apiKeyInput.contentType = apiKeyInput.contentType == InputField.ContentType.Password ? InputField.ContentType.Standard : InputField.ContentType.Password;
                apiKeyInput.ForceLabelUpdate();
            });
            var protocolButton = TownUi.Button("Protocol", root.transform, protocolNames[0], 28, 479, 555, 50, () =>
            {
                protocolIndex = (protocolIndex + 1) % protocols.Length;
                protocolText.text = "高级协议：" + protocolNames[protocolIndex];
            });
            protocolText = protocolButton.GetComponentInChildren<Text>();
            protocolText.text = "高级协议：" + protocolNames[0];
            var rememberButton = TownUi.Button("Remember", root.transform, "记住本机配置：是", 600, 479, 372, 50, () =>
            {
                remember = !remember;
                rememberText.text = "记住本机配置：" + (remember ? "是" : "否");
            });
            rememberText = rememberButton.GetComponentInChildren<Text>();
            startButton = TownUi.Button("Apply", root.transform, "测试并应用", 28, 550, 260, 56, () => StartConfiguredGateway());
            stopButton = TownUi.Button("Reconnect", root.transform, "重新连接", 310, 550, 244, 56, () => connection?.Reconnect());
            clearButton = TownUi.Button("Clear", root.transform, "清除配置", 576, 550, 224, 56, () => { connection?.Clear(); apiKeyInput.text = ""; loaded = false; });
            statusText = TownUi.Label("Status", root.transform, "等待本机网关…", 28, 628, 944, 154, 22);
            panelRoot.SetActive(false);
        }

        private void Start()
        {
            BuildControls();
            connection = FindFirstObjectByType<GatewayConnectionController>();
            if (connection == null)
            {
                GameObject host = gatewayProcess == null ? gameObject : gatewayProcess.gameObject;
                connection = host.AddComponent<GatewayConnectionController>();
            }
            openButton?.onClick.AddListener(Show);
            connection.StatusChanged += Refresh;
            Refresh();
        }

        public void Show()
        {
            BuildControls();
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
            Refresh();
        }
        public void Hide()
        {
            panelRoot?.SetActive(false);
        }
        public ActionResult StartConfiguredGateway()
        {
            loaded = true;
            if (connection == null)
                return ActionResult.Failure(ActionFailureReason.ServiceUnavailable, "本机网关尚未初始化。");
            if (string.IsNullOrWhiteSpace(upstreamInput.text) || string.IsNullOrWhiteSpace(modelInput.text))
            {
                statusText.text = "请填写上游地址和模型名称。";
                return ActionResult.Failure(ActionFailureReason.InvalidArgument, statusText.text);
            }
            connection.Apply(new GatewayConnectionController.Configuration
            {
                provider = "openai",
                protocol = protocols[protocolIndex],
                base_url = upstreamInput.text.Trim(),
                model = modelInput.text.Trim(),
                api_key = string.IsNullOrEmpty(apiKeyInput.text) && connection.KeyConfigured ? null : apiKeyInput.text,
                persist = remember
            });
            return ActionResult.Success("正在执行文本、居民决策、居民对话三项真实推理测试。");
        }
        private void Refresh()
        {
            if (connection == null)
                return;
            if (!loaded && connection.GatewayReachable)
            {
                var settings = connection.Editable;
                upstreamInput.text = settings.base_url ?? "";
                modelInput.text = settings.model ?? "";
                remember = settings.persist;
                rememberText.text = "记住本机配置：" + (remember ? "是" : "否");
                protocolIndex = System.Array.IndexOf(protocols, settings.protocol);
                if (protocolIndex < 0)
                    protocolIndex = 0;
                protocolText.text = "高级协议：" + protocolNames[protocolIndex];
                loaded = true;
            }
            gatewayText.text = "Unity → 本机 Python 网关：" + connection.GatewayUrl;
            startButton.interactable = !connection.Busy;
            stopButton.interactable = !connection.Busy;
            if (clearButton != null) clearButton.interactable = !connection.Busy;
            statusText.text = "网关：" + (connection.GatewayReachable ? "已运行" : "未连接") + "  上游：" + connection.StateLabel + "  配置 v" + connection.ConfigVersion +
                "\n实际请求地址：" + connection.Endpoint + "\n" + (string.IsNullOrEmpty(connection.LastError) ? "配置保存在本机用户目录；三项真实推理全部成功后才显示在线。" : connection.LastError);
        }
        private void OnDestroy()
        {
            if (connection != null)
                connection.StatusChanged -= Refresh;
        }
    }
}
