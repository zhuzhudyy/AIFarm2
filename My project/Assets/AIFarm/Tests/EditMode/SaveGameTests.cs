using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using AIFarm.Ai;
using AIFarm.Activities;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Social;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.EditMode
{
    public sealed class SaveGameTests
    {
        private GameObject root;
        private GameObject npcObject;
        private DemoInventoryConfig inventoryConfig;
        private DemoSceneConfig sceneConfig;
        private GameBootstrap bootstrap;
        private NpcPlanExecutor executor;
        private ReplanController replanner;
        private FakeNavigation navigation;
        private InMemorySaveStorage storage;
        private SaveGameService service;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SaveGameTestRoot");
            inventoryConfig = ScriptableObject.CreateInstance<DemoInventoryConfig>();
            inventoryConfig.Configure(20, 20, 20, 2);
            sceneConfig = ScriptableObject.CreateInstance<DemoSceneConfig>();
            sceneConfig.Configure(
                inventoryConfig,
                day: 2,
                hour: 6,
                scale: 5f,
                size: 2f,
                spacing: 0.25f,
                gatewayMode: AiGatewayMode.Local);

            GameObject bootstrapObject = new GameObject("Bootstrap");
            bootstrapObject.transform.SetParent(root.transform, false);
            bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
            bootstrap.Configure(sceneConfig);
            Assert.That(bootstrap.Initialize().Succeeded, Is.True);

            npcObject = new GameObject("Npc");
            npcObject.transform.SetParent(root.transform, false);
            npcObject.transform.position = new Vector3(-2f, 0f, -1f);
            navigation = new FakeNavigation();
            executor = npcObject.AddComponent<NpcPlanExecutor>();
            var context = new NpcActionContext(
                bootstrap.Field,
                bootstrap.Inventory,
                bootstrap.Clock,
                bootstrap.Simulation,
                bootstrap.Events);
            Assert.That(
                executor.Initialize(context, navigation, new ImmediateFeedback()).Succeeded,
                Is.True);

            replanner = bootstrapObject.AddComponent<ReplanController>();
            Assert.That(
                replanner.Initialize(
                    context,
                    executor,
                    bootstrap.Mode,
                    new LocalAiGatewayClient(),
                    bootstrap.ResidentRegistry).Succeeded,
                Is.True);

            storage = new InMemorySaveStorage();
            service = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                npcObject.transform,
                storage);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            if (sceneConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(sceneConfig);
            }

            if (inventoryConfig != null)
            {
                UnityEngine.Object.DestroyImmediate(inventoryConfig);
            }
        }

        [Test]
        public void SaveLoad_PreservesFishFruitCompostAndExhaustedTree_WithoutDuplicatingHarvest()
        {
            TownActivityResources activities = bootstrap.Activities;
            string tree = TownActivityResources.OrchardIds[0];
            for (int index = 0; index < activities.Rules.fruitsPerTree; index++)
            {
                Assert.That(activities.TryReserve(tree, ResidentIds.Yaya).Succeeded, Is.True);
                Assert.That(bootstrap.Simulation.Advance(activities.Rules.pickingGameSeconds /
                    (bootstrap.Clock.GameSecondsPerRealSecond * bootstrap.Clock.TimeScale)).Succeeded, Is.True);
                Assert.That(activities.TryPickFruit(tree, ResidentIds.Yaya).Succeeded, Is.True);
            }
            Assert.That(activities.TryReserve(TownActivityResources.FishingOneId, ResidentIds.Amu).Succeeded, Is.True);
            Assert.That(bootstrap.Simulation.Advance(activities.Rules.fishingGameSeconds /
                (bootstrap.Clock.GameSecondsPerRealSecond * bootstrap.Clock.TimeScale)).Succeeded, Is.True);
            Assert.That(activities.TryFish(TownActivityResources.FishingOneId, ResidentIds.Amu).Succeeded, Is.True);
            FarmPlot plot = bootstrap.Field.GetPlot(1);
            Assert.That(plot.Sow(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.Water(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.Fertilize(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.IntroduceWeeds().Succeeded, Is.True);
            Assert.That(plot.Weed(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.AdvanceGrowth(23).Succeeded, Is.True);
            double remaining = activities.GetRegrowthRemaining(tree);
            Assert.That(service.Save().Succeeded, Is.True);
            Assert.That(bootstrap.Inventory.RestoreCounts(0, 0, 0, 0).Succeeded, Is.True);
            activities.Reset();
            Assert.That(service.Load().Succeeded, Is.True);
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fish), Is.EqualTo(1));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fruit), Is.EqualTo(3));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Compost), Is.EqualTo(1));
            Assert.That(activities.GetFruitRemaining(tree), Is.Zero);
            Assert.That(activities.GetRegrowthRemaining(tree), Is.EqualTo(remaining));
            Assert.That(plot.GrowthProgress, Is.EqualTo(23));
            Assert.That(plot.Harvest(bootstrap.Inventory).Failed, Is.True);
            Assert.That(activities.TryFish(TownActivityResources.FishingOneId, ResidentIds.Amu).Failed, Is.True);
        }

        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(4)]
        public void Load_InvalidQuantityActivityProgressIsRejectedBeforeLiveMutation(int completed)
        {
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            ResidentTaskSpec task = ResidentTaskSpec.Create(ResidentIds.Yaya, "Fish");
            task.quantity = 3;
            data.lifeTasks = new[] { new ResidentTaskSnapshot { residentId = ResidentIds.Yaya.Value,
                hasTask = true, task = task, completedActivityCount = completed } };
            storage.Json = JsonUtility.ToJson(data, true);
            Assert.That(bootstrap.Field.GetPlot(1).Sow(bootstrap.Inventory).Succeeded, Is.True);
            long revisionBeforeLoad = bootstrap.AuthoritativeStateRevision;
            int seedsBeforeLoad = bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed);

            ActionResult result = service.Load();

            Assert.That(result.Failed, Is.True);
            Assert.That(result.Message, Does.Contain("activity progress"));
            Assert.That(bootstrap.AuthoritativeStateRevision, Is.EqualTo(revisionBeforeLoad));
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(seedsBeforeLoad));
        }

        [Test]
        public void SaveLoad_RoundTrip_RestoresAllStateAndRestartsAtomicAction()
        {
            FarmPlot plot = bootstrap.Field.GetPlot(1);
            Assert.That(plot.Sow(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.Water(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.Fertilize(bootstrap.Inventory).Succeeded, Is.True);
            Assert.That(plot.IntroduceWeeds().Succeeded, Is.True);
            Assert.That(plot.Weed().Succeeded, Is.True);
            Assert.That(plot.AdvanceGrowth(37).Succeeded, Is.True);
            Assert.That(
                bootstrap.Clock.Restore(12345d, 7d, true).Succeeded,
                Is.True);

            double[] waterElapsed = CreateDoubleArray(1d);
            double[] weedElapsed = CreateDoubleArray(2d);
            double[] growthElapsed = CreateDoubleArray(3d);
            bool[] decayed = new bool[FarmField.PlotCount];
            decayed[0] = true;
            Assert.That(
                bootstrap.Simulation.RestoreRuntimeState(
                    4,
                    5,
                    waterElapsed,
                    weedElapsed,
                    growthElapsed,
                    decayed).Succeeded,
                Is.True);

            npcObject.transform.SetPositionAndRotation(
                new Vector3(3f, 0.25f, 4f),
                Quaternion.Euler(0f, 73f, 0f));
            Assert.That(
                replanner.RuntimeState.Memories.AddObservation(
                    bootstrap.Clock.ElapsedGameSeconds,
                    "我记得 01 号地已经整理得很整齐。",
                    8,
                    WorldEventKind.ActionCompleted,
                    out _).Succeeded,
                Is.True);
            int cycleNumber = replanner.RuntimeState.BeginCycle();
            Assert.That(
                replanner.RuntimeState.RecordCompletedCycleReflection(
                    cycleNumber,
                    new NpcReflection(
                        FarmGoalSpec.FullFieldCarrotLifecycleId,
                        NpcReflectionOutcome.Completed,
                        NpcMood.Proud,
                        "🌱",
                        "整齐完成一轮，下次继续先处理缺水。"),
                    bootstrap.Clock.ElapsedGameSeconds).Succeeded,
                Is.True);

            Assert.That(executor.Enqueue(new SowAction(3, 0.2f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new WaitAction(0.1f)).Succeeded, Is.True);
            Assert.That(executor.Tick(0f).Succeeded, Is.True);
            Assert.That(executor.CurrentAction, Is.TypeOf<SowAction>());
            int savedSeeds = bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed);

            ActionResult saved = service.Save();
            Assert.That(saved.Succeeded, Is.True, saved.Message);
            Assert.That(
                storage.Json,
                Does.Contain($"\"version\": {SaveData.CurrentVersion}"));
            Assert.That(storage.Json, Does.Contain("\"residentId\": \"resident-001\""));
            Assert.That(storage.Json, Does.Not.Contain("OPENAI_API_KEY"));
            Assert.That(storage.Json, Does.Not.Contain("sk-"));

            Assert.That(
                plot.RestoreState(PlotState.Empty, null, 0, false, false, false, 0).Succeeded,
                Is.True);
            Assert.That(bootstrap.Inventory.RestoreCounts(0, 0, 0, 0).Succeeded, Is.True);
            Assert.That(bootstrap.Clock.Restore(1d, 1d, false).Succeeded, Is.True);
            replanner.RuntimeState.Memories.Clear();
            npcObject.transform.position = Vector3.zero;

            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.Crop, Is.EqualTo(CropType.Carrot));
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(plot.IsFertilized, Is.True);
            Assert.That(plot.HasBeenWeeded, Is.True);
            Assert.That(plot.GrowthProgress, Is.EqualTo(37));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(savedSeeds));
            Assert.That(bootstrap.Clock.ElapsedGameSeconds, Is.EqualTo(12345d));
            Assert.That(bootstrap.Clock.TimeScale, Is.EqualTo(7d));
            Assert.That(bootstrap.Clock.IsPaused, Is.True);
            Assert.That(npcObject.transform.position, Is.EqualTo(new Vector3(3f, 0.25f, 4f)));
            Assert.That(replanner.RecentReflections, Has.Count.EqualTo(1));
            Assert.That(replanner.LatestReflection.Text, Does.Contain("整齐完成"));
            bool restoredObservation = false;
            foreach (MemoryEntry memory in replanner.Memories.Entries)
            {
                restoredObservation |= memory.Text.Contains("01 号地");
            }

            Assert.That(restoredObservation, Is.True);

            bootstrap.Simulation.CaptureRuntimeState(
                out double[] restoredWater,
                out double[] restoredWeeds,
                out double[] restoredGrowth,
                out bool[] restoredDecay);
            Assert.That(restoredWater, Is.EqualTo(waterElapsed));
            Assert.That(restoredWeeds, Is.EqualTo(weedElapsed));
            Assert.That(restoredGrowth, Is.EqualTo(growthElapsed));
            Assert.That(restoredDecay, Is.EqualTo(decayed));
            Assert.That(bootstrap.Simulation.WaterDecayEventCount, Is.EqualTo(4));
            Assert.That(bootstrap.Simulation.WeedEventCount, Is.EqualTo(5));

            Assert.That(executor.CurrentAction, Is.Null);
            Assert.That(executor.Queue.Count, Is.EqualTo(2));
            RunExecutorUntilIdle();
            Assert.That(bootstrap.Field.GetPlot(3).State, Is.EqualTo(PlotState.Growing));
            Assert.That(
                bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed),
                Is.EqualTo(savedSeeds - 1),
                "Restarted atomic action must commit exactly once.");
        }

        [Test]
        public void SaveLoad_PreservesAllRegisteredResidentMemoriesAndProvenance()
        {
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Amu,
                    out ResidentRuntimeState amu).Succeeded,
                Is.True);
            Assert.That(
                amu.Memories.AddObservation(
                    ResidentIds.Amu,
                    bootstrap.Clock.ElapsedGameSeconds,
                    "芽芽告诉我：今天完成了胡萝卜收获。",
                    9,
                    WorldEventKind.ConversationCompleted,
                    MemorySourceKind.Conversation,
                    "conversation:test-save-amu-yaya",
                    "fact-carrot-harvest-day-1",
                    "knowledge:resident-001:000000000001",
                    ResidentIds.Yaya,
                    new[] { "carrot", "harvest" },
                    true,
                    out MemoryEntry sourceFact).Succeeded,
                Is.True);

            Assert.That(service.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            Assert.That(data.residents, Has.Length.EqualTo(4));
            Assert.That(
                Array.ConvertAll(data.residents, resident => resident.residentId),
                Is.EqualTo(new[]
                {
                    ResidentIds.YayaValue,
                    ResidentIds.AmuValue,
                    ResidentIds.XiaosuiValue,
                    ResidentIds.MomoValue
                }));
            ResidentSaveData savedAmu = Array.Find(
                data.residents,
                resident => resident.residentId == ResidentIds.AmuValue);
            MemorySaveData savedFact = Array.Find(
                savedAmu.recentMemories,
                memory => memory.rootFactId == "fact-carrot-harvest-day-1");
            Assert.That(savedFact, Is.Not.Null);
            Assert.That(
                savedFact.immediateSourceResidentId,
                Is.EqualTo(ResidentIds.YayaValue));
            Assert.That(
                savedFact.rootFactId,
                Is.EqualTo("fact-carrot-harvest-day-1"));
            Assert.That(savedFact.knowledgeId, Is.EqualTo(sourceFact.KnowledgeId));
            Assert.That(
                savedFact.parentKnowledgeId,
                Is.EqualTo("knowledge:resident-001:000000000001"));
            Assert.That(
                savedFact.sourceEventId,
                Is.EqualTo("conversation:test-save-amu-yaya"));
            Assert.That(savedFact.tags, Does.Contain("carrot").And.Contain("harvest"));
            Assert.That(savedFact.isShareable, Is.True);

            amu.Memories.Clear();
            ActionResult loaded = service.Load();
            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Amu,
                    out ResidentRuntimeState restoredAmu).Succeeded,
                Is.True);
            Assert.That(
                restoredAmu.Memories.Query(
                    ResidentIds.Amu,
                    new[] { "harvest" },
                    3,
                    out IReadOnlyList<MemoryEntry> memories).Succeeded,
                Is.True);
            Assert.That(memories, Has.Count.EqualTo(1));
            Assert.That(memories[0].SourceKind, Is.EqualTo(MemorySourceKind.Conversation));
            Assert.That(memories[0].ImmediateSourceResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(memories[0].RootFactId, Is.EqualTo("fact-carrot-harvest-day-1"));
            Assert.That(memories[0].KnowledgeId, Is.EqualTo(sourceFact.KnowledgeId));
            Assert.That(
                memories[0].ParentKnowledgeId,
                Is.EqualTo("knowledge:resident-001:000000000001"));
            Assert.That(
                memories[0].SourceEventId,
                Is.EqualTo("conversation:test-save-amu-yaya"));
            Assert.That(memories[0].Tags, Does.Contain("carrot").And.Contain("harvest"));
            Assert.That(memories[0].IsShareable, Is.True);
        }

        [Test]
        public void SaveLoad_PreservesTownEventProposalConsumptionMarker()
        {
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Xiaosui,
                    out ResidentRuntimeState xiaosui).Succeeded,
                Is.True);
            Assert.That(
                xiaosui.Memories.AddObservation(
                    ResidentIds.Xiaosui,
                    bootstrap.Clock.ElapsedGameSeconds,
                    "I already used this carrot-harvest fact for a HarvestDinner proposal.",
                    MemoryEntry.MaximumImportance,
                    WorldEventKind.TownEventProposed,
                    MemorySourceKind.Perception,
                    "town-event:harvest-dinner:day-000002:proposal-consumed",
                    "fact-carrot-harvest-consumed",
                    string.Empty,
                    default,
                    new[]
                    {
                        "town-event",
                        "harvest-dinner",
                        "town-event-proposal-consumed"
                    },
                    false,
                    out MemoryEntry marker).Succeeded,
                Is.True);

            Assert.That(service.Save().Succeeded, Is.True);
            xiaosui.Memories.Clear();

            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Xiaosui,
                    out ResidentRuntimeState restored).Succeeded,
                Is.True);
            MemoryEntry restoredMarker = null;
            foreach (MemoryEntry memory in restored.Memories.Entries)
            {
                if (memory.KnowledgeId == marker.KnowledgeId)
                {
                    restoredMarker = memory;
                    break;
                }
            }

            Assert.That(restoredMarker, Is.Not.Null);
            Assert.That(restoredMarker.RootFactId, Is.EqualTo("fact-carrot-harvest-consumed"));
            Assert.That(restoredMarker.ParentKnowledgeId, Is.Empty);
            Assert.That(restoredMarker.SourceKind, Is.EqualTo(MemorySourceKind.Perception));
            Assert.That(restoredMarker.IsShareable, Is.False);
            Assert.That(restoredMarker.Tags, Does.Contain("town-event-proposal-consumed"));
        }

        [Test]
        public void Load_LegacySingleResidentSave_MigratesToYaya()
        {
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData current = JsonUtility.FromJson<SaveData>(storage.Json);
            ResidentSaveData resident = current.residents[0];
            foreach (MemorySaveData memory in resident.recentMemories)
            {
                memory.ownerResidentId = string.Empty;
            }

            var legacy = new LegacySaveDataV1
            {
                version = SaveData.LegacySingleResidentVersion,
                savedAtUtc = current.savedAtUtc,
                clock = current.clock,
                plots = current.plots,
                inventory = current.inventory,
                simulation = current.simulation,
                npc = resident.npc,
                hasFarmGoal = resident.hasFarmGoal,
                farmGoal = resident.farmGoal,
                executor = resident.executor,
                recentMemories = resident.recentMemories,
                recentReflections = resident.recentReflections,
                npcRuntime = resident.runtime
            };
            storage.Json = JsonUtility.ToJson(legacy, true);

            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(service.LastLoadMigratedLegacySave, Is.True);
            Assert.That(replanner.ResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(replanner.Memories.OwnerResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData migrated = JsonUtility.FromJson<SaveData>(storage.Json);
            Assert.That(migrated.version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(migrated.residents, Has.Length.EqualTo(4));
            Assert.That(migrated.residents[0].residentId, Is.EqualTo(ResidentIds.YayaValue));
            Assert.That(migrated.relationships, Has.Length.EqualTo(12));
            for (int index = 1; index < migrated.residents.Length; index++)
            {
                Assert.That(migrated.residents[index].recentMemories, Is.Empty);
                Assert.That(migrated.residents[index].hasFarmGoal, Is.False);
            }
        }

        [Test]
        public void Load_RunningGoal_DiscardsMidActionAndSafelyReplansFromWorldState()
        {
            Assert.That(
                replanner.SubmitGoal("把九块地种满胡萝卜并照顾到收获").Succeeded,
                Is.True);
            Assert.That(replanner.Status, Is.EqualTo(ReplanStatus.Running));
            Assert.That(executor.Queue.Count, Is.EqualTo(1));
            Assert.That(executor.Tick(0f).Succeeded, Is.True);
            Assert.That(executor.CurrentAction, Is.TypeOf<SowAction>());

            Assert.That(service.Save().Succeeded, Is.True);
            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(service.LastLoadUsedSafeReplan, Is.True);
            Assert.That(loaded.Message, Does.Contain("safely replanned"));
            Assert.That(replanner.Status, Is.EqualTo(ReplanStatus.Running));
            Assert.That(executor.CurrentAction, Is.Null);
            Assert.That(executor.Queue.Count, Is.EqualTo(1));
            Assert.That(executor.Queue.TryPeek(out INpcAction replanned).Succeeded, Is.True);
            Assert.That(replanned, Is.TypeOf<SowAction>());
            Assert.That(replanned.TargetPlotNumber, Is.EqualTo(1));

            RunExecutorUntilIdle();
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(replanner.TickReplan().Succeeded, Is.True);
            Assert.That(executor.Queue.TryPeek(out INpcAction next).Succeeded, Is.True);
            Assert.That(next.TargetPlotNumber, Is.EqualTo(2));
        }

        [Test]
        public void Load_RejectsConversationMemoryWithUnknownImmediateSource()
        {
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Amu,
                    out ResidentRuntimeState amu).Succeeded,
                Is.True);
            Assert.That(
                amu.Memories.AddObservation(
                    ResidentIds.Amu,
                    20d,
                    "芽芽告诉阿木的可传播事实。",
                    8,
                    WorldEventKind.ConversationCompleted,
                    MemorySourceKind.Conversation,
                    "conversation:save-source-validation",
                    "fact-save-source-validation",
                    "knowledge:resident-001:000000000099",
                    ResidentIds.Yaya,
                    new[] { "source-validation" },
                    true,
                    out _).Succeeded,
                Is.True);
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            ResidentSaveData savedAmu = Array.Find(
                data.residents,
                resident => resident.residentId == ResidentIds.AmuValue);
            MemorySaveData fact = Array.Find(
                savedAmu.recentMemories,
                memory => memory.rootFactId == "fact-save-source-validation");
            fact.immediateSourceResidentId = "resident-not-registered";
            storage.Json = JsonUtility.ToJson(data, true);

            ActionResult loaded = service.Load();

            Assert.That(loaded.Failed, Is.True);
            Assert.That(loaded.FailureReason, Is.EqualTo(ActionFailureReason.InvalidResponse));
        }

        [Test]
        public void Load_RejectsShareableReflectionMemory()
        {
            Assert.That(
                bootstrap.ResidentRegistry.TryGetRuntimeState(
                    ResidentIds.Amu,
                    out ResidentRuntimeState amu).Succeeded,
                Is.True);
            Assert.That(
                amu.Memories.AddReflection(
                    ResidentIds.Amu,
                    20d,
                    "这条反思必须保持私有。",
                    out MemoryEntry reflection).Succeeded,
                Is.True);
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            ResidentSaveData savedAmu = Array.Find(
                data.residents,
                resident => resident.residentId == ResidentIds.AmuValue);
            MemorySaveData savedReflection = Array.Find(
                savedAmu.recentMemories,
                memory => memory.knowledgeId == reflection.KnowledgeId);
            savedReflection.isShareable = true;
            storage.Json = JsonUtility.ToJson(data, true);

            ActionResult loaded = service.Load();

            Assert.That(loaded.Failed, Is.True);
            Assert.That(loaded.FailureReason, Is.EqualTo(ActionFailureReason.InvalidResponse));
        }

        [Test]
        public void SaveLoad_PreservesDirectedRelationshipsWithoutTransposition()
        {
            var graph = new SocialGraph(bootstrap.ResidentRegistry.ResidentIds);
            var snapshots = new List<RelationshipStateSnapshot>();
            foreach (RelationshipStateSnapshot snapshot in graph.CaptureSnapshots())
            {
                if (snapshot.OwnerResidentId == ResidentIds.Yaya &&
                    snapshot.OtherResidentId == ResidentIds.Amu)
                {
                    snapshots.Add(new RelationshipStateSnapshot(
                        snapshot.OwnerResidentId,
                        snapshot.OtherResidentId,
                        17,
                        9,
                        1L,
                        "relationship:yaya-to-amu"));
                }
                else if (snapshot.OwnerResidentId == ResidentIds.Amu &&
                    snapshot.OtherResidentId == ResidentIds.Yaya)
                {
                    snapshots.Add(new RelationshipStateSnapshot(
                        snapshot.OwnerResidentId,
                        snapshot.OtherResidentId,
                        3,
                        -4,
                        1L,
                        "relationship:amu-to-yaya"));
                }
                else
                {
                    snapshots.Add(snapshot);
                }
            }

            Assert.That(graph.Restore(snapshots).Succeeded, Is.True);
            var relationshipService = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                npcObject.transform,
                storage,
                graph);
            Assert.That(relationshipService.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            Assert.That(data.relationships, Has.Length.EqualTo(12));
            Assert.That(graph.Reset().Succeeded, Is.True);

            ActionResult loaded = relationshipService.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(
                graph.TryGetRelationship(
                    ResidentIds.Yaya,
                    ResidentIds.Amu,
                    out RelationshipState yayaToAmu).Succeeded,
                Is.True);
            Assert.That(
                graph.TryGetRelationship(
                    ResidentIds.Amu,
                    ResidentIds.Yaya,
                    out RelationshipState amuToYaya).Succeeded,
                Is.True);
            Assert.That(yayaToAmu.Familiarity, Is.EqualTo(17));
            Assert.That(yayaToAmu.Trust, Is.EqualTo(9));
            Assert.That(amuToYaya.Familiarity, Is.EqualTo(3));
            Assert.That(amuToYaya.Trust, Is.EqualTo(-4));
            Assert.That(yayaToAmu, Is.Not.SameAs(amuToYaya));
        }

        [Test]
        public void Load_MissingResidentOrDuplicateRelationship_IsRejectedBeforeLiveMutation()
        {
            Assert.That(service.Save().Succeeded, Is.True);
            SaveData data = JsonUtility.FromJson<SaveData>(storage.Json);
            data.residents = new[]
            {
                data.residents[0],
                data.residents[1],
                data.residents[2]
            };
            storage.Json = JsonUtility.ToJson(data, true);
            Assert.That(bootstrap.Field.GetPlot(1).Sow(bootstrap.Inventory).Succeeded, Is.True);
            long revisionBeforeLoad = bootstrap.AuthoritativeStateRevision;

            ActionResult missingResident = service.Load();

            Assert.That(missingResident.Failed, Is.True);
            Assert.That(missingResident.FailureReason, Is.EqualTo(ActionFailureReason.InvalidResponse));
            Assert.That(bootstrap.AuthoritativeStateRevision, Is.EqualTo(revisionBeforeLoad));
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));

            Assert.That(service.Save().Succeeded, Is.True);
            data = JsonUtility.FromJson<SaveData>(storage.Json);
            data.relationships[0].ownerResidentId =
                data.relationships[1].ownerResidentId;
            data.relationships[0].otherResidentId =
                data.relationships[1].otherResidentId;
            storage.Json = JsonUtility.ToJson(data, true);
            revisionBeforeLoad = bootstrap.AuthoritativeStateRevision;

            ActionResult duplicateRelationship = service.Load();

            Assert.That(duplicateRelationship.Failed, Is.True);
            Assert.That(
                duplicateRelationship.FailureReason,
                Is.EqualTo(ActionFailureReason.InvalidResponse));
            Assert.That(bootstrap.AuthoritativeStateRevision, Is.EqualTo(revisionBeforeLoad));
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
        }

        [Test]
        public void SaveSchema_OmitsTransientRequestsConversationsAndReservations()
        {
            Assert.That(service.Save().Succeeded, Is.True);

            Assert.That(storage.Json, Does.Not.Contain("activeRequest"));
            Assert.That(storage.Json, Does.Not.Contain("ConversationSession"));
            Assert.That(storage.Json, Does.Not.Contain("participantLock"));
            Assert.That(storage.Json, Does.Not.Contain("reservation"));
            Assert.That(storage.Json, Does.Not.Contain("TownEventSession"));
        }

        [Test]
        public void Load_WithActiveAndPendingAiRequests_CancelsStaleWorkAndCoordinatorRemainsReusable()
        {
            Assert.That(service.Save().Succeeded, Is.True);
            var gateway = new LoadResetGatewayClient();
            var coordinator = new AiRequestCoordinator(
                gateway,
                maximumConcurrentRequests: 1,
                deterministicLocalFallback: new LocalAiGatewayClient());
            ReplaceAiRequestCoordinator(coordinator);
            int activeCompletionCount = 0;
            int pendingCompletionCount = 0;
            IEnumerator active = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.CommandAccepted,
                "stale-active",
                _ => activeCompletionCount++);
            IEnumerator pending = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.WaterNeeded,
                "stale-pending",
                _ => pendingCompletionCount++);

            Assert.That(active.MoveNext(), Is.True);
            Assert.That(pending.MoveNext(), Is.True);
            Assert.That(active.MoveNext(), Is.True);
            Assert.That(pending.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.PendingRequestCount, Is.EqualTo(1));
            Assert.That(gateway.ActiveOperationCount, Is.EqualTo(1));
            long revisionBeforeLoad = bootstrap.AuthoritativeStateRevision;

            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(
                bootstrap.AuthoritativeStateRevision,
                Is.EqualTo(revisionBeforeLoad + 1));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
            Assert.That(gateway.ActiveOperationCount, Is.Zero);
            Assert.That(gateway.DisposedOperationCount, Is.EqualTo(1));

            gateway.PublishDisposedRequestResult();
            Assert.That(active.MoveNext(), Is.False);
            Assert.That(pending.MoveNext(), Is.False);
            Assert.That(activeCompletionCount, Is.Zero);
            Assert.That(pendingCompletionCount, Is.Zero);

            AiGatewayResult<NpcExpression> replacementResult = null;
            IEnumerator replacement = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.CommandAccepted,
                "replacement",
                value => replacementResult = value);
            while (replacement.MoveNext())
            {
            }

            Assert.That(replacementResult, Is.Not.Null);
            Assert.That(replacementResult.Succeeded, Is.True, replacementResult.Outcome.Message);
            Assert.That(replacementResult.ResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
            Assert.That(coordinator.IsShutdown, Is.False);
        }

        [Test]
        public void SaveLoad_FourPrivateMemoryPartitionsAndAllDirectedRelationshipsDoNotCross()
        {
            var graph = new SocialGraph(bootstrap.ResidentRegistry.ResidentIds);
            IReadOnlyList<RelationshipStateSnapshot> expectedRelationships =
                CreateDistinctRelationshipSnapshots(graph, 0);
            Assert.That(graph.Restore(expectedRelationships).Succeeded, Is.True);
            ReplaceAllResidentMemoriesWithPrivateSentinels("saved");
            var isolatedService = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                npcObject.transform,
                storage,
                graph);

            Assert.That(isolatedService.Save().Succeeded, Is.True);
            SaveData captured = JsonUtility.FromJson<SaveData>(storage.Json);
            foreach (ResidentSaveData resident in captured.residents)
            {
                Assert.That(
                    resident.recentMemories,
                    Has.Length.EqualTo(1),
                    $"Captured Resident '{resident.residentId}' must own one private sentinel.");
            }

            Assert.That(graph.Reset().Succeeded, Is.True);
            ClearAllResidentMemories();

            ActionResult loaded = isolatedService.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            AssertPrivateResidentMemories("saved");
            AssertDirectedRelationships(graph, expectedRelationships);
        }

        [Test]
        public void Load_TamperedMemoryOwner_IsRejectedBeforeResetWithoutMutatingLivePartitionsOrRelationships()
        {
            var graph = new SocialGraph(bootstrap.ResidentRegistry.ResidentIds);
            Assert.That(
                graph.Restore(CreateDistinctRelationshipSnapshots(graph, 0)).Succeeded,
                Is.True);
            ReplaceAllResidentMemoriesWithPrivateSentinels("saved");
            var isolatedService = new SaveGameService(
                bootstrap,
                executor,
                replanner,
                npcObject.transform,
                storage,
                graph);
            Assert.That(isolatedService.Save().Succeeded, Is.True);

            SaveData tampered = JsonUtility.FromJson<SaveData>(storage.Json);
            ResidentSaveData yaya = Array.Find(
                tampered.residents,
                resident => resident.residentId == ResidentIds.YayaValue);
            Assert.That(yaya, Is.Not.Null);
            Assert.That(yaya.recentMemories, Is.Not.Empty);
            yaya.recentMemories[0].ownerResidentId = ResidentIds.AmuValue;
            storage.Json = JsonUtility.ToJson(tampered, true);

            ClearAllResidentMemories();
            ReplaceAllResidentMemoriesWithPrivateSentinels("live");
            IReadOnlyList<RelationshipStateSnapshot> liveRelationships =
                CreateDistinctRelationshipSnapshots(graph, 40);
            Assert.That(graph.Restore(liveRelationships).Succeeded, Is.True);
            long revisionBeforeLoad = bootstrap.AuthoritativeStateRevision;

            ActionResult loaded = isolatedService.Load();

            Assert.That(loaded.Failed, Is.True);
            Assert.That(loaded.FailureReason, Is.EqualTo(ActionFailureReason.InvalidResponse));
            Assert.That(bootstrap.AuthoritativeStateRevision, Is.EqualTo(revisionBeforeLoad));
            AssertPrivateResidentMemories("live");
            AssertDirectedRelationships(graph, liveRelationships);
        }

        [Test]
        public void CorruptPrimarySave_LoadsValidatedBackupWithoutKeepingLiveMutation()
        {
            Assert.That(service.Save().Succeeded, Is.True);
            storage.BackupJson = storage.Json;
            storage.Json = "{ broken primary save";
            Assert.That(bootstrap.Field.GetPlot(1).Sow(bootstrap.Inventory).Succeeded, Is.True);

            ActionResult loaded = service.Load();

            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(service.LastLoadRecoveredBackup, Is.True);
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(20));
        }

        [Test]
        public void CorruptSave_ControllerPreservesLiveGameAndSaveWithoutThrowing()
        {
            Assert.That(bootstrap.Field.GetPlot(1).Sow(bootstrap.Inventory).Succeeded, Is.True);
            storage.Json = "{ definitely not valid save json";
            GameObject ui = new GameObject("SaveUi", typeof(RectTransform));
            ui.transform.SetParent(root.transform, false);
            Button saveButton = CreateButton(ui.transform, "Save");
            Button loadButton = CreateButton(ui.transform, "Load");
            Button newButton = CreateButton(ui.transform, "New");
            Text status = new GameObject("Status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            status.transform.SetParent(ui.transform, false);
            SaveGameController controller = ui.AddComponent<SaveGameController>();
            Assert.That(
                controller.Configure(
                    bootstrap,
                    executor,
                    replanner,
                    npcObject.transform,
                    saveButton,
                    loadButton,
                    newButton,
                    status).Succeeded,
                Is.True);
            Assert.That(controller.Initialize(storage).Succeeded, Is.True);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("存档损坏或不可读.*当前游戏未被修改"));

            Assert.DoesNotThrow(controller.Load);

            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(19));
            Assert.That(replanner.Status, Is.EqualTo(ReplanStatus.Idle));
            Assert.That(executor.IsBusy, Is.False);
            Assert.That(status.text, Does.Contain("当前游戏未被修改"));
            Assert.That(storage.DeleteCount, Is.Zero);
        }

        [Test]
        public void NavigationFailure_FailsImmediatelyWithoutMutationAndCanBeReset()
        {
            navigation.FailBegin = true;
            Assert.That(executor.Enqueue(new SowAction(1, 0.2f)).Succeeded, Is.True);
            Assert.That(executor.Tick(0f).Succeeded, Is.True);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("NPC action 'Sow Plot 01' failed: Plot 01 is unreachable"));

            ActionResult failed = executor.Tick(0f);

            Assert.That(failed.Failed, Is.True);
            Assert.That(failed.FailureReason, Is.EqualTo(ActionFailureReason.NavigationFailed));
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Failed));
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(20));
            Assert.That(executor.ResetAfterFailure().Succeeded, Is.True);
            Assert.That(executor.IsBusy, Is.False);
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
        }

        [Test]
        public void NavigationFailure_DuringGoal_MarksGoalFailedWithoutWaitingForever()
        {
            navigation.FailBegin = true;
            Assert.That(
                replanner.SubmitGoal("把九块地种满胡萝卜并照顾到收获").Succeeded,
                Is.True);
            Assert.That(executor.Tick(0f).Succeeded, Is.True);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("NPC action 'Sow Plot 01' failed: Plot 01 is unreachable"));

            ActionResult failed = executor.Tick(0f);

            Assert.That(failed.Failed, Is.True);
            Assert.That(replanner.Status, Is.EqualTo(ReplanStatus.Failed));
            Assert.That(replanner.IsGoalActive, Is.False);
            Assert.That(replanner.LastFailureReason, Does.Contain("unreachable"));
            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(20));
        }

        private void RunExecutorUntilIdle()
        {
            int ticks = 0;
            while (executor.IsBusy && ticks++ < 40)
            {
                ActionResult result = executor.Tick(1f);
                Assert.That(result.Succeeded, Is.True, result.Message);
            }

            Assert.That(ticks, Is.LessThan(40), "Restored action queue did not finish.");
            Assert.That(executor.IsBusy, Is.False);
        }

        private static double[] CreateDoubleArray(double firstValue)
        {
            var values = new double[FarmField.PlotCount];
            values[0] = firstValue;
            return values;
        }

        private void ReplaceAiRequestCoordinator(AiRequestCoordinator coordinator)
        {
            PropertyInfo property = typeof(GameBootstrap).GetProperty(
                nameof(GameBootstrap.AiRequests),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo setter = property?.GetSetMethod(nonPublic: true);
            Assert.That(setter, Is.Not.Null, "GameBootstrap must retain its private AI coordinator setter.");
            bootstrap.AiRequests?.Shutdown();
            setter.Invoke(bootstrap, new object[] { coordinator });
        }

        private void ClearAllResidentMemories()
        {
            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                Assert.That(
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtimeState).Succeeded,
                    Is.True);
                runtimeState.Memories.Clear();
            }
        }

        private void ReplaceAllResidentMemoriesWithPrivateSentinels(string phase)
        {
            ClearAllResidentMemories();
            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                Assert.That(
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtimeState).Succeeded,
                    Is.True);
                Assert.That(
                    runtimeState.Memories.AddObservation(
                        residentId,
                        bootstrap.Clock.ElapsedGameSeconds,
                        $"private-memory:{phase}:{residentId.Value}",
                        6,
                        WorldEventKind.System,
                        MemorySourceKind.Perception,
                        $"private-event:{phase}:{residentId.Value}",
                        $"private-fact:{phase}:{residentId.Value}",
                        string.Empty,
                        default,
                        new[] { "private-sentinel", phase },
                        false,
                        out _).Succeeded,
                    Is.True);
            }
        }

        private void AssertPrivateResidentMemories(string phase)
        {
            foreach (ResidentId residentId in bootstrap.ResidentRegistry.ResidentIds)
            {
                Assert.That(
                    bootstrap.ResidentRegistry.TryGetRuntimeState(
                        residentId,
                        out ResidentRuntimeState runtimeState).Succeeded,
                    Is.True);
                Assert.That(runtimeState.Memories.OwnerResidentId, Is.EqualTo(residentId));
                Assert.That(
                    runtimeState.Memories.Entries,
                    Has.Count.EqualTo(1),
                    $"Resident '{residentId}' must retain only its '{phase}' private sentinel.");
                MemoryEntry memory = runtimeState.Memories.Entries[0];
                Assert.That(memory.OwnerResidentId, Is.EqualTo(residentId));
                Assert.That(memory.Text, Is.EqualTo($"private-memory:{phase}:{residentId.Value}"));
                Assert.That(
                    memory.RootFactId,
                    Is.EqualTo($"private-fact:{phase}:{residentId.Value}"));
                Assert.That(memory.IsShareable, Is.False);
            }
        }

        private static IReadOnlyList<RelationshipStateSnapshot>
            CreateDistinctRelationshipSnapshots(SocialGraph graph, int valueOffset)
        {
            var snapshots = new List<RelationshipStateSnapshot>();
            IReadOnlyList<RelationshipStateSnapshot> empty = graph.CaptureSnapshots();
            for (int index = 0; index < empty.Count; index++)
            {
                RelationshipStateSnapshot edge = empty[index];
                int value = valueOffset + index + 1;
                snapshots.Add(new RelationshipStateSnapshot(
                    edge.OwnerResidentId,
                    edge.OtherResidentId,
                    value,
                    -value,
                    value,
                    $"relationship:{value}:{edge.OwnerResidentId.Value}:{edge.OtherResidentId.Value}"));
            }

            return snapshots;
        }

        private static void AssertDirectedRelationships(
            SocialGraph graph,
            IReadOnlyList<RelationshipStateSnapshot> expected)
        {
            Assert.That(graph.RelationshipCount, Is.EqualTo(expected.Count));
            foreach (RelationshipStateSnapshot snapshot in expected)
            {
                Assert.That(
                    graph.TryGetRelationship(
                        snapshot.OwnerResidentId,
                        snapshot.OtherResidentId,
                        out RelationshipState actual).Succeeded,
                    Is.True);
                Assert.That(actual.Snapshot, Is.EqualTo(snapshot));
            }
        }

        private static Button CreateButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private sealed class InMemorySaveStorage : IRecoverableSaveGameStorage
        {
            public string Json { get; set; }

            public string BackupJson { get; set; }

            public int DeleteCount { get; private set; }

            public string SavePath => "memory://aifarm-save-v1.json";

            public string BackupPath => "memory://aifarm-save-v1.json.bak";

            public ActionResult Write(string json)
            {
                Json = json;
                return ActionResult.Success("Saved in memory.");
            }

            public ActionResult Read(out string json)
            {
                json = Json;
                return string.IsNullOrEmpty(json)
                    ? ActionResult.Failure(ActionFailureReason.InvalidState, "No save exists.")
                    : ActionResult.Success("Read from memory.");
            }

            public ActionResult ReadBackup(out string json)
            {
                json = BackupJson;
                return string.IsNullOrEmpty(json)
                    ? ActionResult.Failure(ActionFailureReason.InvalidState, "No backup exists.")
                    : ActionResult.Success("Read backup from memory.");
            }

            public ActionResult Delete()
            {
                Json = null;
                BackupJson = null;
                DeleteCount++;
                return ActionResult.Success("Deleted in-memory save.");
            }
        }

        private sealed class FakeNavigation : INpcNavigationDriver
        {
            public bool FailBegin { get; set; }

            public bool IsMoving { get; private set; }

            public ActionResult BeginMove(int plotNumber)
            {
                if (FailBegin)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.NavigationFailed,
                        $"Plot {plotNumber:00} is unreachable.");
                }

                IsMoving = true;
                return ActionResult.Success("Moving.");
            }

            public ActionResult Tick(float deltaTime, out bool arrived)
            {
                arrived = true;
                IsMoving = false;
                return ActionResult.Success("Arrived.");
            }

            public ActionResult CancelMove()
            {
                IsMoving = false;
                return ActionResult.Success("Cancelled.");
            }
        }

        private sealed class ImmediateFeedback : INpcActionFeedback
        {
            public bool IsPlaying { get; private set; }

            public float Progress => IsPlaying ? 0f : 1f;

            public ActionResult Begin(string actionName, float durationSeconds)
            {
                IsPlaying = true;
                return ActionResult.Success("Feedback started.");
            }

            public ActionResult Tick(float deltaTime, out bool completed)
            {
                completed = true;
                IsPlaying = false;
                return ActionResult.Success("Feedback completed.");
            }

            public ActionResult Cancel()
            {
                IsPlaying = false;
                return ActionResult.Success("Feedback cancelled.");
            }
        }

        private sealed class LoadResetGatewayClient : IAiGatewayClient
        {
            private readonly LocalAiGatewayClient local = new LocalAiGatewayClient();
            private Action disposedRequestCompletion;
            private int utteranceCount;

            public AiGatewayMode ConfiguredMode => AiGatewayMode.Remote;

            public AiGatewayMode ActiveMode => AiGatewayMode.Remote;

            public int ActiveOperationCount { get; private set; }

            public int DisposedOperationCount { get; private set; }

            public IEnumerator InterpretCommand(
                string command,
                Action<AiGatewayResult<FarmGoalSpec>> completed)
            {
                return local.InterpretCommand(command, completed);
            }

            public IEnumerator InterpretCommand(
                ResidentId residentId,
                string command,
                Action<AiGatewayResult<FarmGoalSpec>> completed)
            {
                return local.InterpretCommand(residentId, command, completed);
            }

            public IEnumerator GenerateUtterance(
                NpcExpressionTrigger trigger,
                string context,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                return GenerateUtterance(ResidentIds.Yaya, trigger, context, completed);
            }

            public IEnumerator GenerateUtterance(
                ResidentId residentId,
                NpcExpressionTrigger trigger,
                string context,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                var expression = new NpcExpression(
                    trigger,
                    NpcMood.Focused,
                    "!",
                    context);
                if (utteranceCount++ == 0)
                {
                    return BlockUntilDisposed(residentId, expression, completed);
                }

                return Complete(residentId, expression, completed);
            }

            public IEnumerator Reflect(
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                return local.Reflect(goal, outcome, eventSummary, completed);
            }

            public IEnumerator Reflect(
                ResidentId residentId,
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                return local.Reflect(residentId, goal, outcome, eventSummary, completed);
            }

            public IEnumerator GenerateConversationScript(
                ConversationScriptRequest request,
                Action<AiGatewayResult<ConversationScriptSpec>> completed)
            {
                return local.GenerateConversationScript(request, completed);
            }

            public IEnumerator DecideResident(
                ResidentDecisionRequest request,
                Action<AiGatewayResult<ResidentDecisionSpec>> completed)
            {
                return local.DecideResident(request, completed);
            }

            public void PublishDisposedRequestResult()
            {
                Assert.That(disposedRequestCompletion, Is.Not.Null);
                disposedRequestCompletion();
            }

            private IEnumerator BlockUntilDisposed(
                ResidentId residentId,
                NpcExpression expression,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                ActiveOperationCount++;
                disposedRequestCompletion = () => completed(
                    AiGatewayResult<NpcExpression>.Success(
                        residentId,
                        expression,
                        AiGatewayMode.Remote));
                try
                {
                    while (true)
                    {
                        yield return null;
                    }
                }
                finally
                {
                    ActiveOperationCount--;
                    DisposedOperationCount++;
                }
            }

            private static IEnumerator Complete(
                ResidentId residentId,
                NpcExpression expression,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                yield return null;
                completed(AiGatewayResult<NpcExpression>.Success(
                    residentId,
                    expression,
                    AiGatewayMode.Remote));
            }
        }
    }
}
