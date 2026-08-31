using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Time;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class OfflinePlanningTests
    {
        private static readonly string[] SupportedChineseGoals =
        {
            "把地种满胡萝卜并照顾到收获。",
            "帮我种胡萝卜，记得浇水施肥除草，成熟后收掉。",
            "今天把所有空地种上并全部收成。",
            "种满之后全部收掉。",
            "把这块地照顾好。"
        };

        [TestCaseSource(nameof(SupportedChineseGoals))]
        public void LocalIntentInterpreter_ConvertsRequiredChinesePhrases(string input)
        {
            var interpreter = new LocalIntentInterpreter();

            ActionResult result = interpreter.TryInterpret(input, out FarmGoalSpec goal);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(goal, Is.Not.Null);
            Assert.That(goal.Crop, Is.EqualTo(CropType.Carrot));
            Assert.That(goal.TargetPlotNumbers, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
            Assert.That(goal.RequiresSowing, Is.True);
            Assert.That(goal.RequiresWatering, Is.True);
            Assert.That(goal.RequiresFertilizing, Is.True);
            Assert.That(goal.RequiresWeeding, Is.True);
            Assert.That(goal.RequiresHarvesting, Is.True);
        }

        [Test]
        public void LocalIntentInterpreter_RecognizesAcceptanceCommand_AndRejectsUnrelatedText()
        {
            var interpreter = new LocalIntentInterpreter();

            Assert.That(
                interpreter.TryInterpret(
                    "请把这块 3×3 农田全部种上胡萝卜，并完成浇水、施肥、除草和收获。",
                    out FarmGoalSpec goal).Succeeded,
                Is.True);
            Assert.That(goal, Is.Not.Null);

            ActionResult rejected = interpreter.TryInterpret("去镇上买一把锄头", out FarmGoalSpec unsupported);
            Assert.That(rejected.Failed, Is.True);
            Assert.That(rejected.FailureReason, Is.EqualTo(ActionFailureReason.UnsupportedIntent));
            Assert.That(unsupported, Is.Null);
        }

        [Test]
        public void DeterministicPlanner_UsesDocumentedPriorityAndPlotOrder()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 9, water: 9, fertilizer: 9);
            var clock = new GameClock();
            var world = new WorldStateQuery(field, inventory, clock);
            var mode = new DemoMode();
            var planner = new DeterministicFarmPlanner(world, mode);
            FarmGoalSpec goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            var harvested = new HashSet<int>();

            AssertNext<SowAction>(planner, goal, harvested, 1);
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                Assert.That(field.GetPlot(plotNumber).Sow(inventory).Succeeded, Is.True);
            }

            AssertNext<WaterAction>(planner, goal, harvested, 1);
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                Assert.That(field.GetPlot(plotNumber).Water(inventory).Succeeded, Is.True);
            }

            AssertNext<FertilizeAction>(planner, goal, harvested, 1);
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                Assert.That(field.GetPlot(plotNumber).Fertilize(inventory).Succeeded, Is.True);
            }

            Assert.That(field.GetPlot(3).IntroduceWeeds().Succeeded, Is.True);
            AssertNext<WeedAction>(planner, goal, harvested, 3);
            Assert.That(field.GetPlot(3).Weed().Succeeded, Is.True);

            Assert.That(field.GetPlot(4).IntroduceWeeds().Succeeded, Is.True);
            Assert.That(field.GetPlot(4).Weed().Succeeded, Is.True);
            Assert.That(field.GetPlot(4).AdvanceGrowth(FarmPlot.RequiredGrowth).Succeeded, Is.True);
            AssertNext<HarvestAction>(planner, goal, harvested, 4);

            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                harvested.Add(plotNumber);
            }

            Assert.That(planner.DecideNext(goal, harvested, out FarmPlanDecision complete).Succeeded, Is.True);
            Assert.That(complete.Kind, Is.EqualTo(FarmPlanDecisionKind.GoalCompleted));
            Assert.That(complete.Action, Is.Null);
        }

        [Test]
        public void DeterministicPlanner_WhenNoActionIsReady_WaitsAndRechecks()
        {
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 9, water: 9, fertilizer: 9);
            var clock = new GameClock();
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                FarmPlot plot = field.GetPlot(plotNumber);
                Assert.That(plot.Sow(inventory).Succeeded, Is.True);
                Assert.That(plot.Water(inventory).Succeeded, Is.True);
                Assert.That(plot.Fertilize(inventory).Succeeded, Is.True);
            }

            var planner = new DeterministicFarmPlanner(
                new WorldStateQuery(field, inventory, clock),
                new DemoMode());

            Assert.That(
                planner.DecideNext(
                    FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                    new HashSet<int>(),
                    out FarmPlanDecision decision).Succeeded,
                Is.True);
            Assert.That(decision.Kind, Is.EqualTo(FarmPlanDecisionKind.WaitAndRecheck));
            Assert.That(decision.Action, Is.TypeOf<WaitAction>());
        }

        [Test]
        public void DeterministicPlanner_MissingRequiredInventory_FailsWithoutAction()
        {
            var field = new FarmField();
            var planner = new DeterministicFarmPlanner(
                new WorldStateQuery(field, new FarmInventory(), new GameClock()),
                new DemoMode());

            ActionResult result = planner.DecideNext(
                FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                new HashSet<int>(),
                out FarmPlanDecision decision);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InsufficientResource));
            Assert.That(decision.Action, Is.Null);
            Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
        }

        private static void AssertNext<TAction>(
            DeterministicFarmPlanner planner,
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvested,
            int expectedPlotNumber)
            where TAction : INpcAction
        {
            Assert.That(planner.DecideNext(goal, harvested, out FarmPlanDecision decision).Succeeded, Is.True);
            Assert.That(decision.Kind, Is.EqualTo(FarmPlanDecisionKind.ExecuteAction));
            Assert.That(decision.Action, Is.TypeOf<TAction>());
            Assert.That(decision.Action.TargetPlotNumber, Is.EqualTo(expectedPlotNumber));
        }
    }
}
