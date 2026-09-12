using System;
using System.Collections.Generic;
using System.Text;
using AIFarm.Npc;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class TownDialogueOverlay : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string speaker, name, recipient, session, text, source;
            public double gameSeconds;
            public double displayAt;
        }
        private sealed class Bubble
        {
            public RectTransform rect;
            public Text label;
            public ResidentBlockoutView view;
            public double until;
            public readonly Queue<Entry> queue = new Queue<Entry>();
        }
        private readonly List<Entry> history = new List<Entry>();
        private readonly Dictionary<string, Bubble> bubbles = new Dictionary<string, Bubble>();
        private readonly Dictionary<string, ResidentBlockoutView> views = new Dictionary<string, ResidentBlockoutView>();
        private readonly Dictionary<string, double> nextSessionDisplay = new Dictionary<string, double>();
        private readonly Dictionary<string, double> nextResidentDisplay = new Dictionary<string, double>();
        private readonly Dictionary<string, double> activeSessionUntil = new Dictionary<string, double>();
        private static TownDialogueOverlay instance;
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameBootstrap bootstrap;
        private DemoHud hud;
        private GameObject historyPanel;
        private Text historyLabel;
        private Text filterLabel;
        private Text selectedLabel;
        private Text diagnostics;
        private GameObject diagnosticsPanel;
        private GameObject farmPanel;
        private Text farmLabel;
        private ScrollRect historyScroll;
        private bool filterSelected;
        private string lastFilter = "";
        public IReadOnlyList<Entry> History => history;
        public static float ReadingSeconds(string text) => Mathf.Clamp(4f + (text?.Length ?? 0) * .10f, 4f, 12f);

        public static void Publish(ResidentId speaker, string text, string sessionId = "", ResidentId recipient = default, string source = "local")
        {
            if (!Application.isPlaying || !speaker.IsValid || string.IsNullOrWhiteSpace(text))
                return;
            if (instance == null)
            {
                instance = FindFirstObjectByType<TownDialogueOverlay>();
                if (instance == null)
                {
                    var go = new GameObject("TownDialogueOverlay");
                    instance = go.AddComponent<TownDialogueOverlay>();
                }
            }
            instance.Add(speaker, text, sessionId, recipient, source);
        }

        private void Awake()
        {
            instance = this;
            bootstrap = FindFirstObjectByType<GameBootstrap>();
            hud = FindFirstObjectByType<DemoHud>();
            foreach (var view in FindObjectsByType<ResidentBlockoutView>(FindObjectsSortMode.None))
                if (view.ResidentId.IsValid)
                    views[view.ResidentId.Value] = view;
            var root = new GameObject("TownScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            canvasRect = root.GetComponent<RectTransform>();
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            foreach (var existingScaler in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
                if (existingScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    existingScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            foreach (var label in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                label.font = TownUi.Font;
                label.resizeTextForBestFit = false;
            }
            Transform detailToggle = GameObject.Find("UI_Canvas")?.transform.Find("Meadow_Details_Button");
            if (detailToggle != null)
                ((RectTransform)detailToggle).anchoredPosition = new Vector2(-20, -142);
            Transform cameraHint = GameObject.Find("UI_Canvas")?.transform.Find("Meadow_Camera_Hint");
            if (cameraHint != null)
                cameraHint.gameObject.SetActive(false);
            var targetPanel = TownUi.Panel("CommandTargetPanel", root.transform, 400, 874, 1120, 88);
            targetPanel.raycastTarget = false;
            selectedLabel = TownUi.Label("CommandTarget", targetPanel.transform, "", 18, 4, 1084, 38, 24);
            int residentIndex = 0;
            foreach (var view in views.Values)
            {
                ResidentId selectedId = view.ResidentId;
                TownUi.Button("Select_" + selectedId.Value, targetPanel.transform, view.DefinitionAsset.DisplayName, 18 + residentIndex * 275, 47, 260, 35, () => hud?.SelectResident(selectedId));
                residentIndex++;
            }
            TownUi.Button("HistoryToggle", root.transform, "对话历史", 1510, 22, 175, 48, () => { historyPanel.SetActive(!historyPanel.activeSelf); RefreshHistory(); });
            TownUi.Button("DiagnosticsToggle", root.transform, "居民诊断", 1698, 22, 195, 48, () => diagnosticsPanel.SetActive(!diagnosticsPanel.activeSelf));
            TownUi.Button("StopTask", root.transform, "停止任务 / 自主", 1510, 82, 383, 46, () => { if (hud != null) hud.SubmitCommand("停止"); });
            historyPanel = TownUi.Panel("DialogueHistory", root.transform, 965, 160, 920, 680).gameObject;
            TownUi.Label("Title", historyPanel.transform, "完整对话记录", 22, 16, 650, 46, 28);
            TownUi.Button("CloseHistory", historyPanel.transform, "关闭", 785, 16, 110, 44, () => historyPanel.SetActive(false));
            var filter = TownUi.Button("ResidentFilter", historyPanel.transform, "全部居民", 22, 75, 870, 44, () => { filterSelected = !filterSelected; RefreshHistory(); });
            filterLabel = filter.GetComponentInChildren<Text>();
            var viewport = TownUi.Panel("Viewport", historyPanel.transform, 22, 135, 870, 520);
            viewport.color = new Color(.075f, .14f, .11f, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            historyScroll = viewport.gameObject.AddComponent<ScrollRect>();
            historyScroll.horizontal = false;
            historyScroll.viewport = viewport.rectTransform;
            historyLabel = TownUi.Label("Transcript", viewport.transform, "", 16, 8, 832, 505, 24);
            historyLabel.verticalOverflow = VerticalWrapMode.Overflow;
            historyScroll.content = historyLabel.rectTransform;
            historyScroll.movementType = ScrollRect.MovementType.Clamped;
            historyPanel.SetActive(false);
            diagnosticsPanel = TownUi.Panel("ResidentDiagnostics", root.transform, 1120, 160, 765, 680).gameObject;
            TownUi.Label("Title", diagnosticsPanel.transform, "居民实时诊断", 22, 16, 590, 45, 28);
            TownUi.Button("CloseDiagnostic", diagnosticsPanel.transform, "关闭", 635, 16, 105, 44, () => diagnosticsPanel.SetActive(false));
            diagnostics = TownUi.Label("Diagnostics", diagnosticsPanel.transform, "", 22, 80, 715, 575, 23);
            diagnosticsPanel.SetActive(false);
            TownUi.Button("FarmToggle", root.transform, "农田 / 公共仓库", 1095, 22, 400, 48, () => farmPanel.SetActive(!farmPanel.activeSelf));
            farmPanel = TownUi.Panel("FarmOverview", root.transform, 740, 160, 1145, 680).gameObject;
            TownUi.Label("Title", farmPanel.transform, "农田生长与公共仓库", 24, 18, 925, 46, 28);
            TownUi.Button("CloseFarm", farmPanel.transform, "关闭", 1015, 18, 105, 44, () => farmPanel.SetActive(false));
            farmLabel = TownUi.Label("FarmState", farmPanel.transform, "", 24, 86, 1080, 565, 24);
            farmPanel.SetActive(false);
        }

        private void Add(ResidentId speaker, string text, string sessionId, ResidentId recipient, string source)
        {
            if (!views.TryGetValue(speaker.Value, out var view))
                return;
            var entry = new Entry
            {
                speaker = speaker.Value,
                name = view.DefinitionAsset.DisplayName,
                recipient = recipient.Value,
                session = sessionId ?? "",
                text = text,
                source = source ?? "local",
                gameSeconds = bootstrap?.Clock?.ElapsedGameSeconds ?? 0
            };
            double start = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (nextResidentDisplay.TryGetValue(entry.speaker, out double residentFree))
                start = Math.Max(start, residentFree);
            if (entry.session.Length > 0 && nextSessionDisplay.TryGetValue(entry.session, out double sessionFree))
                start = Math.Max(start, sessionFree);
            entry.displayAt = start;
            double ends = start + ReadingSeconds(text);
            nextResidentDisplay[entry.speaker] = ends;
            if (entry.session.Length > 0)
                nextSessionDisplay[entry.session] = ends;
            history.Add(entry);
            if (!bubbles.TryGetValue(speaker.Value, out var bubble))
            {
                var panel = TownUi.Panel("Bubble_" + speaker.Value, canvas.transform, 0, 0, 510, 130);
                panel.raycastTarget = false;
                bubble = new Bubble { rect = panel.rectTransform, view = view, label = TownUi.Label("Message", panel.transform, "", 18, 14, 474, 102, 24) };
                panel.gameObject.SetActive(false);
                bubbles.Add(speaker.Value, bubble);
            }
            bubble.queue.Enqueue(entry);
            RefreshHistory();
        }

        private void LateUpdate()
        {
            if (hud == null)
                hud = FindFirstObjectByType<DemoHud>();
            if (bootstrap == null)
                bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit clicked, 500))
                {
                    var resident = clicked.collider.GetComponentInParent<ResidentBlockoutView>();
                    if (resident != null)
                        hud?.SelectResident(resident.ResidentId);
                    if (clicked.collider.GetComponentInParent<PlotBlockoutView>() != null)
                        farmPanel.SetActive(true);
                }
            }
            if (farmPanel.activeSelf && bootstrap != null && bootstrap.IsInitialized)
            {
                var inventory = bootstrap.Inventory;
                var builder = new StringBuilder("收获返还种子；水井补水并将杂草堆肥制成肥料。\n公共仓库：");
                foreach (AIFarm.Inventory.InventoryItem item in Enum.GetValues(typeof(AIFarm.Inventory.InventoryItem)))
                    builder.Append(item).Append(' ').Append(inventory.GetCount(item)).Append("  ");
                builder.Append("\n\n");
                for (int number = 1; number <= 9; number++)
                {
                    var plot = bootstrap.Field.GetPlot(number);
                    builder.Append(number.ToString("00")).Append("号地  ").Append(plot.State == AIFarm.Farming.PlotState.Empty ? "空地" : plot.State == AIFarm.Farming.PlotState.Mature ? "成熟可收获" : "生长阶段 " + plot.GrowthStage + " / 4")
                        .Append("  ").Append(plot.GrowthProgress).Append("%  水分 ").Append(plot.WaterLevel)
                        .Append("  ").Append(bootstrap.Simulation.GetGrowthBlockReason(number))
                        .Append("  预计剩余 ").Append((bootstrap.Simulation.GetRemainingGrowthGameSeconds(number) / 3600).ToString("0.0")).Append(" 游戏小时\n");
                }
                farmLabel.text = builder.ToString();
            }
            if (hud != null && views.TryGetValue(hud.SelectedResidentId.Value, out var selected))
            {
                selectedLabel.text = "当前指令对象：" + selected.DefinitionAsset.DisplayName + "   ·   点击居民或列表切换";
                if (bootstrap != null && bootstrap.LifeControllers.TryGetValue(hud.SelectedResidentId, out var life))
                    selectedLabel.text += "   |   " + life.CurrentAction + (life.ActivityProgress > 0 ? "  " + (life.ActivityProgress * 100).ToString("0") + "%" : "");
                if (historyPanel.activeSelf && filterSelected && lastFilter != hud.SelectedResidentId.Value)
                    RefreshHistory();
                if (diagnosticsPanel.activeSelf)
                    diagnostics.text = bootstrap?.GetResidentDiagnostics(hud.SelectedResidentId) ?? "初始化中";
            }
            Camera camera = Camera.main;
            if (camera == null)
                return;
            // Reserve the actual command target/list rectangle as well as the
            // HUD. Its top is fixed in reference-canvas coordinates even when
            // a taller window expands the canvas height.
            var occupied = new List<Rect>
            {
                new Rect(0, 0, 455, 370),
                new Rect(390, 864, 1140, 108)
            };
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            // Expire both participants before displaying the next turn, independent
            // of dictionary iteration order and the frame in which it was queued.
            foreach (var expired in bubbles.Values)
                if (now >= expired.until)
                    expired.rect.gameObject.SetActive(false);
            foreach (var pair in bubbles)
            {
                var bubble = pair.Value;
                if (now >= bubble.until)
                {
                    if (bubble.queue.Count == 0 || now < bubble.queue.Peek().displayAt)
                    {
                        bubble.rect.gameObject.SetActive(false);
                        continue;
                    }
                    string session = bubble.queue.Peek().session;
                    if (session.Length > 0 && activeSessionUntil.TryGetValue(session, out double actualEnd) && now < actualEnd)
                        continue;
                    var entry = bubble.queue.Dequeue();
                    bubble.label.text = entry.name + "  ·  " + (entry.source == "openai" ? "模型" : "本地") + "\n" + entry.text;
                    float height = Mathf.Max(bubble.label.preferredHeight + 28, 100);
                    bubble.rect.sizeDelta = new Vector2(510, height);
                    bubble.label.rectTransform.sizeDelta = new Vector2(474, height - 28);
                    bubble.until = now + ReadingSeconds(entry.text);
                    if (session.Length > 0)
                        activeSessionUntil[session] = bubble.until;
                }
                Vector3 world = bubble.view.transform.position + Vector3.up * 2.3f;
                Vector3 screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0)
                {
                    bubble.rect.gameObject.SetActive(false);
                    continue;
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 point);
                float width = canvasRect.rect.width, heightCanvas = canvasRect.rect.height;
                float x = point.x + width * .5f - 255;
                float y = heightCanvas * .5f - point.y - bubble.rect.sizeDelta.y - 18;
                x = Mathf.Clamp(x, 10, Mathf.Max(10, width - 520));
                y = Mathf.Clamp(y, 220, Mathf.Max(220, heightCanvas - bubble.rect.sizeDelta.y - 185));
                var rect = new Rect(x, y, 510, bubble.rect.sizeDelta.y);
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    bool collision = false;
                    foreach (var used in occupied)
                    if (rect.Overlaps(used))
                    {
                        collision = true;
                        break;
                    }
                    if (!collision)
                        break;
                    rect.y += bubble.rect.sizeDelta.y + 14;
                    if (rect.yMax > heightCanvas - 175)
                    {
                        rect.y = 220;
                        rect.x = Mathf.Clamp(rect.x + 525, 10, width - 520);
                    }
                }
                bool hiddenForSpace = rect.yMax > heightCanvas - 175;
                foreach (var used in occupied)
                if (rect.Overlaps(used))
                {
                    hiddenForSpace = true;
                    break;
                }
                if (hiddenForSpace)
                {
                    bubble.rect.gameObject.SetActive(false);
                    continue;
                }
                occupied.Add(rect);
                bubble.rect.anchoredPosition = new Vector2(rect.x, -rect.y);
                bubble.rect.gameObject.SetActive(true);
                // Occlusion changes background emphasis; the transcript remains readable.
                bool obscured = Physics.Linecast(camera.transform.position, world, out RaycastHit hit) && hit.transform.GetComponentInParent<ResidentBlockoutView>() != bubble.view;
                bubble.rect.GetComponent<Image>().color = obscured ? new Color(.10f, .16f, .15f, .99f) : new Color(.045f, .095f, .075f, .96f);
            }
            // Reading panels are interactive foregrounds, never covered by a
            // newly created noninteractive speech bubble.
            if (historyPanel.activeSelf)
                historyPanel.transform.SetAsLastSibling();
            if (diagnosticsPanel.activeSelf)
                diagnosticsPanel.transform.SetAsLastSibling();
            if (farmPanel.activeSelf)
                farmPanel.transform.SetAsLastSibling();
        }

        private void RefreshHistory()
        {
            if (historyLabel == null)
                return;
            lastFilter = hud == null ? "" : hud.SelectedResidentId.Value;
            filterLabel.text = filterSelected ? "仅当前居民（点击显示全部）" : "全部居民（点击按当前居民筛选）";
            var builder = new StringBuilder();
            foreach (var entry in history)
            {
                if (filterSelected && entry.speaker != lastFilter && entry.recipient != lastFilter)
                    continue;
                int minutes = (int)(entry.gameSeconds / 60);
                builder.Append("第").Append(minutes / 1440 + 1).Append("天 ").Append((minutes % 1440 / 60).ToString("00")).Append(':').Append((minutes % 60).ToString("00"))
                    .Append("  ").Append(entry.name).Append(" → ").Append(string.IsNullOrEmpty(entry.recipient) ? "附近 / 玩家" : DisplayName(entry.recipient))
                    .Append("  [").Append(entry.source).Append("]\n").Append(entry.text);
                if (!string.IsNullOrEmpty(entry.session))
                    builder.Append("\n会话 ").Append(entry.session);
                builder.Append("\n\n");
            }
            historyLabel.text = builder.Length == 0 ? "还没有对话。居民交流和任务反馈会按顺序记录在这里。" : builder.ToString();
            historyLabel.rectTransform.sizeDelta = new Vector2(832, Mathf.Max(505, historyLabel.preferredHeight + 16));
            Canvas.ForceUpdateCanvases();
            historyScroll.verticalNormalizedPosition = 0;
        }
        private string DisplayName(string id) => views.TryGetValue(id, out var view) ? view.DefinitionAsset.DisplayName : id;
        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
