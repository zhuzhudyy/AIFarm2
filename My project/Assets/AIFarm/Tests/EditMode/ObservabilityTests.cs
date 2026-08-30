using System;
using System.Linq;
using System.Text.RegularExpressions;
using AIFarm.Core;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Presentation;
using AIFarm.Time;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIFarm.Tests.EditMode
{
    public sealed class ObservabilityTests
    {
        [Test]
        public void WorldEventLog_RetainsOnlyNewestTenEntries()
        {
            var events = new WorldEventLog();

            for (int index = 1; index <= 12; index++)
            {
                Assert.That(
                    events.Record(index, WorldEventKind.System, $"Event {index}").Succeeded,
                    Is.True);
            }

            Assert.That(events.Entries, Has.Count.EqualTo(10));
            Assert.That(events.Entries[0].Sequence, Is.EqualTo(3));
            Assert.That(events.Entries[0].Message, Is.EqualTo("Event 3"));
            Assert.That(events.Entries[9].Sequence, Is.EqualTo(12));
            Assert.That(events.Record(-1d, WorldEventKind.System, "Invalid").Failed, Is.True);
            Assert.That(events.Entries, Has.Count.EqualTo(10));
        }

        [TestCase(NpcExpressionTrigger.CommandAccepted, NpcMood.Happy)]
        [TestCase(NpcExpressionTrigger.SowingStarted, NpcMood.Focused)]
        [TestCase(NpcExpressionTrigger.WaterNeeded, NpcMood.Worried)]
        [TestCase(NpcExpressionTrigger.WeedsFound, NpcMood.Worried)]
        [TestCase(NpcExpressionTrigger.ActionFailed, NpcMood.Worried)]
        [TestCase(NpcExpressionTrigger.WaitingForGrowth, NpcMood.Tired)]
        [TestCase(NpcExpressionTrigger.HarvestStarted, NpcMood.Happy)]
        [TestCase(NpcExpressionTrigger.GoalCompleted, NpcMood.Proud)]
        public void LocalTemplateService_CoversRequiredTriggers(
            NpcExpressionTrigger trigger,
            NpcMood expectedMood)
        {
            var service = new LocalTemplateExpressionService();

            ActionResult result = service.CreateExpression(
                trigger,
                "测试上下文",
                0,
                out NpcExpression expression);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(service.IsExternalAiRequired, Is.False);
            Assert.That(expression.Trigger, Is.EqualTo(trigger));
            Assert.That(expression.Mood, Is.EqualTo(expectedMood));
            Assert.That(expression.Text, Is.Not.Empty);
            Assert.That(expression.Emoji, Is.Not.Empty);
        }

        [Test]
        public void ExpressionDirector_CoolsDownRepeatedTrigger_ButQueuesDifferentEvents()
        {
            var director = new NpcExpressionDirector(
                new LocalTemplateExpressionService(),
                cooldownSeconds: 10d,
                displaySeconds: 2d);

            Assert.That(
                director.Trigger(NpcExpressionTrigger.SowingStarted, string.Empty, 0d).Succeeded,
                Is.True);
            Assert.That(
                director.Trigger(NpcExpressionTrigger.SowingStarted, string.Empty, 1d).Failed,
                Is.True);
            Assert.That(
                director.Trigger(NpcExpressionTrigger.WaterNeeded, string.Empty, 1d).Succeeded,
                Is.True);

            Assert.That(director.Current.Trigger, Is.EqualTo(NpcExpressionTrigger.SowingStarted));
            Assert.That(director.PendingCount, Is.EqualTo(1));
            Assert.That(director.RecentExpressions, Has.Count.EqualTo(2));
            Assert.That(director.Advance(2d).Succeeded, Is.True);
            Assert.That(director.Current.Trigger, Is.EqualTo(NpcExpressionTrigger.WaterNeeded));
            Assert.That(director.Advance(2d).Succeeded, Is.True);
            Assert.That(director.Current, Is.Null);
            Assert.That(director.Latest.Trigger, Is.EqualTo(NpcExpressionTrigger.WaterNeeded));
            Assert.That(
                director.Trigger(NpcExpressionTrigger.SowingStarted, string.Empty, 10d).Succeeded,
                Is.True);
        }

        [Test]
        public void ExpressionDirector_FailureInterruptsQueuedDialogue()
        {
            var director = new NpcExpressionDirector(
                new LocalTemplateExpressionService(),
                cooldownSeconds: 10d,
                displaySeconds: 2d);
            Assert.That(
                director.Trigger(NpcExpressionTrigger.CommandAccepted, string.Empty, 0d).Succeeded,
                Is.True);
            Assert.That(
                director.Trigger(NpcExpressionTrigger.SowingStarted, string.Empty, 0d).Succeeded,
                Is.True);

            Assert.That(
                director.Trigger(
                    NpcExpressionTrigger.ActionFailed,
                    "没有种子",
                    1d,
                    interrupt: true).Succeeded,
                Is.True);

            Assert.That(director.Current.Trigger, Is.EqualTo(NpcExpressionTrigger.ActionFailed));
            Assert.That(director.Current.Mood, Is.EqualTo(NpcMood.Worried));
            Assert.That(director.PendingCount, Is.Zero);
        }

        [Test]
        public void ExecutorFailure_TriggersWorriedOfflineExpression()
        {
            var root = new GameObject("ObservabilityFailureTest");
            try
            {
                var field = new FarmField();
                var inventory = new FarmInventory();
                var clock = new GameClock();
                var events = new WorldEventLog();
                var context = new NpcActionContext(field, inventory, clock, eventLog: events);
                NpcPlanExecutor executor = root.AddComponent<NpcPlanExecutor>();
                Assert.That(
                    executor.Initialize(context, new StubNavigation(), new StubFeedback()).Succeeded,
                    Is.True);
                ReplanController controller = root.AddComponent<ReplanController>();
                Assert.That(controller.Initialize(context, executor, new DemoMode()).Succeeded, Is.True);
                Assert.That(executor.Enqueue(new SowAction(1, 0.01f)).Succeeded, Is.True);
                Assert.That(executor.Tick(0f).Succeeded, Is.True);
                LogAssert.Expect(
                    LogType.Warning,
                    new Regex("NPC action 'Sow Plot 01' failed: Sow failed: no carrot seed is available\\."));

                ActionResult failure = executor.Tick(0f);

                Assert.That(failure.Failed, Is.True);
                Assert.That(controller.CurrentMood, Is.EqualTo(NpcMood.Worried));
                Assert.That(
                    controller.RecentExpressions.Any(
                        expression => expression.Trigger == NpcExpressionTrigger.ActionFailed),
                    Is.True);
                Assert.That(events.Entries.Last().Kind, Is.EqualTo(WorldEventKind.ActionFailed));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class StubNavigation : INpcNavigationDriver
        {
            public bool IsMoving => false;

            public ActionResult BeginMove(int plotNumber)
            {
                return ActionResult.Success();
            }

            public ActionResult Tick(float deltaTime, out bool arrived)
            {
                arrived = true;
                return ActionResult.Success();
            }

            public ActionResult CancelMove()
            {
                return ActionResult.Success();
            }
        }

        private sealed class StubFeedback : INpcActionFeedback
        {
            public bool IsPlaying => false;

            public float Progress => 0f;

            public ActionResult Begin(string actionName, float durationSeconds)
            {
                return ActionResult.Success();
            }

            public ActionResult Tick(float deltaTime, out bool completed)
            {
                completed = true;
                return ActionResult.Success();
            }

            public ActionResult Cancel()
            {
                return ActionResult.Success();
            }
        }
    }
}
