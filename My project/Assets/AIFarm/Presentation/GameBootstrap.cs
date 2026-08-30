using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Time;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField]
        private DemoSceneConfig sceneConfig;

        public DemoSceneConfig SceneConfig => sceneConfig;

        public FarmField Field { get; private set; }

        public FarmInventory Inventory { get; private set; }

        public GameClock Clock { get; private set; }

        public DemoMode Mode { get; private set; }

        public FarmSimulation Simulation { get; private set; }

        public WorldEventLog Events { get; private set; }

        public ActionResult? LastSimulationResult { get; private set; }

        public bool IsInitialized { get; private set; }

        public void Configure(DemoSceneConfig config)
        {
            sceneConfig = config;
        }

        public ActionResult Initialize()
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "GameBootstrap has already been initialized.");
            }

            if (sceneConfig == null || sceneConfig.InventoryConfig == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "GameBootstrap requires a complete demo scene configuration.");
            }

            DemoInventoryConfig inventoryConfig = sceneConfig.InventoryConfig;
            Field = new FarmField();
            Inventory = new FarmInventory(
                inventoryConfig.CarrotSeeds,
                inventoryConfig.Water,
                inventoryConfig.Fertilizer,
                inventoryConfig.Carrots);
            Clock = new GameClock(sceneConfig.InitialElapsedGameSeconds, sceneConfig.TimeScale);
            Mode = sceneConfig.CreateDemoMode();
            Events = new WorldEventLog();
            Simulation = new FarmSimulation(Field, Clock, Mode, Events);
            Events.Record(
                Clock.ElapsedGameSeconds,
                WorldEventKind.System,
                "离线演示已启动；本地规划与表达服务可用。");
            LastSimulationResult = null;
            IsInitialized = true;
            return ActionResult.Success("Demo domain state initialized.");
        }

        private void Awake()
        {
            ActionResult result = Initialize();
            if (result.Failed)
            {
                Debug.LogError(result.Message, this);
            }
        }

        private void Update()
        {
            if (!IsInitialized || Simulation == null ||
                (LastSimulationResult.HasValue && LastSimulationResult.Value.Failed))
            {
                return;
            }

            ActionResult result = Simulation.Advance(UnityEngine.Time.deltaTime);
            LastSimulationResult = result;
            if (result.Failed)
            {
                Debug.LogError($"Farm simulation stopped: {result.Message}", this);
            }
        }
    }
}
