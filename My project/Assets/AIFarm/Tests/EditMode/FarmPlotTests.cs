using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class FarmPlotTests
    {
        [Test]
        public void FarmField_ContainsExactlyNineEmptyPlotsInDeterministicOrder()
        {
            var field = new FarmField();

            Assert.That(field.Plots, Has.Count.EqualTo(9));
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                FarmPlot plot = field.GetPlot(plotNumber);
                Assert.That(plot, Is.SameAs(field.Plots[plotNumber - 1]));
                Assert.That(plot.PlotNumber, Is.EqualTo(plotNumber));
                Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            }
        }

        [TestCase(0)]
        [TestCase(10)]
        public void FarmField_GetPlotOutsideOneToNine_Throws(int plotNumber)
        {
            var field = new FarmField();

            Assert.Throws<System.ArgumentOutOfRangeException>(() => field.GetPlot(plotNumber));
        }

        [Test]
        public void Sow_EmptyPlot_ConsumesOneSeed()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 2);

            ActionResult result = plot.Sow(inventory);

            AssertSuccess(result);
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.Crop, Is.EqualTo(CropType.Carrot));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
        }

        [Test]
        public void Sow_NonEmptyPlot_FailsWithoutChangingStateOrInventory()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 2);
            AssertSuccess(plot.Sow(inventory));

            ActionResult result = plot.Sow(inventory);

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.Crop, Is.EqualTo(CropType.Carrot));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
        }

        [Test]
        public void Water_SownPlot_IncreasesWaterAndConsumesOneWater()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 2);
            AssertSuccess(plot.Sow(inventory));

            ActionResult result = plot.Water(inventory);

            AssertSuccess(result);
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.EqualTo(1));
        }

        [Test]
        public void Water_WhenAlreadyWatered_FailsWithoutConsumingWater()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 2);
            AssertSuccess(plot.Sow(inventory));
            AssertSuccess(plot.Water(inventory));

            ActionResult result = plot.Water(inventory);

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.EqualTo(1));
        }

        [Test]
        public void Fertilize_Twice_SecondAttemptFailsWithoutConsumingFertilizer()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 2);
            AssertSuccess(plot.Sow(inventory));
            AssertSuccess(plot.Water(inventory));
            AssertSuccess(plot.Fertilize(inventory));

            ActionResult result = plot.Fertilize(inventory);

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.IsFertilized, Is.True);
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.EqualTo(1));
        }

        [Test]
        public void Fertilize_UnwateredPlot_FailsWithoutConsumingFertilizer()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
            AssertSuccess(plot.Sow(inventory));

            ActionResult result = plot.Fertilize(inventory);

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.IsFertilized, Is.False);
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.EqualTo(1));
        }

        [Test]
        public void Weed_WhenWeedsArePresent_RemovesWeeds()
        {
            var plot = new FarmPlot();
            var inventory = CreatePreparedInventory();
            PrepareThroughFertilizing(plot, inventory);
            AssertSuccess(plot.IntroduceWeeds());

            ActionResult result = plot.Weed();

            AssertSuccess(result);
            Assert.That(plot.HasWeeds, Is.False);
            Assert.That(plot.HasBeenWeeded, Is.True);
        }

        [Test]
        public void Weed_WhenNoWeedsExist_FailsWithoutChangingState()
        {
            var plot = new FarmPlot();
            var inventory = CreatePreparedInventory();
            PrepareThroughFertilizing(plot, inventory);

            ActionResult result = plot.Weed();

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.HasWeeds, Is.False);
            Assert.That(plot.HasBeenWeeded, Is.False);
        }

        [Test]
        public void AdvanceGrowth_OnlySucceedsAfterAllGrowthConditionsAreMet()
        {
            var plot = new FarmPlot();
            var inventory = CreatePreparedInventory();
            AssertSuccess(plot.Sow(inventory));

            AssertGrowthBlockedWithoutMutation(plot);
            AssertSuccess(plot.Water(inventory));
            AssertGrowthBlockedWithoutMutation(plot);
            AssertSuccess(plot.Fertilize(inventory));
            AssertGrowthBlockedWithoutMutation(plot);
            AssertSuccess(plot.IntroduceWeeds());
            AssertGrowthBlockedWithoutMutation(plot);
            AssertSuccess(plot.Weed());

            ActionResult result = plot.AdvanceGrowth(40);

            AssertSuccess(result);
            Assert.That(plot.GrowthProgress, Is.EqualTo(40));
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
        }

        [Test]
        public void Harvest_BeforeMaturity_FailsWithoutChangingStateOrInventory()
        {
            var plot = new FarmPlot();
            var inventory = CreatePreparedInventory();
            PrepareForGrowth(plot, inventory);
            AssertSuccess(plot.AdvanceGrowth(FarmPlot.RequiredGrowth - 1));

            ActionResult result = plot.Harvest(inventory);

            AssertFailure(result, ActionFailureReason.InvalidState);
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.GrowthProgress, Is.EqualTo(FarmPlot.RequiredGrowth - 1));
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(0));
        }

        [Test]
        public void Harvest_MatureCrop_AddsCarrotAndClearsPlot()
        {
            var plot = new FarmPlot();
            var inventory = CreatePreparedInventory();
            PrepareForGrowth(plot, inventory);
            AssertSuccess(plot.AdvanceGrowth(FarmPlot.RequiredGrowth));
            Assert.That(plot.State, Is.EqualTo(PlotState.Mature));

            ActionResult result = plot.Harvest(inventory);

            AssertSuccess(result);
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(1));
            Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            Assert.That(plot.Crop, Is.Null);
            Assert.That(plot.WaterLevel, Is.Zero);
            Assert.That(plot.IsFertilized, Is.False);
            Assert.That(plot.HasWeeds, Is.False);
            Assert.That(plot.HasBeenWeeded, Is.False);
            Assert.That(plot.GrowthProgress, Is.Zero);
        }

        [Test]
        public void Sow_WithoutSeed_FailsWithoutChangingPlotOrInventory()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory();

            ActionResult result = plot.Sow(inventory);

            AssertFailure(result, ActionFailureReason.InsufficientResource);
            Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            Assert.That(plot.Crop, Is.Null);
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
        }

        [Test]
        public void Water_WithoutWater_FailsWithoutChangingPlotOrInventory()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1);
            AssertSuccess(plot.Sow(inventory));

            ActionResult result = plot.Water(inventory);

            AssertFailure(result, ActionFailureReason.InsufficientResource);
            Assert.That(plot.WaterLevel, Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.Zero);
        }

        [Test]
        public void Fertilize_WithoutFertilizer_FailsWithoutChangingPlotOrInventory()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1);
            AssertSuccess(plot.Sow(inventory));
            AssertSuccess(plot.Water(inventory));

            ActionResult result = plot.Fertilize(inventory);

            AssertFailure(result, ActionFailureReason.InsufficientResource);
            Assert.That(plot.IsFertilized, Is.False);
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.Zero);
        }

        [Test]
        public void Harvest_WhenCarrotInventoryIsFull_FailsWithoutClearingPlot()
        {
            var plot = new FarmPlot();
            var inventory = new FarmInventory(
                carrotSeeds: 1,
                water: 1,
                fertilizer: 1,
                carrots: int.MaxValue);
            PrepareForGrowth(plot, inventory);
            AssertSuccess(plot.AdvanceGrowth(FarmPlot.RequiredGrowth));

            ActionResult result = plot.Harvest(inventory);

            AssertFailure(result, ActionFailureReason.CapacityExceeded);
            Assert.That(plot.State, Is.EqualTo(PlotState.Mature));
            Assert.That(plot.Crop, Is.EqualTo(CropType.Carrot));
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(int.MaxValue));
        }

        private static FarmInventory CreatePreparedInventory()
        {
            return new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
        }

        private static void PrepareThroughFertilizing(FarmPlot plot, FarmInventory inventory)
        {
            AssertSuccess(plot.Sow(inventory));
            AssertSuccess(plot.Water(inventory));
            AssertSuccess(plot.Fertilize(inventory));
        }

        private static void PrepareForGrowth(FarmPlot plot, FarmInventory inventory)
        {
            PrepareThroughFertilizing(plot, inventory);
            AssertSuccess(plot.IntroduceWeeds());
            AssertSuccess(plot.Weed());
        }

        private static void AssertGrowthBlockedWithoutMutation(FarmPlot plot)
        {
            int progressBefore = plot.GrowthProgress;
            ActionResult result = plot.AdvanceGrowth(10);

            AssertFailure(result, ActionFailureReason.MissingGrowthCondition);
            Assert.That(plot.GrowthProgress, Is.EqualTo(progressBefore));
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
        }

        private static void AssertSuccess(ActionResult result)
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.None));
        }

        private static void AssertFailure(ActionResult result, ActionFailureReason expectedReason)
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(expectedReason), result.Message);
        }
    }
}
