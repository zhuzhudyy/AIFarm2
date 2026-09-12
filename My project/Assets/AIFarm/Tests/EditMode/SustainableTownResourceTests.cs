using System;
using System.Linq;
using AIFarm.Activities;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Time;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class SustainableTownResourceTests
    {
        [TestCase(1d)]
        [TestCase(5d)]
        [TestCase(20d)]
        public void UnifiedClock_OneDayTakesTwelveMinutesAtOneTimes_AndPauseFreezes(double scale)
        {
            var clock = new GameClock(initialTimeScale: scale, gameSecondsPerRealSecond: 120d);
            Success(clock.Advance(720d / scale));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(GameClock.SecondsPerDay).Within(0.00001d));
            Assert.That(clock.Day, Is.EqualTo(2));
            Success(clock.Pause());
            Success(clock.Advance(999d));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(GameClock.SecondsPerDay));
        }

        [Test]
        public void NormalNineSeeds_CompleteThreeRounds_UsingWellAndWeedCompost_WithJsonReloadMidCrop()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(9, 9, 9);
            var clock = new GameClock(gameSecondsPerRealSecond: 120d);
            var simulation = new FarmSimulation(field, clock, new DemoMode());
            var activities = new TownActivityResources(clock, inventory);
            bool reloaded = false;

            for (int round = 1; round <= 3; round++)
            {
                if (round > 1) Refill(activities, simulation);
                foreach (FarmPlot plot in field.Plots) Success(plot.Sow(inventory));
                Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
                foreach (FarmPlot plot in field.Plots)
                {
                    Success(plot.Water(inventory));
                    Success(plot.Fertilize(inventory));
                }

                int hour = 0;
                while (field.Plots.Any(plot => plot.State != PlotState.Mature))
                {
                    Assert.That(++hour, Is.LessThan(80), "A cultivated two-day crop must not deadlock.");
                    Success(simulation.Advance(30d)); // exactly one simulated hour
                    double observed = clock.ElapsedGameSeconds;
                    int progress = field.GetPlot(1).GrowthProgress;
                    Success(simulation.AdvanceToClock());
                    Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(observed));
                    Assert.That(field.GetPlot(1).GrowthProgress, Is.EqualTo(progress), "Second observer must not double-grow crops.");

                    foreach (FarmPlot plot in field.Plots)
                    {
                        if (plot.HasWeeds) Success(plot.Weed(inventory));
                        if (plot.State == PlotState.Growing && plot.WaterLevel < FarmPlot.RequiredWaterLevel)
                        {
                            if (!inventory.Has(InventoryItem.Water)) Refill(activities, simulation);
                            Success(plot.Water(inventory));
                        }
                    }

                    if (round == 2 && hour == 20)
                    {
                        Assert.That(field.GetPlot(1).GrowthProgress, Is.InRange(1, 99));
                        CropRoundSnapshot snapshot = Capture(field, inventory, clock, simulation, activities);
                        string json = JsonUtility.ToJson(snapshot);
                        snapshot = JsonUtility.FromJson<CropRoundSnapshot>(json);
                        Restore(snapshot, out field, out inventory, out clock, out simulation, out activities);
                        Assert.That(field.GetPlot(1).GrowthProgress, Is.EqualTo(snapshot.plots[0].growthProgress));
                        Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
                        reloaded = true;
                    }
                }

                foreach (FarmPlot plot in field.Plots)
                {
                    Success(plot.Harvest(inventory));
                    Assert.That(plot.Harvest(inventory).Failed, Is.True, "Second harvest cannot settle twice.");
                }
                Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(9));
                Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(round * 9));
            }

            Assert.That(reloaded, Is.True);
            Assert.That(clock.ElapsedGameSeconds, Is.GreaterThan(6d * GameClock.SecondsPerDay));
        }

        [Test]
        public void ClockJumpAndHourlySteps_ApplyExactlyTheSameWaterAndGrowthConditions()
        {
            var jumpedField = new FarmField();
            var steppedField = new FarmField();
            var jumpedInventory = new FarmInventory(1, 1, 1);
            var steppedInventory = new FarmInventory(1, 1, 1);
            var jumpedClock = new GameClock(gameSecondsPerRealSecond: 120d);
            var steppedClock = new GameClock(gameSecondsPerRealSecond: 120d);
            var jumped = new FarmSimulation(jumpedField, jumpedClock, new DemoMode());
            var stepped = new FarmSimulation(steppedField, steppedClock, new DemoMode());
            Prepare(jumpedField.GetPlot(1), jumpedInventory);
            Prepare(steppedField.GetPlot(1), steppedInventory);
            Success(jumped.Advance(720));
            for (int hour = 0; hour < 24; hour++) Success(stepped.Advance(30));
            Assert.That(jumpedClock.ElapsedGameSeconds, Is.EqualTo(steppedClock.ElapsedGameSeconds));
            Assert.That(jumpedField.GetPlot(1).WaterLevel, Is.Zero);
            Assert.That(steppedField.GetPlot(1).WaterLevel, Is.Zero);
            Assert.That(jumpedField.GetPlot(1).GrowthProgress, Is.EqualTo(33));
            Assert.That(steppedField.GetPlot(1).GrowthProgress, Is.EqualTo(33));
            Assert.That(jumped.WaterDecayEventCount, Is.EqualTo(2));
            Assert.That(stepped.WaterDecayEventCount, Is.EqualTo(2));
        }

        [Test]
        public void CompostSupply_RequiresRealWeeds_AndCannotCreateFertilizerFromAnEmptyBin()
        {
            var clock = new GameClock();
            var inventory = new FarmInventory();
            var activities = new TownActivityResources(clock, inventory);
            Success(activities.TryReserve(TownActivityResources.WellId, ResidentIds.Amu));
            Success(clock.Advance(activities.Rules.supplyGameSeconds));
            Success(activities.RefillSupplies(ResidentIds.Amu));
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.EqualTo(18));
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.Zero);
            var plot = new FarmPlot();
            Success(plot.RestoreState(PlotState.Growing, CropType.Carrot, 1, true, true, false, 0));
            Success(plot.Weed(inventory));
            Assert.That(inventory.GetCount(InventoryItem.Compost), Is.EqualTo(1));
            Success(activities.TryReserve(TownActivityResources.WellId, ResidentIds.Xiaosui));
            Success(clock.Advance(activities.Rules.supplyGameSeconds));
            Success(activities.RefillSupplies(ResidentIds.Xiaosui));
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.EqualTo(1));
            Assert.That(inventory.GetCount(InventoryItem.Compost), Is.Zero);
            Assert.That(plot.Weed(inventory).Failed, Is.True);
        }

        [Test]
        public void Harvest_WhenSeedReturnCannotFit_IsAtomicAndLeavesCropMature()
        {
            var inventory = new FarmInventory(int.MaxValue, 1, 1);
            var plot = new FarmPlot();
            Success(plot.RestoreState(PlotState.Mature, CropType.Carrot, 1, true, false, true, 100));
            ActionResult result = plot.Harvest(inventory);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.CapacityExceeded));
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(int.MaxValue));
            Assert.That(plot.State, Is.EqualTo(PlotState.Mature));
        }

        [Test]
        public void HarvestAndImmediateReplant_DoNotReusePreviousGrowthTimers()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(1, 4, 2);
            var clock = new GameClock();
            var simulation = new FarmSimulation(field, clock,
                new DemoMode(waterDecayGameSeconds: 3, weedDelayGameSeconds: 1, maturityGameSeconds: 5));
            FarmPlot plot = field.GetPlot(1);
            Prepare(plot, inventory);
            Success(simulation.Advance(5));
            Success(plot.Harvest(inventory));
            Prepare(plot, inventory);
            Success(simulation.Advance(0.5));
            Assert.That(plot.GrowthProgress, Is.EqualTo(10));
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
        }

        [Test]
        public void Activities_AllResidentsCanFishPickAndRelease_ResourcesRegrowAfterSavedSimulationTime()
        {
            var clock = new GameClock();
            var inventory = new FarmInventory();
            var activities = new TownActivityResources(clock, inventory);
            ResidentId[] residents = ResidentIds.TownResidents.ToArray();
            foreach (ResidentId resident in residents)
            {
                Success(activities.TryReserve(TownActivityResources.FishingOneId, resident));
                Assert.That(activities.TryFish(TownActivityResources.FishingOneId, resident).Failed, Is.True);
                Success(clock.Advance(activities.Rules.fishingGameSeconds));
                Success(activities.TryFish(TownActivityResources.FishingOneId, resident));
                Assert.That(activities.TryFish(TownActivityResources.FishingOneId, resident).Failed, Is.True);
                Assert.That(activities.IsAvailable(TownActivityResources.FishingOneId), Is.True);
            }
            Assert.That(inventory.GetCount(InventoryItem.Fish), Is.EqualTo(residents.Length));
            Success(activities.ConsumeFood());
            Assert.That(inventory.GetCount(InventoryItem.Fish), Is.EqualTo(residents.Length - 1));

            string tree = TownActivityResources.OrchardIds[0];
            for (int i = 0; i < activities.Rules.fruitsPerTree; i++)
            {
                Success(activities.TryReserve(tree, residents[i]));
                Assert.That(activities.TryReserve(tree, residents[(i + 1) % residents.Length]).Failed, Is.True);
                Success(clock.Advance(activities.Rules.pickingGameSeconds));
                Success(activities.TryPickFruit(tree, residents[i]));
            }
            Assert.That(activities.GetFruitRemaining(tree), Is.Zero);
            Assert.That(activities.IsAvailable(tree), Is.False);
            ActivityResourceSnapshot[] saved = activities.Capture();
            activities = new TownActivityResources(clock, inventory);
            Success(activities.Restore(saved));
            Success(clock.Pause());
            Success(clock.Advance(activities.Rules.fruitRegrowthGameSeconds));
            Assert.That(activities.GetFruitRemaining(tree), Is.Zero);
            Success(clock.Resume());
            Success(clock.Advance(activities.Rules.fruitRegrowthGameSeconds - 1));
            Assert.That(activities.GetFruitRemaining(tree), Is.Zero);
            Success(clock.Advance(1));
            Assert.That(activities.GetFruitRemaining(tree), Is.EqualTo(activities.Rules.fruitsPerTree));
            Success(activities.TryReserve(tree, residents[0]));
            activities.ReleaseAll(residents[0]);
            Assert.That(activities.IsAvailable(tree), Is.True);
        }

        private static void Refill(TownActivityResources resources, FarmSimulation simulation)
        {
            Success(resources.TryReserve(TownActivityResources.WellId, ResidentIds.Yaya));
            Success(resources.BeginInteraction(TownActivityResources.WellId, ResidentIds.Yaya));
            Success(simulation.Advance(resources.Rules.supplyGameSeconds / 120d));
            Success(resources.RefillSupplies(ResidentIds.Yaya));
        }

        private static void Prepare(FarmPlot plot, FarmInventory inventory)
        {
            Success(plot.Sow(inventory)); Success(plot.Water(inventory)); Success(plot.Fertilize(inventory));
            Success(plot.IntroduceWeeds()); Success(plot.Weed(inventory));
        }

        private static CropRoundSnapshot Capture(FarmField field, FarmInventory inventory, GameClock clock,
            FarmSimulation simulation, TownActivityResources activities)
        {
            simulation.CaptureRuntimeState(out double[] water, out double[] weeds, out double[] growth, out bool[] decayed);
            return new CropRoundSnapshot
            {
                elapsed = clock.ElapsedGameSeconds,
                plots = field.Plots.Select(p => new PlotSaveData { plotNumber = p.PlotNumber, state = (int)p.State,
                    hasCrop = p.Crop.HasValue, crop = (int)(p.Crop ?? CropType.Carrot), waterLevel = p.WaterLevel,
                    isFertilized = p.IsFertilized, hasWeeds = p.HasWeeds, hasBeenWeeded = p.HasBeenWeeded,
                    growthProgress = p.GrowthProgress }).ToArray(),
                inventory = new InventorySaveData { carrotSeeds = inventory.GetCount(InventoryItem.CarrotSeed),
                    water = inventory.GetCount(InventoryItem.Water), fertilizer = inventory.GetCount(InventoryItem.Fertilizer),
                    carrots = inventory.GetCount(InventoryItem.Carrot), compost = inventory.GetCount(InventoryItem.Compost) },
                simulation = new SimulationSaveData { waterElapsed = water, weedElapsed = weeds, growthElapsed = growth,
                    waterHasDecayed = decayed, waterDecayEventCount = simulation.WaterDecayEventCount,
                    weedEventCount = simulation.WeedEventCount }, activities = activities.Capture()
            };
        }

        private static void Restore(CropRoundSnapshot saved, out FarmField field, out FarmInventory inventory,
            out GameClock clock, out FarmSimulation simulation, out TownActivityResources activities)
        {
            clock = new GameClock(saved.elapsed, gameSecondsPerRealSecond: 120d);
            inventory = new FarmInventory(saved.inventory.carrotSeeds, saved.inventory.water,
                saved.inventory.fertilizer, saved.inventory.carrots, compost: saved.inventory.compost);
            field = new FarmField();
            foreach (PlotSaveData p in saved.plots)
                Success(field.GetPlot(p.plotNumber).RestoreState((PlotState)p.state, p.hasCrop ? (CropType?)p.crop : null,
                    p.waterLevel, p.isFertilized, p.hasWeeds, p.hasBeenWeeded, p.growthProgress));
            simulation = new FarmSimulation(field, clock, new DemoMode());
            Success(simulation.RestoreRuntimeState(saved.simulation.waterDecayEventCount, saved.simulation.weedEventCount,
                saved.simulation.waterElapsed, saved.simulation.weedElapsed, saved.simulation.growthElapsed, saved.simulation.waterHasDecayed));
            activities = new TownActivityResources(clock, inventory);
            Success(activities.Restore(saved.activities));
        }

        private static void Success(ActionResult result) => Assert.That(result.Succeeded, Is.True, result.Message);

        [Serializable]
        public sealed class CropRoundSnapshot
        {
            public double elapsed;
            public PlotSaveData[] plots;
            public InventorySaveData inventory;
            public SimulationSaveData simulation;
            public ActivityResourceSnapshot[] activities;
        }
    }
}
