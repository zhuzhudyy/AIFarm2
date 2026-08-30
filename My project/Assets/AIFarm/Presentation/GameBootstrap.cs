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
    }
}
