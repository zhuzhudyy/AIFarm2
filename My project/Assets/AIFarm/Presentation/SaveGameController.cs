using AIFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class SaveGameController : MonoBehaviour
    {
        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private NpcPlanExecutor executor;

        [SerializeField]
        private ReplanController replanner;

        [SerializeField]
        private Transform npcTransform;

        [SerializeField]
        private Button saveButton;

        [SerializeField]
        private Button loadButton;

        [SerializeField]
        private Button newDemoButton;

        [SerializeField]
        private Text statusText;

        private SaveGameService service;

        public ActionResult? LastOperationResult { get; private set; }

        public string SavePath => service?.SavePath ?? string.Empty;

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            NpcPlanExecutor planExecutor,
            ReplanController replanController,
            Transform npc,
            Button save,
            Button load,
            Button newDemo,
            Text status)
        {
            if (gameBootstrap == null || planExecutor == null || replanController == null ||
                npc == null || save == null || load == null || newDemo == null || status == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "SaveGameController requires game state, NPC, buttons, and status text.");
            }

            bootstrap = gameBootstrap;
            executor = planExecutor;
            replanner = replanController;
            npcTransform = npc;
            saveButton = save;
            loadButton = load;
            newDemoButton = newDemo;
            statusText = status;
            return ActionResult.Success("Save UI configured.");
        }

        public ActionResult Initialize(ISaveGameStorage storage = null)
        {
            if (service != null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "SaveGameController has already been initialized.");
            }

            if (bootstrap == null || executor == null || replanner == null || npcTransform == null ||
                !bootstrap.IsInitialized || !executor.IsInitialized || !replanner.IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "SaveGameController requires initialized game, executor, and replanner components.");
            }

            service = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                npcTransform,
                storage ?? new PersistentSaveGameStorage());
            saveButton?.onClick.AddListener(Save);
            loadButton?.onClick.AddListener(Load);
            newDemoButton?.onClick.AddListener(NewDemo);
            SetStatus($"存档：{service.SavePath}", false);
            return ActionResult.Success("SaveGameController initialized.");
        }

        public void Save()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            LastOperationResult = service.Save();
            SetStatus(
                LastOperationResult.Value.Succeeded
                    ? "保存成功。"
                    : $"保存失败：{LastOperationResult.Value.Message}",
                LastOperationResult.Value.Failed);
        }

        public void Load()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            ActionResult loaded = service.Load();
            if (loaded.Succeeded)
            {
                LastOperationResult = loaded;
                SetStatus(
                    service.LastLoadUsedSafeReplan
                        ? "加载成功；未完成任务已安全重新规划。"
                        : "加载成功。",
                    false);
                return;
            }

            string loadFailure = loaded.Message;
            ActionResult fallback = service.NewDemo(deleteSave: true);
            LastOperationResult = fallback.Failed ? fallback : loaded;
            if (fallback.Failed)
            {
                SetStatus(
                    $"存档损坏/不可读：{loadFailure}；新 Demo 恢复也失败：{fallback.Message}",
                    true);
                return;
            }

            SetStatus(
                $"存档损坏或不可读：{loadFailure}；已回到新 Demo。",
                true);
        }

        public void NewDemo()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            LastOperationResult = service.NewDemo(deleteSave: true);
            SetStatus(
                LastOperationResult.Value.Succeeded
                    ? "已创建新 Demo。"
                    : $"新 Demo 创建失败：{LastOperationResult.Value.Message}",
                LastOperationResult.Value.Failed);
        }

        private void Start()
        {
            if (service != null)
            {
                return;
            }

            ActionResult initialized = Initialize();
            if (initialized.Failed)
            {
                LastOperationResult = initialized;
                SetStatus($"存档服务不可用：{initialized.Message}", true);
            }
        }

        private void OnDestroy()
        {
            saveButton?.onClick.RemoveListener(Save);
            loadButton?.onClick.RemoveListener(Load);
            newDemoButton?.onClick.RemoveListener(NewDemo);
        }

        private bool EnsureInitialized()
        {
            if (service != null)
            {
                return true;
            }

            ActionResult initialized = Initialize();
            if (initialized.Succeeded)
            {
                return true;
            }

            LastOperationResult = initialized;
            SetStatus($"存档服务不可用：{initialized.Message}", true);
            return false;
        }

        private void SetStatus(string message, bool isError)
        {
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
                statusText.color = isError
                    ? new Color(1f, 0.55f, 0.45f, 1f)
                    : new Color(0.82f, 0.94f, 0.82f, 1f);
            }

            if (isError)
            {
                Debug.LogWarning(message, this);
            }
        }
    }
}
