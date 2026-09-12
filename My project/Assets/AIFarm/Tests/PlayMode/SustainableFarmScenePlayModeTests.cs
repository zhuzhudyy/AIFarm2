using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.PlayMode
{
    public sealed class SustainableFarmScenePlayModeTests
    {
        private Scene fixtureScene;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (fixtureScene.IsValid() && fixtureScene.isLoaded)
            {
                // Dispose only the DemoScene loaded by this fixture, so its
                // overlays, event systems and stopped bootstrap cannot affect a
                // later isolated UI fixture. Never save the edited scene.
                Scene cleanup = SceneManager.CreateScene("SustainableFarmTestCleanup");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(fixtureScene);
            }
        }

        [UnityTest]
        public IEnumerator ActualTendFarm_ThreeNormalRounds_WithSaveReloadAndSharedResourceAccounting()
        {
            yield return SceneManager.LoadSceneAsync("DemoScene", LoadSceneMode.Single);
            fixtureScene = SceneManager.GetSceneByName("DemoScene");
            yield return null;
            GameBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.LifeControllers.Count, Is.EqualTo(4));
            foreach (GatewayConnectionController connection in UnityEngine.Object.FindObjectsByType<GatewayConnectionController>(FindObjectsSortMode.None))
            { connection.StopAllCoroutines(); connection.enabled = false; }
            foreach (TownLifeController resident in bootstrap.LifeControllers.Values) resident.StopTask();
            bootstrap.AiRequests.ReplaceClient(new LocalAiGatewayClient());
            // Establish the normal new-game fixture once. There are no resets,
            // replenishment helpers or direct plot mutations after this point.
            Success(bootstrap.ResetToConfiguredDefaults());
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(9));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Water), Is.EqualTo(9));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fertilizer), Is.EqualTo(9));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Carrot), Is.Zero);
            Assert.That(bootstrap.SceneConfig.FastGrowthDemo, Is.False);
            Assert.That(bootstrap.Mode.MaturityGameSeconds, Is.EqualTo(172800d));
            Assert.That(bootstrap.Clock.GameSecondsPerRealSecond, Is.EqualTo(120d));
            Success(bootstrap.Clock.SetTimeScale(1d));
            // The test controls only time advancement. The actual production life
            // coroutine, planner, executor, feedback and inventory settlement run.
            bootstrap.enabled = false;

            TownLifeController farmer = bootstrap.LifeControllers[ResidentIds.Yaya];
            NpcPlanExecutor farmerExecutor = farmer.GetComponent<NpcPlanExecutor>();
            ReplanController legacy = UnityEngine.Object.FindObjectsByType<ReplanController>(FindObjectsSortMode.None)
                .Single(controller => controller.ResidentId == ResidentIds.Yaya);
            var storage = new MemoryStorage();
            var save = new SaveGameService(bootstrap, farmerExecutor, legacy, farmer.transform, storage);
            var harvests = new Dictionary<int, int>();
            int sowCount = 0, weedCount = 0, fertilizerCount = 0, produce = 0, returnedSeeds = 0;
            int successfulWaterActions = 0;
            var actionFailures = new List<string>();
            foreach (TownLifeController resident in bootstrap.LifeControllers.Values)
            {
                ResidentId owner = resident.ResidentId;
                NpcPlanExecutor executor = resident.GetComponent<NpcPlanExecutor>();
                executor.ActionFailed += (action, result) => actionFailures.Add(owner + ": " + result.Message);
                executor.ActionCompleted += (action, result) =>
                {
                    Assert.That(result.Succeeded, Is.True, result.Message);
                    if (!(action is WaitAction)) Assert.That(owner, Is.EqualTo(ResidentIds.Yaya),
                        "Other autonomous residents must respect the player's nine-plot task claim.");
                    if (action is SowAction) sowCount++;
                    if (action is WaterAction) successfulWaterActions++;
                    if (action is FertilizeAction) fertilizerCount++;
                    if (action is WeedAction) weedCount++;
                    if (action is HarvestAction)
                    {
                        int plotNumber = action.TargetPlotNumber.Value;
                        harvests.TryGetValue(plotNumber, out int prior);
                        harvests[plotNumber] = prior + 1;
                        produce += bootstrap.Field.GetPlot(plotNumber).CropYieldCount;
                        returnedSeeds += bootstrap.Field.GetPlot(plotNumber).SeedReturnCount;
                    }
                };
            }

            Success(bootstrap.SubmitResidentCommand(ResidentIds.Yaya,
                "持续照料全部九块农田，循环种胡萝卜并完成播种、浇水、施肥、除草和收获。"));
            double deadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 180d;
            bool loadedMidSecondRound = false;
            int consumedCarrots = 0;
            while (farmer.CompletedCycles < 3 && UnityEngine.Time.realtimeSinceStartupAsDouble < deadline)
            {
                foreach (TownLifeController resident in bootstrap.LifeControllers.Values) FinishTravelForTest(resident);
                bool needsCare = bootstrap.Field.Plots.Any(plot => plot.State == PlotState.Empty || plot.State == PlotState.Mature ||
                    plot.WaterLevel < FarmPlot.RequiredWaterLevel || !plot.IsFertilized || plot.HasWeeds);
                // Large time steps only while there is no immediately necessary
                // care. Rain/water and maturity still use the unchanged two-day rules.
                Success(bootstrap.Simulation.Advance((needsCare ? 30d : 7200d) /
                    (bootstrap.Clock.GameSecondsPerRealSecond * bootstrap.Clock.TimeScale)));
                yield return null;

                Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(9 + returnedSeeds - sowCount),
                    "Every seed must originate in the initial nine or a completed real harvest.");
                int convertedCompost = weedCount - bootstrap.Inventory.GetCount(InventoryItem.Compost);
                Assert.That(convertedCompost, Is.GreaterThanOrEqualTo(0));
                Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Fertilizer), Is.EqualTo(9 + convertedCompost - fertilizerCount),
                    "New fertilizer must come from actual removed weeds processed at the well.");
                foreach (InventoryItem item in Enum.GetValues(typeof(InventoryItem)))
                    Assert.That(bootstrap.Inventory.GetCount(item), Is.GreaterThanOrEqualTo(0), item.ToString());
                int nowConsumed = produce - bootstrap.Inventory.GetCount(InventoryItem.Carrot);
                Assert.That(nowConsumed, Is.GreaterThanOrEqualTo(consumedCarrots),
                    "Carrot production must not duplicate during retries or loading.");
                consumedCarrots = nowConsumed;

                if (!loadedMidSecondRound && farmer.CompletedCycles == 1 &&
                    bootstrap.Field.Plots.Any(plot => plot.State == PlotState.Growing && plot.GrowthProgress > 0))
                {
                    int[] progress = bootstrap.Field.Plots.Select(plot => plot.GrowthProgress).ToArray();
                    int seedsBefore = bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed);
                    int carrotsBefore = bootstrap.Inventory.GetCount(InventoryItem.Carrot);
                    double clockBefore = bootstrap.Clock.ElapsedGameSeconds;
                    Success(save.Save());
                    Assert.That(storage.Json, Does.Contain("lifeTasks").And.Contain("TendFarm"));
                    Success(save.Load());
                    Assert.That(farmer.CompletedCycles, Is.EqualTo(1));
                    Assert.That(farmer.HasPlayerTask, Is.True);
                    Assert.That(bootstrap.Field.Plots.Select(plot => plot.GrowthProgress), Is.EqualTo(progress));
                    Assert.That(bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(seedsBefore));
                    Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(carrotsBefore));
                    Assert.That(bootstrap.Clock.ElapsedGameSeconds, Is.EqualTo(clockBefore));
                    loadedMidSecondRound = true;
                }
            }

            Assert.That(farmer.CompletedCycles, Is.EqualTo(3), farmer.Diagnostics + "\n" + string.Join("\n", actionFailures));
            Assert.That(loadedMidSecondRound, Is.True, "The test must actually save and reload a growing second crop.");
            Assert.That(harvests.Keys, Is.EquivalentTo(Enumerable.Range(1, 9)));
            foreach (int count in harvests.Values) Assert.That(count, Is.EqualTo(3));
            Assert.That(produce, Is.GreaterThanOrEqualTo(27));
            Assert.That(bootstrap.Inventory.GetCount(InventoryItem.Carrot) + consumedCarrots, Is.EqualTo(produce));
            Assert.That(sowCount, Is.EqualTo(27));
            Assert.That(fertilizerCount, Is.EqualTo(27));
            Assert.That(weedCount, Is.EqualTo(27));
            Assert.That(successfulWaterActions, Is.GreaterThan(27), "Crops must receive ongoing water, including actual resupply.");
            Assert.That(bootstrap.Clock.ElapsedGameSeconds, Is.GreaterThan(6d * 86400d));
            Assert.That(actionFailures, Is.Empty, "A normal cultivated cycle should not rely on failed actions or duplicate settlement.");
            Success(bootstrap.SubmitResidentCommand(ResidentIds.Yaya, "停止"));
            Assert.That(farmer.HasPlayerTask, Is.False);
            Assert.That(farmerExecutor.IsBusy, Is.False);
            for (int plot = 1; plot <= 9; plot++) Assert.That(bootstrap.CanWorkPlot(plot, ResidentIds.Amu), Is.True);

            string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string verification = Path.Combine(repository, "Docs/Verification");
            Directory.CreateDirectory(verification);
            File.WriteAllText(Path.Combine(verification, "agriculture-three-rounds.txt"),
                "PASS: Actual DemoScene / TownLife / NpcPlanExecutor / SaveGameService\n" +
                "Test acceleration: manually advanced the production GameClock; Warp used only for real NavMesh arrival points.\n" +
                "This is not a six-day real-time soak test. No crop, seed, fertilizer or food settlement was fabricated.\n" +
                $"Initial seeds/water/fertilizer: 9/9/9\nCompleted cycles: {farmer.CompletedCycles}\n" +
                $"Harvest actions: {harvests.Values.Sum()}\nProduced carrots: {produce}\nConsumed carrots: {consumedCarrots}\n" +
                $"Warehouse carrots: {bootstrap.Inventory.GetCount(InventoryItem.Carrot)}\n" +
                $"Warehouse seeds: {bootstrap.Inventory.GetCount(InventoryItem.CarrotSeed)}\n" +
                $"Sow/water/fertilize/weed actions: {sowCount}/{successfulWaterActions}/{fertilizerCount}/{weedCount}\n" +
                $"Fertilizer produced from compost: {weedCount - bootstrap.Inventory.GetCount(InventoryItem.Compost)}\n" +
                $"Second-round growing crop save/load: {loadedMidSecondRound}\n" +
                $"Elapsed game seconds: {bootstrap.Clock.ElapsedGameSeconds}\n" +
                "Stopped: no active player task or executor action; all nine task claims released.\n");
            Button farmButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(button => button.name == "FarmToggle");
            farmButton.onClick.Invoke();
            yield return null;
            Canvas.ForceUpdateCanvases();
            string previewDirectory = Path.Combine(repository, "Docs/ArtPreview");
            Directory.CreateDirectory(previewDirectory);
            string screenshot = Path.Combine(previewDirectory, "agriculture-three-rounds.png");
            ScreenCapture.CaptureScreenshot(screenshot);
            yield return null;
            double screenshotDeadline = UnityEngine.Time.realtimeSinceStartupAsDouble + 3d;
            while ((!File.Exists(screenshot) || new FileInfo(screenshot).Length == 0) &&
                UnityEngine.Time.realtimeSinceStartupAsDouble < screenshotDeadline) yield return null;
            Assert.That(File.Exists(screenshot), Is.True, "The actual Game View screenshot must finish before scene teardown.");
        }

        private static void FinishTravelForTest(TownLifeController resident)
        {
            NavMeshAgent agent = resident.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            NpcNavigator farm = resident.GetComponent<NpcNavigator>();
            TownResidentNavigator town = resident.GetComponent<TownResidentNavigator>();
            Vector3? target = farm != null && farm.IsMoving && farm.CurrentTarget != null ? farm.CurrentTarget.Position :
                town != null && town.IsMoving && town.CurrentTarget != null ? town.CurrentTarget.Position : (Vector3?)null;
            if (!target.HasValue || Vector3.Distance(agent.transform.position, target.Value) < .08f) return;
            Assert.That(NavMesh.SamplePosition(target.Value, out NavMeshHit hit, .3f, agent.areaMask), Is.True,
                "The real interaction point must exist on the baked NavMesh.");
            Assert.That(agent.Warp(hit.position), Is.True);
        }

        private static void Success(ActionResult result) => Assert.That(result.Succeeded, Is.True, result.Message);

        private sealed class MemoryStorage : ISaveGameStorage
        {
            public string Json { get; private set; }
            public string SavePath => "test-memory://sustainable-farm-scene";
            public ActionResult Write(string json)
            {
                Json = json;
                // Isolated diagnostic artifact: this normal test world contains
                // no model credentials and does not use the player's save path.
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Docs/Verification"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SustainableFarmScene-save.json"), json);
                return ActionResult.Success();
            }
            public ActionResult Read(out string json)
            {
                json = Json;
                return string.IsNullOrEmpty(Json) ? ActionResult.Failure(ActionFailureReason.InvalidState, "No test save.") : ActionResult.Success();
            }
            public ActionResult Delete() { Json = null; return ActionResult.Success(); }
        }
    }
}
