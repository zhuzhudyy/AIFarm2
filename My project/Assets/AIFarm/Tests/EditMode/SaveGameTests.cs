using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
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
                    new LocalAiGatewayClient()).Succeeded,
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
            Assert.That(storage.Json, Does.Contain("\"version\": 3"));
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
        public void CorruptSave_ControllerPromptsAndReturnsToNewDemoWithoutThrowing()
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
                new Regex("存档损坏或不可读.*已回到新 Demo"));

            Assert.DoesNotThrow(controller.Load);

            Assert.That(bootstrap.Field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(20));
            Assert.That(replanner.Status, Is.EqualTo(ReplanStatus.Idle));
            Assert.That(executor.IsBusy, Is.False);
            Assert.That(status.text, Does.Contain("已回到新 Demo"));
            Assert.That(storage.DeleteCount, Is.EqualTo(1));
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

        private static Button CreateButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            return buttonObject.GetComponent<Button>();
        }

        private sealed class InMemorySaveStorage : ISaveGameStorage
        {
            public string Json { get; set; }

            public int DeleteCount { get; private set; }

            public string SavePath => "memory://aifarm-save-v1.json";

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

            public ActionResult Delete()
            {
                Json = null;
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
    }
}
