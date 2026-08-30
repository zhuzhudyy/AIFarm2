using System.Collections;
using System.Text.RegularExpressions;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Time;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AIFarm.Tests.PlayMode
{
    public sealed class NpcPlanExecutorPlayModeTests
    {
        private GameObject testRoot;
        private GameObject npcObject;
        private Transform npcVisual;
        private FarmField field;
        private FarmInventory inventory;
        private GameClock clock;
        private NpcPlanExecutor executor;

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
        public IEnumerator HudSubmit_WhileActionRunning_QueuesAndExecutesNextCommand()
        {
            CreateExecutor(
                seedCount: 1,
                interactionPosition: new Vector3(0.5f, 0f, 0f),
                movementSpeed: 10f,
                waterCount: 1);
            GameObject hudObject = new GameObject("TestDemoHud");
            hudObject.transform.SetParent(testRoot.transform, false);
            DemoHud hud = hudObject.AddComponent<DemoHud>();

            GameObject inputObject = new GameObject("TestCommandInput", typeof(RectTransform));
            inputObject.transform.SetParent(hudObject.transform, false);
            InputField input = inputObject.AddComponent<InputField>();

            GameObject buttonObject = new GameObject("TestSubmitButton", typeof(RectTransform));
            buttonObject.transform.SetParent(hudObject.transform, false);
            Button button = buttonObject.AddComponent<Button>();
            hud.Configure(
                gameBootstrap: null,
                timeLabel: null,
                inventoryLabel: null,
                goalLabel: null,
                actionLabel: null,
                input: input,
                button: button,
                executor: executor);

            yield return null;
            input.text = "播种 1";
            button.onClick.Invoke();
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Pending));

            float actingDeadline = UnityEngine.Time.realtimeSinceStartup + 2f;
            while (executor.Status != NpcExecutionStatus.Acting &&
                UnityEngine.Time.realtimeSinceStartup < actingDeadline)
            {
                yield return null;
            }

            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Acting));
            input.text = "浇水 1";
            button.onClick.Invoke();
            Assert.That(executor.Queue.Count, Is.EqualTo(1));

            float deadline = UnityEngine.Time.realtimeSinceStartup + 4f;
            while (executor.IsBusy && UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.LessThan(deadline), "HUD command timed out.");
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
            Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(field.GetPlot(1).WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.Zero);
        }

        [UnityTest]
        public IEnumerator NavMeshMode_ConsecutiveActionsAtSamePlot_DoNotStall()
        {
            testRoot = new GameObject("NavMeshExecutorTestRoot");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestNavigationGround";
            ground.transform.SetParent(testRoot.transform, false);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(8f, 0.2f, 8f);

            NavMeshSurface surface = testRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.ignoreNavMeshAgent = true;
            surface.BuildNavMesh();

            field = new FarmField();
            inventory = new FarmInventory(carrotSeeds: 1, water: 1, fertilizer: 1);
            clock = new GameClock();

            GameObject plotTarget = new GameObject("PlotTarget_01");
            plotTarget.transform.SetParent(testRoot.transform, false);
            plotTarget.transform.position = new Vector3(1f, 0f, 1f);

            GameObject interactionObject = new GameObject("InteractionPoint_01");
            interactionObject.transform.SetParent(testRoot.transform, false);
            interactionObject.transform.position = new Vector3(1f, 0f, 0f);
            PlotInteractionPoint interactionPoint = interactionObject.AddComponent<PlotInteractionPoint>();
            Assert.That(interactionPoint.Configure(1, plotTarget.transform).Succeeded, Is.True);

            npcObject = new GameObject("NavMeshTestNpc");
            npcObject.transform.SetParent(testRoot.transform, false);
            npcObject.transform.position = new Vector3(-1f, 0f, 0f);
            npcVisual = new GameObject("NavMeshTestNpcVisual").transform;
            npcVisual.SetParent(npcObject.transform, false);

            NavMeshAgent agent = npcObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.2f;
            agent.height = 1f;
            agent.speed = 4f;
            agent.acceleration = 20f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 0.02f;

            NpcNavigator navigator = npcObject.AddComponent<NpcNavigator>();
            Assert.That(
                navigator.Configure(
                    agent,
                    new[] { interactionPoint },
                    requireNavMesh: true,
                    movementSpeed: 4f).Succeeded,
                Is.True);

            BlockoutActionFeedback feedback = npcObject.AddComponent<BlockoutActionFeedback>();
            Assert.That(feedback.Configure(npcVisual, null, null).Succeeded, Is.True);

            executor = npcObject.AddComponent<NpcPlanExecutor>();
            Assert.That(
                executor.Initialize(new NpcActionContext(field, inventory, clock), navigator, feedback).Succeeded,
                Is.True);
            Assert.That(executor.Enqueue(new MoveToPlotAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new SowAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new WaterAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new FertilizeAction(1, 0.05f)).Succeeded, Is.True);

            bool sawMoving = false;
            bool sawActing = false;
            float deadline = UnityEngine.Time.realtimeSinceStartup + 5f;
            while (executor.IsBusy && UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                sawMoving |= executor.Status == NpcExecutionStatus.Moving;
                sawActing |= executor.Status == NpcExecutionStatus.Acting;
                yield return null;
            }

            Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.LessThan(deadline), "NavMesh executor timed out.");
            Assert.That(agent.isOnNavMesh, Is.True);
            Assert.That(sawMoving, Is.True);
            Assert.That(sawActing, Is.True);
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
            Assert.That(Vector3.Distance(npcObject.transform.position, interactionObject.transform.position), Is.LessThan(0.2f));
            Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(field.GetPlot(1).WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(field.GetPlot(1).IsFertilized, Is.True);
        }

        [UnityTest]
        public IEnumerator Sow_MovesAndShowsFeedback_BeforeCommittingDomainState()
        {
            CreateExecutor(seedCount: 1, interactionPosition: new Vector3(1.5f, 0f, 0f), movementSpeed: 6f);
            Vector3 initialScale = npcVisual.localScale;

            Assert.That(executor.Enqueue(new SowAction(1, 0.3f)).Succeeded, Is.True);
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Pending));
            Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));

            bool sawMoving = false;
            bool sawActing = false;
            bool sawVisualFeedback = false;
            float deadline = UnityEngine.Time.realtimeSinceStartup + 3f;
            while (executor.IsBusy && UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                sawMoving |= executor.Status == NpcExecutionStatus.Moving;
                if (executor.Status == NpcExecutionStatus.Acting)
                {
                    sawActing = true;
                    sawVisualFeedback |= Vector3.Distance(npcVisual.localScale, initialScale) > 0.001f;
                    Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Empty));
                    Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
                }

                yield return null;
            }

            Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.LessThan(deadline), "Executor timed out.");
            Assert.That(sawMoving, Is.True);
            Assert.That(sawActing, Is.True);
            Assert.That(sawVisualFeedback, Is.True);
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
            Assert.That(field.GetPlot(1).State, Is.EqualTo(PlotState.Growing));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
            Assert.That(npcVisual.localScale, Is.EqualTo(initialScale));
        }

        [UnityTest]
        public IEnumerator PreconditionsChangedDuringMovement_FailsWithoutSecondMutation()
        {
            CreateExecutor(seedCount: 2, interactionPosition: new Vector3(2f, 0f, 0f), movementSpeed: 2f);
            Assert.That(executor.Enqueue(new SowAction(1, 0.1f)).Succeeded, Is.True);

            float movingDeadline = UnityEngine.Time.realtimeSinceStartup + 1f;
            while (executor.Status != NpcExecutionStatus.Moving &&
                UnityEngine.Time.realtimeSinceStartup < movingDeadline)
            {
                yield return null;
            }

            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Moving));
            Assert.That(field.GetPlot(1).Sow(inventory).Succeeded, Is.True);
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
            LogAssert.Expect(
                LogType.Warning,
                new Regex("NPC action 'Sow Plot 01' failed: Sow failed: Plot 01 is not empty\\."));

            float failureDeadline = UnityEngine.Time.realtimeSinceStartup + 2f;
            while (executor.Status != NpcExecutionStatus.Failed &&
                UnityEngine.Time.realtimeSinceStartup < failureDeadline)
            {
                yield return null;
            }

            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Failed));
            Assert.That(executor.LastResult.HasValue, Is.True);
            Assert.That(executor.LastResult.Value.FailureReason, Is.EqualTo(ActionFailureReason.InvalidState));
            Assert.That(executor.LastFailureReason, Does.Contain("Plot 01 is not empty"));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator QueuedActions_RunInOrder_AndWaitCommitsClockAtCompletion()
        {
            CreateExecutor(
                seedCount: 1,
                interactionPosition: new Vector3(0.2f, 0f, 0f),
                movementSpeed: 20f,
                waterCount: 1,
                fertilizerCount: 1);

            Assert.That(executor.Enqueue(new SowAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new WaterAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new FertilizeAction(1, 0.05f)).Succeeded, Is.True);
            Assert.That(executor.Enqueue(new WaitAction(0.05f)).Succeeded, Is.True);

            float deadline = UnityEngine.Time.realtimeSinceStartup + 3f;
            while (executor.IsBusy && UnityEngine.Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            FarmPlot plot = field.GetPlot(1);
            Assert.That(UnityEngine.Time.realtimeSinceStartup, Is.LessThan(deadline), "Executor timed out.");
            Assert.That(executor.Status, Is.EqualTo(NpcExecutionStatus.Completed));
            Assert.That(plot.State, Is.EqualTo(PlotState.Growing));
            Assert.That(plot.WaterLevel, Is.EqualTo(FarmPlot.MaximumWaterLevel));
            Assert.That(plot.IsFertilized, Is.True);
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Water), Is.Zero);
            Assert.That(inventory.GetCount(InventoryItem.Fertilizer), Is.Zero);
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(0.05d).Within(0.0001d));
        }

        private void CreateExecutor(
            int seedCount,
            Vector3 interactionPosition,
            float movementSpeed,
            int waterCount = 0,
            int fertilizerCount = 0)
        {
            testRoot = new GameObject("NpcExecutorTestRoot");
            field = new FarmField();
            inventory = new FarmInventory(seedCount, waterCount, fertilizerCount);
            clock = new GameClock(initialTimeScale: 1d);

            GameObject plotTarget = new GameObject("PlotTarget_01");
            plotTarget.transform.SetParent(testRoot.transform, false);
            plotTarget.transform.position = interactionPosition + Vector3.forward;

            GameObject interactionObject = new GameObject("InteractionPoint_01");
            interactionObject.transform.SetParent(testRoot.transform, false);
            interactionObject.transform.position = interactionPosition;
            PlotInteractionPoint interactionPoint = interactionObject.AddComponent<PlotInteractionPoint>();
            Assert.That(interactionPoint.Configure(1, plotTarget.transform).Succeeded, Is.True);

            npcObject = new GameObject("TestNpc");
            npcObject.transform.SetParent(testRoot.transform, false);
            npcVisual = new GameObject("TestNpcVisual").transform;
            npcVisual.SetParent(npcObject.transform, false);

            NpcNavigator navigator = npcObject.AddComponent<NpcNavigator>();
            Assert.That(
                navigator.Configure(
                    agent: null,
                    points: new[] { interactionPoint },
                    requireNavMesh: false,
                    movementSpeed: movementSpeed).Succeeded,
                Is.True);

            BlockoutActionFeedback feedback = npcObject.AddComponent<BlockoutActionFeedback>();
            Assert.That(feedback.Configure(npcVisual, null, null).Succeeded, Is.True);

            executor = npcObject.AddComponent<NpcPlanExecutor>();
            var context = new NpcActionContext(field, inventory, clock);
            Assert.That(executor.Initialize(context, navigator, feedback).Succeeded, Is.True);
        }
    }
}
