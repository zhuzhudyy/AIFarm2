using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Time;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private readonly Dictionary<ResidentId, ObservationService>
            backgroundObservationServices =
                new Dictionary<ResidentId, ObservationService>();

        [SerializeField]
        private DemoSceneConfig sceneConfig;

        public DemoSceneConfig SceneConfig => sceneConfig;

        public FarmField Field { get; private set; }

        public FarmInventory Inventory { get; private set; }

        public GameClock Clock { get; private set; }

        public DemoMode Mode { get; private set; }

        public FarmSimulation Simulation { get; private set; }

        public WorldEventLog Events { get; private set; }

        public ResidentRegistry ResidentRegistry { get; private set; }

        public AiRequestCoordinator AiRequests { get; private set; }

        public ResidentReflectionCoordinator ResidentReflections { get; private set; }

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
            ResidentRegistry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                ResidentRuntimeState runtimeState = definition.ResidentId == ResidentIds.Yaya
                    ? new NpcRuntimeState(definition)
                    : new ResidentRuntimeState(definition);
                ActionResult residentResult = ResidentRegistry.Register(definition, runtimeState);
                if (residentResult.Failed)
                {
                    return residentResult;
                }
            }

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
            StartBackgroundObservationProjection();
            IAiGatewayClient sharedGateway = sceneConfig.AiGatewayMode == AiGatewayMode.Local
                ? (IAiGatewayClient)new LocalAiGatewayClient()
                : new RemoteAiGatewayClient(
                    sceneConfig.AiGatewayBaseUrl,
                    sceneConfig.AiRequestTimeoutSeconds);
            AiRequests = new AiRequestCoordinator(
                sharedGateway,
                sceneConfig.MaximumConcurrentAiRequests,
                new LocalAiGatewayClient());
            ResidentReflections = new ResidentReflectionCoordinator(
                ResidentRegistry,
                Clock.ElapsedGameSeconds);
            Events.RecordPublicTownEvent(
                Clock.ElapsedGameSeconds,
                WorldEventKind.System,
                "离线演示已启动；本地规划与表达服务可用。");
            LastSimulationResult = null;
            IsInitialized = true;
            return ActionResult.Success("Demo domain state initialized.");
        }

        public ActionResult ResetToConfiguredDefaults()
        {
            if (!IsInitialized || sceneConfig == null || sceneConfig.InventoryConfig == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "GameBootstrap must be initialized before starting a new demo.");
            }

            foreach (FarmPlot plot in Field.Plots)
            {
                ActionResult plotResult = plot.RestoreState(
                    PlotState.Empty,
                    null,
                    0,
                    false,
                    false,
                    false,
                    0);
                if (plotResult.Failed)
                {
                    return plotResult;
                }
            }

            DemoInventoryConfig inventoryConfig = sceneConfig.InventoryConfig;
            ActionResult inventoryResult = Inventory.RestoreCounts(
                inventoryConfig.CarrotSeeds,
                inventoryConfig.Water,
                inventoryConfig.Fertilizer,
                inventoryConfig.Carrots);
            if (inventoryResult.Failed)
            {
                return inventoryResult;
            }

            ActionResult clockResult = Clock.Restore(
                sceneConfig.InitialElapsedGameSeconds,
                sceneConfig.TimeScale,
                false);
            if (clockResult.Failed)
            {
                return clockResult;
            }

            ActionResult simulationResult = Simulation.ResetRuntimeState();
            if (simulationResult.Failed)
            {
                return simulationResult;
            }

            ResidentReflections?.Synchronize(Clock.ElapsedGameSeconds);

            Events.Clear();
            Events.RecordPublicTownEvent(
                Clock.ElapsedGameSeconds,
                WorldEventKind.System,
                "已创建新 Demo；本地规划与表达服务可用。");
            LastSimulationResult = null;
            return ActionResult.Success("New demo state initialized.");
        }

        private void OnDestroy()
        {
            if (Events != null)
            {
                Events.EntryRecorded -= HandleBackgroundWorldEventRecorded;
            }

            AiRequests?.Shutdown();
        }

        private void StartBackgroundObservationProjection()
        {
            backgroundObservationServices.Clear();
            foreach (ResidentId residentId in ResidentRegistry.ResidentIds)
            {
                // The active farming resident is projected by ReplanController.
                if (residentId != ResidentIds.Yaya)
                {
                    backgroundObservationServices.Add(
                        residentId,
                        new ObservationService(residentId));
                }
            }

            Events.EntryRecorded += HandleBackgroundWorldEventRecorded;
        }

        private void HandleBackgroundWorldEventRecorded(WorldEventEntry worldEvent)
        {
            foreach (KeyValuePair<ResidentId, ObservationService> pair in
                backgroundObservationServices)
            {
                ActionResult runtimeResolved = ResidentRegistry.TryGetRuntimeState(
                    pair.Key,
                    out ResidentRuntimeState runtimeState);
                if (runtimeResolved.Failed || runtimeState == null)
                {
                    continue;
                }

                ActionResult observed = pair.Value.Observe(
                    worldEvent,
                    runtimeState.Memories,
                    out _);
                if (observed.Failed)
                {
                    Debug.LogWarning(
                        $"Background memory projection failed for {pair.Key}: " +
                        observed.Message,
                        this);
                }
            }
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
                return;
            }

            if (ResidentReflections != null)
            {
                ActionResult reflected = ResidentReflections.TickDayBoundaries(
                    Clock.ElapsedGameSeconds,
                    Events,
                    out _);
                if (reflected.Failed)
                {
                    Debug.LogWarning(
                        $"Daily resident reflection recovered safely: {reflected.Message}",
                        this);
                }
            }
        }
    }
}
