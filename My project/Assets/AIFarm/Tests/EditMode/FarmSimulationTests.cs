using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Time;
using NUnit.Framework;
using System.Linq;

namespace AIFarm.Tests.EditMode
{
    public sealed class FarmSimulationTests
    {
        [Test]
        public void DemoMode_ProducesWaterDecayWeedsAndMaturityInOrder()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
            var clock = new GameClock(initialTimeScale: 1d);
            var mode = new DemoMode(
                recommendedTimeScale: 1d,
                waterDecayGameSeconds: 2d,
                weedDelayGameSeconds: 3d,
                maturityGameSeconds: 4d);
            var events = new WorldEventLog();
            var simulation = new FarmSimulation(field, clock, mode, events);
            FarmPlot plot = field.GetPlot(1);
            Assert.That(plot.Sow(inventory).Succeeded, Is.True);
            Assert.That(plot.Water(inventory).Succeeded, Is.True);
            Assert.That(plot.Fertilize(inventory).Succeeded, Is.True);

            Assert.That(simulation.Advance(1.99d).Succeeded, Is.True);
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(plot.HasWeeds, Is.False);

            Assert.That(simulation.Advance(0.01d).Succeeded, Is.True);
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.RequiredWaterLevel));
            Assert.That(simulation.WaterDecayEventCount, Is.EqualTo(1));
            Assert.That(
                events.Entries.Any(entry => entry.Kind == WorldEventKind.MoistureChanged),
                Is.True);

            Assert.That(simulation.Advance(1d).Succeeded, Is.True);
            Assert.That(plot.HasWeeds, Is.True);
            Assert.That(simulation.WeedEventCount, Is.EqualTo(1));
            Assert.That(
                events.Entries.Any(entry => entry.Kind == WorldEventKind.WeedsAppeared),
                Is.True);
            Assert.That(plot.Weed().Succeeded, Is.True);

            Assert.That(simulation.Advance(3.99d).Succeeded, Is.True);
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.GrowthProgress, Is.LessThan(FarmPlot.RequiredGrowth));
            Assert.That(simulation.Advance(0.01d).Succeeded, Is.True);
            Assert.That(plot.State, Is.EqualTo(PlotState.Mature));
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.RequiredWaterLevel));
            Assert.That(simulation.WaterDecayEventCount, Is.EqualTo(1));
            Assert.That(
                events.Entries.Any(entry => entry.Kind == WorldEventKind.CropMatured),
                Is.True);
        }

        [Test]
        public void FarmSimulation_PausedClockDoesNotChangeWorld()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
            var clock = new GameClock(initialTimeScale: 100d);
            var simulation = new FarmSimulation(field, clock, new DemoMode());
            FarmPlot plot = field.GetPlot(1);
            Assert.That(plot.Sow(inventory).Succeeded, Is.True);
            Assert.That(plot.Water(inventory).Succeeded, Is.True);
            Assert.That(plot.Fertilize(inventory).Succeeded, Is.True);
            Assert.That(clock.Pause().Succeeded, Is.True);

            Assert.That(simulation.Advance(100d).Succeeded, Is.True);

            Assert.That(clock.ElapsedGameSeconds, Is.Zero);
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(plot.HasWeeds, Is.False);
            Assert.That(plot.GrowthProgress, Is.Zero);
        }
    }
}
