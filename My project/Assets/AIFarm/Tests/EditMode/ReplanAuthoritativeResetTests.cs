using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class ReplanAuthoritativeResetTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject root;
        private DemoInventoryConfig inventoryConfig;
        private DemoSceneConfig sceneConfig;
        private GameBootstrap bootstrap;
        private ReplanController replanner;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ReplanAuthoritativeResetTestRoot");
            inventoryConfig = ScriptableObject.CreateInstance<DemoInventoryConfig>();
            inventoryConfig.Configure(20, 20, 20, 0);
            sceneConfig = ScriptableObject.CreateInstance<DemoSceneConfig>();
            sceneConfig.Configure(
                inventoryConfig,
                day: 1,
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

            GameObject npcObject = new GameObject("Npc");
            npcObject.transform.SetParent(root.transform, false);
            NpcPlanExecutor executor = npcObject.AddComponent<NpcPlanExecutor>();
            var context = new NpcActionContext(
                bootstrap.Field,
                bootstrap.Inventory,
                bootstrap.Clock,
                bootstrap.Simulation,
                bootstrap.Events);
            Assert.That(
                executor.Initialize(
                    context,
                    new ImmediateNavigation(),
                    new ImmediateFeedback()).Succeeded,
                Is.True);

            replanner = bootstrapObject.AddComponent<ReplanController>();
            Assert.That(replanner.Configure(bootstrap, executor).Succeeded, Is.True);
            Assert.That(
                replanner.Initialize(
                    context,
                    executor,
                    bootstrap.Mode,
                    new DelayedSuccessfulGateway(),
                    bootstrap.ResidentRegistry).Succeeded,
                Is.True);
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
        public void AuthoritativeReset_StaleGoalParentCannotOverwriteNewStateOrFlags()
        {
            IEnumerator request = InvokeRequest(
                "RequestGoalInterpretation",
                "种满胡萝卜并照顾到收获",
                0L,
                0L);
            IEnumerator transport = StartNestedRequest(request);
            Assert.That(transport.MoveNext(), Is.True);

            bootstrap.NotifyAuthoritativeStateResetting();
            SetPrivateField("gatewayRequestPending", true);
            SetAutoProperty("CurrentGoalText", "loaded-goal-sentinel");
            Complete(transport);

            Assert.That(request.MoveNext(), Is.False);
            Assert.That(replanner.ActiveGoal, Is.Null);
            Assert.That(replanner.CurrentGoalText, Is.EqualTo("loaded-goal-sentinel"));
            Assert.That(replanner.IsGatewayRequestPending, Is.True,
                "The stale parent must not clear a newer request's pending flag.");
        }

        [Test]
        public void AuthoritativeReset_StaleExpressionParentCannotReplaceLoadedExpression()
        {
            const NpcExpressionTrigger trigger = NpcExpressionTrigger.WaterNeeded;
            IEnumerator request = InvokeRequest(
                "RequestExpression",
                trigger,
                "local fallback",
                "remote context",
                true,
                0L,
                0L);
            IEnumerator transport = StartNestedRequest(request);
            Assert.That(transport.MoveNext(), Is.True);

            bootstrap.NotifyAuthoritativeStateResetting();
            NpcExpressionDirector director = GetPrivateField<NpcExpressionDirector>(
                "expressionDirector");
            Assert.That(
                director.Trigger(
                    new NpcExpression(
                        NpcExpressionTrigger.ActionFailed,
                        NpcMood.Focused,
                        "",
                        "loaded-expression-sentinel"),
                    UnityEngine.Time.unscaledTime,
                    true).Succeeded,
                Is.True);
            HashSet<NpcExpressionTrigger> pending =
                GetPrivateField<HashSet<NpcExpressionTrigger>>(
                    "pendingExpressionTriggers");
            pending.Add(trigger);
            Complete(transport);

            Assert.That(request.MoveNext(), Is.False);
            Assert.That(replanner.NpcExpression, Is.EqualTo("loaded-expression-sentinel"));
            Assert.That(pending.Contains(trigger), Is.True,
                "The stale parent must not remove a newer expression ticket.");
        }

        [Test]
        public void AuthoritativeReset_StaleReflectionParentCannotWriteLoadedMemory()
        {
            FarmGoalSpec goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            int cycleNumber = replanner.RuntimeState.BeginCycle();
            const NpcExpressionTrigger trigger = NpcExpressionTrigger.GoalCompleted;
            IEnumerator request = InvokeRequest(
                "RequestReflection",
                goal,
                NpcReflectionOutcome.Completed,
                "旧请求的完成摘要",
                trigger,
                cycleNumber,
                true,
                0L,
                0L);
            IEnumerator transport = StartNestedRequest(request);
            Assert.That(transport.MoveNext(), Is.True);
            int memoryCount = replanner.RuntimeState.Memories.Entries.Count;

            bootstrap.NotifyAuthoritativeStateResetting();
            SetPrivateField("reflectionRequestPending", true);
            HashSet<NpcExpressionTrigger> pending =
                GetPrivateField<HashSet<NpcExpressionTrigger>>(
                    "pendingExpressionTriggers");
            pending.Add(trigger);
            Complete(transport);

            Assert.That(request.MoveNext(), Is.False);
            Assert.That(replanner.RecentReflections, Is.Empty);
            Assert.That(
                replanner.RuntimeState.Memories.Entries.Count,
                Is.EqualTo(memoryCount));
            Assert.That(replanner.IsReflectionPending, Is.True,
                "The stale parent must not clear a newer reflection ticket.");
            Assert.That(pending.Contains(trigger), Is.True);
        }

        private IEnumerator InvokeRequest(string methodName, params object[] arguments)
        {
            MethodInfo method = typeof(ReplanController).GetMethod(
                methodName,
                PrivateInstance);
            Assert.That(method, Is.Not.Null, $"Missing private request method {methodName}.");
            return (IEnumerator)method.Invoke(replanner, arguments);
        }

        private static IEnumerator StartNestedRequest(IEnumerator parent)
        {
            Assert.That(parent.MoveNext(), Is.True);
            Assert.That(parent.Current, Is.InstanceOf<IEnumerator>());
            return (IEnumerator)parent.Current;
        }

        private static void Complete(IEnumerator request)
        {
            int steps = 0;
            while (request.MoveNext())
            {
                Assert.That(++steps, Is.LessThan(8));
            }
        }

        private T GetPrivateField<T>(string fieldName)
        {
            FieldInfo field = typeof(ReplanController).GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
            return (T)field.GetValue(replanner);
        }

        private void SetPrivateField<T>(string fieldName, T value)
        {
            FieldInfo field = typeof(ReplanController).GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
            field.SetValue(replanner, value);
        }

        private void SetAutoProperty(string propertyName, string value)
        {
            SetPrivateField($"<{propertyName}>k__BackingField", value);
        }

        private sealed class DelayedSuccessfulGateway : IAiGatewayClient
        {
            public AiGatewayMode ConfiguredMode => AiGatewayMode.Remote;

            public AiGatewayMode ActiveMode => AiGatewayMode.Remote;

            public IEnumerator InterpretCommand(
                string command,
                Action<AiGatewayResult<FarmGoalSpec>> completed)
            {
                return InterpretCommand(ResidentIds.Yaya, command, completed);
            }

            public IEnumerator InterpretCommand(
                ResidentId residentId,
                string command,
                Action<AiGatewayResult<FarmGoalSpec>> completed)
            {
                yield return null;
                completed(AiGatewayResult<FarmGoalSpec>.Success(
                    residentId,
                    FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                    AiGatewayMode.Remote,
                    "Delayed goal completed."));
            }

            public IEnumerator GenerateUtterance(
                NpcExpressionTrigger trigger,
                string context,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                return GenerateUtterance(ResidentIds.Yaya, trigger, context, completed);
            }

            public IEnumerator GenerateUtterance(
                ResidentId residentId,
                NpcExpressionTrigger trigger,
                string context,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                yield return null;
                completed(AiGatewayResult<NpcExpression>.Success(
                    residentId,
                    new NpcExpression(trigger, NpcMood.Happy, "", "stale-expression"),
                    AiGatewayMode.Remote,
                    "Delayed expression completed."));
            }

            public IEnumerator Reflect(
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                return Reflect(
                    ResidentIds.Yaya,
                    goal,
                    outcome,
                    eventSummary,
                    completed);
            }

            public IEnumerator Reflect(
                ResidentId residentId,
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                yield return null;
                completed(AiGatewayResult<NpcReflection>.Success(
                    residentId,
                    new NpcReflection(
                        goal.GoalId,
                        outcome,
                        NpcMood.Proud,
                        "",
                        "stale-reflection"),
                    AiGatewayMode.Remote,
                    "Delayed reflection completed."));
            }

            public IEnumerator GenerateConversationScript(
                ConversationScriptRequest request,
                Action<AiGatewayResult<ConversationScriptSpec>> completed)
            {
                yield break;
            }

            public IEnumerator DecideResident(
                ResidentDecisionRequest request,
                Action<AiGatewayResult<ResidentDecisionSpec>> completed)
            {
                yield break;
            }
        }

        private sealed class ImmediateNavigation : INpcNavigationDriver
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

        private sealed class ImmediateFeedback : INpcActionFeedback
        {
            public bool IsPlaying => false;

            public float Progress => 1f;

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
