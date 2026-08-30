using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Time;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class NpcActionTests
    {
        [Test]
        public void AtomicActions_CheckWithoutMutation_ThenCommitExpectedState()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
            var clock = new GameClock(initialTimeScale: 2d);
            var context = new NpcActionContext(field, inventory, clock);
            FarmPlot plot = field.GetPlot(1);

            var move = new MoveToPlotAction(1, 0.01f);
            Assert.That(move.CheckPreconditions(context).Succeeded, Is.True);
            Assert.That(move.Complete(context).Succeeded, Is.True);
            Assert.That(plot.State, Is.EqualTo(PlotState.Empty));

            var sow = new SowAction(1, 0.01f);
            Assert.That(sow.CheckPreconditions(context).Succeeded, Is.True);
            Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
            Assert.That(sow.Complete(context).Succeeded, Is.True);

            var water = new WaterAction(1, 0.01f);
            Assert.That(water.Complete(context).Succeeded, Is.True);

            var fertilize = new FertilizeAction(1, 0.01f);
            Assert.That(fertilize.Complete(context).Succeeded, Is.True);

            Assert.That(plot.IntroduceWeeds().Succeeded, Is.True);
            var weed = new WeedAction(1, 0.01f);
            Assert.That(weed.Complete(context).Succeeded, Is.True);

            Assert.That(plot.AdvanceGrowth(FarmPlot.RequiredGrowth).Succeeded, Is.True);
            var harvest = new HarvestAction(1, 0.01f);
            Assert.That(harvest.Complete(context).Succeeded, Is.True);
            Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(1));

            var wait = new WaitAction(0.5f);
            Assert.That(wait.CheckPreconditions(context).Succeeded, Is.True);
            Assert.That(clock.ElapsedGameSeconds, Is.Zero);
            Assert.That(wait.Complete(context).Succeeded, Is.True);
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(1d));
        }

        [Test]
        public void ActionQueue_PreservesInsertionOrder_AndRejectsNull()
        {
            var queue = new ActionQueue();
            var move = new MoveToPlotAction(1);
            var sow = new SowAction(1);

            Assert.That(queue.Enqueue(null).Failed, Is.True);
            Assert.That(queue.Enqueue(move).Succeeded, Is.True);
            Assert.That(queue.Enqueue(sow).Succeeded, Is.True);
            Assert.That(queue.Count, Is.EqualTo(2));

            Assert.That(queue.TryDequeue(out INpcAction first).Succeeded, Is.True);
            Assert.That(first, Is.SameAs(move));
            Assert.That(queue.TryDequeue(out INpcAction second).Succeeded, Is.True);
            Assert.That(second, Is.SameAs(sow));
            Assert.That(queue.IsEmpty, Is.True);
        }

        [TestCase("move 1", typeof(MoveToPlotAction), 1)]
        [TestCase("sow plot 2", typeof(SowAction), 2)]
        [TestCase("播种3", typeof(SowAction), 3)]
        [TestCase("浇水 4号地", typeof(WaterAction), 4)]
        [TestCase("施肥 5", typeof(FertilizeAction), 5)]
        [TestCase("除草 6", typeof(WeedAction), 6)]
        [TestCase("收获 7", typeof(HarvestAction), 7)]
        public void CommandParser_ParsesOneAtomicPlotAction(
            string command,
            System.Type expectedType,
            int expectedPlotNumber)
        {
            Assert.That(NpcActionCommandParser.TryParse(command, out INpcAction action).Succeeded, Is.True);
            Assert.That(action.GetType(), Is.EqualTo(expectedType));
            Assert.That(action.TargetPlotNumber, Is.EqualTo(expectedPlotNumber));
        }

        [Test]
        public void CommandParser_ParsesWait_AndRejectsHighLevelPlan()
        {
            Assert.That(NpcActionCommandParser.TryParse("等待 0.5", out INpcAction wait).Succeeded, Is.True);
            Assert.That(wait, Is.TypeOf<WaitAction>());
            Assert.That(wait.DurationSeconds, Is.EqualTo(0.5f));

            Assert.That(
                NpcActionCommandParser.TryParse("完成整块农田", out INpcAction unsupported).Failed,
                Is.True);
            Assert.That(unsupported, Is.Null);
        }
    }
}
