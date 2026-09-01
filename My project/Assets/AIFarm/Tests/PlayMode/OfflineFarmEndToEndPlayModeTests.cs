using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Time;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.PlayMode
{
    public sealed class OfflineFarmEndToEndPlayModeTests
    {
        private GameObject testRoot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (testRoot != null)
            {
                Object.Destroy(testRoot);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ChineseGoal_CompletesNinePlotLifecycleOffline()
        {
            testRoot = new GameObject("OfflineFarmEndToEndTestRoot");
            var field = new FarmField();
            var inventory = new FarmInventory(carrotSeeds: 9, water: 9, fertilizer: 9);
            var clock = new GameClock(initialTimeScale: 120d);
            var mode = new DemoMode(
                recommendedTimeScale: 120d,
                waterDecayGameSeconds: 2d,
                weedDelayGameSeconds: 4d,
                maturityGameSeconds: 120d,
                sowActionSeconds: 0.01f,
                fertilizeActionSeconds: 0.01f,
                waterActionSeconds: 0.01f,
                weedActionSeconds: 0.01f,
                harvestActionSeconds: 0.01f,
                waitActionSeconds: 0.02f);
            var events = new WorldEventLog();
            var simulation = new FarmSimulation(field, clock, mode, events);
            var context = new NpcActionContext(field, inventory, clock, simulation, events);

            PlotInteractionPoint[] points = CreateInteractionPoints();
            GameObject npc = new GameObject("OfflineNpc");
            npc.transform.SetParent(testRoot.transform, false);
            Transform visual = new GameObject("OfflineNpcVisual").transform;
            visual.SetParent(npc.transform, false);

            NpcNavigator navigator = npc.AddComponent<NpcNavigator>();
            Assert.That(
                navigator.Configure(
                    agent: null,
                    points: points,
                    requireNavMesh: false,
                    movementSpeed: 100f).Succeeded,
                Is.True);

            BlockoutActionFeedback feedback = npc.AddComponent<BlockoutActionFeedback>();
            Assert.That(feedback.Configure(visual, null, null).Succeeded, Is.True);

            NpcPlanExecutor executor = npc.AddComponent<NpcPlanExecutor>();
            Assert.That(executor.Initialize(context, navigator, feedback).Succeeded, Is.True);
            var history = new List<INpcAction>();
            executor.ActionCompleted += (action, result) => history.Add(action);

            ReplanController controller = npc.AddComponent<ReplanController>();
            Assert.That(controller.Initialize(context, executor, mode).Succeeded, Is.True);

            GameObject hudObject = new GameObject("OfflineDemoHud");
            hudObject.transform.SetParent(testRoot.transform, false);
            DemoHud hud = hudObject.AddComponent<DemoHud>();
            Text recentMemoriesText = new GameObject("RecentMemoriesText").AddComponent<Text>();
            recentMemoriesText.transform.SetParent(hudObject.transform, false);
            Text recentReflectionsText = new GameObject("RecentReflectionsText").AddComponent<Text>();
            recentReflectionsText.transform.SetParent(hudObject.transform, false);
            hud.Configure(
                gameBootstrap: null,
                timeLabel: null,
                inventoryLabel: null,
                goalLabel: null,
                actionLabel: null,
                input: null,
                button: null,
                executor: executor,
                controller: controller,
                memoriesLabel: recentMemoriesText,
                reflectionsLabel: recentReflectionsText);

            ActionResult submitted = hud.SubmitCommand("把地种满胡萝卜并照顾到收获。");
            Assert.That(submitted.Succeeded, Is.True, submitted.Message);
            Assert.That(controller.Status, Is.EqualTo(ReplanStatus.Running));

            float deadline = UnityEngine.Time.realtimeSinceStartup + 12f;
            while (controller.Status == ReplanStatus.Running &&
                UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                Assert.That(simulation.Advance(UnityEngine.Time.deltaTime).Succeeded, Is.True);
                yield return null;
            }

            Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.LessThan(deadline), "Offline goal timed out.");
            Assert.That(controller.Status, Is.EqualTo(ReplanStatus.Completed), controller.LastFailureReason);
            Assert.That(controller.ActiveGoal, Is.Null,
                "A completed farm command must not remain a mutable player instruction.");
            Assert.That(controller.HarvestedPlotCount, Is.EqualTo(FarmField.PlotCount));
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
            Assert.That(executor.IsBusy, Is.False);
            Assert.That(simulation.WeedEventCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(simulation.WaterDecayEventCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(clock.ElapsedGameSeconds, Is.GreaterThan(0d));

            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(9));
            foreach (FarmPlot plot in field.Plots)
            {
                Assert.That(plot.State, Is.EqualTo(PlotState.Empty));
            }

            List<INpcAction> farmingActions = history.Where(action => !(action is WaitAction)).ToList();
            Assert.That(farmingActions, Has.Count.EqualTo(45));
            AssertPhase<SowAction>(farmingActions, startIndex: 0);
            AssertPhase<WaterAction>(farmingActions, startIndex: 9);
            AssertPhase<FertilizeAction>(farmingActions, startIndex: 18);
            AssertPhase<WeedAction>(farmingActions, startIndex: 27);
            AssertPhase<HarvestAction>(farmingActions, startIndex: 36);
            Assert.That(history.Any(action => action is WaitAction), Is.True);
            Assert.That(events.Entries, Has.Count.LessThanOrEqualTo(10));
            Assert.That(events.Entries.Last().Kind, Is.EqualTo(WorldEventKind.GoalCompleted));
            Assert.That(
                events.GetVisibleEntries(ResidentIds.Yaya).Last().Kind,
                Is.EqualTo(WorldEventKind.GoalCompleted));
            Assert.That(
                events.GetVisibleEntries(ResidentIds.Xiaosui),
                Is.Empty,
                "小穗不在农场时不能自动获知芽芽的收获结果。");
            Assert.That(controller.CurrentMood, Is.EqualTo(NpcMood.Proud));
            Assert.That(controller.CurrentEmoji, Is.Not.Empty);
            Assert.That(controller.Persona.Name, Is.EqualTo("芽芽"));
            Assert.That(controller.CompletedReflectionCount, Is.EqualTo(1));
            Assert.That(controller.LatestReflection, Is.Not.Null);
            Assert.That(controller.LatestReflection.Text, Does.Contain("杂草"));
            Assert.That(controller.RecentReflections, Has.Count.EqualTo(1));
            Assert.That(controller.RecentMemories, Is.Not.Empty);
            yield return null;
            Assert.That(recentMemoriesText.text, Does.Contain("重要性"));
            Assert.That(recentReflectionsText.text, Does.Contain(controller.LatestReflection.Text));
            NpcExpressionTrigger[] expectedExpressionTriggers =
            {
                NpcExpressionTrigger.CommandAccepted,
                NpcExpressionTrigger.SowingStarted,
                NpcExpressionTrigger.WaterNeeded,
                NpcExpressionTrigger.WeedsFound,
                NpcExpressionTrigger.WaitingForGrowth,
                NpcExpressionTrigger.HarvestStarted,
                NpcExpressionTrigger.GoalCompleted
            };
            foreach (NpcExpressionTrigger trigger in expectedExpressionTriggers)
            {
                Assert.That(
                    controller.RecentExpressions.Any(expression => expression.Trigger == trigger),
                    Is.True,
                    $"Missing offline expression trigger: {trigger}.");
            }
        }

        private PlotInteractionPoint[] CreateInteractionPoints()
        {
            var points = new PlotInteractionPoint[FarmField.PlotCount];
            for (int plotNumber = 1; plotNumber <= FarmField.PlotCount; plotNumber++)
            {
                GameObject target = new GameObject($"Target_{plotNumber:00}");
                target.transform.SetParent(testRoot.transform, false);
                target.transform.position = new Vector3((plotNumber - 1) * 0.03f, 0f, 0.1f);

                GameObject pointObject = new GameObject($"Interaction_{plotNumber:00}");
                pointObject.transform.SetParent(testRoot.transform, false);
                pointObject.transform.position = new Vector3((plotNumber - 1) * 0.03f, 0f, 0f);
                PlotInteractionPoint point = pointObject.AddComponent<PlotInteractionPoint>();
                Assert.That(point.Configure(plotNumber, target.transform).Succeeded, Is.True);
                points[plotNumber - 1] = point;
            }

            return points;
        }

        private static void AssertPhase<TAction>(IReadOnlyList<INpcAction> actions, int startIndex)
            where TAction : INpcAction
        {
            for (int offset = 0; offset < FarmField.PlotCount; offset++)
            {
                INpcAction action = actions[startIndex + offset];
                Assert.That(action, Is.TypeOf<TAction>());
                Assert.That(action.TargetPlotNumber, Is.EqualTo(offset + 1));
            }
        }
    }
}
