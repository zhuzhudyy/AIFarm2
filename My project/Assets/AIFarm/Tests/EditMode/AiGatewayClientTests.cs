using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class AiGatewayClientTests
    {
        private const string SupportedCommand = "把地种满胡萝卜并照顾到收获。";

        private const string ValidGoalJson =
            "{\"goal_id\":\"full_field_carrot_lifecycle\",\"crop\":\"carrot\"," +
            "\"target_plot_numbers\":[1,2,3,4,5,6,7,8,9]," +
            "\"requires_sowing\":true,\"requires_watering\":true," +
            "\"requires_fertilizing\":true,\"requires_weeding\":true," +
            "\"requires_harvesting\":true," +
            "\"summary\":\"完成 3×3 农田的胡萝卜全周期\"}";

        [Test]
        public void LocalClient_InterpretsSupportedCommandWithoutTransport()
        {
            var client = new LocalAiGatewayClient();
            AiGatewayResult<FarmGoalSpec> result = null;

            RunCoroutine(client.InterpretCommand(SupportedCommand, value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(result.Value.GoalId, Is.EqualTo(FarmGoalSpec.FullFieldCarrotLifecycleId));
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Local));
        }

        [Test]
        public void RemoteClient_ValidGoal_UsesConfiguredTimeoutAndNoCredentialPayload()
        {
            var transport = new FakeTransport(AiGatewayHttpResult.Success(200, ValidGoalJson));
            var client = new RemoteAiGatewayClient(
                "http://127.0.0.1:8000/",
                requestTimeoutSeconds: 7,
                gatewayTransport: transport);
            AiGatewayResult<FarmGoalSpec> result = null;

            RunCoroutine(client.InterpretCommand(SupportedCommand, value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(transport.LastUrl, Is.EqualTo("http://127.0.0.1:8000/v1/interpret-command"));
            Assert.That(transport.LastTimeoutSeconds, Is.EqualTo(7));
            Assert.That(transport.LastJson, Does.Contain("\"command\""));
            Assert.That(transport.LastJson, Does.Not.Contain("api_key").IgnoreCase);
            Assert.That(transport.LastJson, Does.Not.Contain("authorization").IgnoreCase);
            Assert.That(transport.LastJson, Does.Not.Contain("OPENAI_API_KEY"));
            Assert.That(transport.LastJson, Does.Not.Contain("sk-"));
        }

        [Test]
        public void RemoteClient_ExplicitResidentId_IsIncludedInRequestAndResult()
        {
            var residentId = new ResidentId("resident-test-a");
            var transport = new FakeTransport(AiGatewayHttpResult.Success(200, ValidGoalJson));
            var client = new RemoteAiGatewayClient(
                "http://127.0.0.1:8000/",
                requestTimeoutSeconds: 7,
                gatewayTransport: transport);
            AiGatewayResult<FarmGoalSpec> result = null;

            RunCoroutine(client.InterpretCommand(
                residentId,
                SupportedCommand,
                value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.ResidentId, Is.EqualTo(residentId));
            Assert.That(
                transport.LastJson,
                Does.Contain("\"resident_id\":\"resident-test-a\""));
        }

        [Test]
        public void RemoteClient_TransportFailure_FallsBackToLocalInterpreter()
        {
            var transport = new FakeTransport(
                AiGatewayHttpResult.Failure(0, "Connection timed out."));
            var client = new RemoteAiGatewayClient(
                "http://gateway.invalid",
                requestTimeoutSeconds: 3,
                gatewayTransport: transport);
            AiGatewayResult<FarmGoalSpec> result = null;

            RunCoroutine(client.InterpretCommand(SupportedCommand, value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(client.LastRemoteFailure, Does.Contain("timed out"));
        }

        [Test]
        public void RemoteClient_InvalidSemanticJson_FallsBackToLocalInterpreter()
        {
            string invalidGoal = ValidGoalJson.Replace(
                "[1,2,3,4,5,6,7,8,9]",
                "[1,2,3]");
            var transport = new FakeTransport(AiGatewayHttpResult.Success(200, invalidGoal));
            var client = new RemoteAiGatewayClient(
                "https://gateway.example",
                requestTimeoutSeconds: 5,
                gatewayTransport: transport);
            AiGatewayResult<FarmGoalSpec> result = null;

            RunCoroutine(client.InterpretCommand(SupportedCommand, value => result = value));

            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(client.LastRemoteFailure, Does.Contain("semantic validation"));
        }

        [Test]
        public void JsonValidation_RejectsUnknownOrDuplicateResponseProperties()
        {
            string unknownProperty = ValidGoalJson.Insert(
                ValidGoalJson.Length - 1,
                ",\"api_key\":\"must-not-be-accepted\"");
            string duplicateProperty = ValidGoalJson.Insert(
                ValidGoalJson.Length - 1,
                ",\"crop\":\"carrot\"");

            Assert.That(
                AiGatewayJsonCodec.TryParseFarmGoal(unknownProperty, out _).Failed,
                Is.True);
            Assert.That(
                AiGatewayJsonCodec.TryParseFarmGoal(duplicateProperty, out _).Failed,
                Is.True);
        }

        [Test]
        public void RemoteClient_Utterance_SendsOnlyBoundedContextAndValidatesResponse()
        {
            const string response =
                "{\"trigger\":\"WaterNeeded\",\"mood\":\"Worried\"," +
                "\"emoji\":\"💧\",\"text\":\"土壤有点干，我去补水。\",\"provider\":\"openai\"}";
            var transport = new FakeTransport(AiGatewayHttpResult.Success(200, response));
            var client = new RemoteAiGatewayClient(
                "https://gateway.example",
                requestTimeoutSeconds: 5,
                gatewayTransport: transport);
            AiGatewayResult<NpcExpression> result = null;
            string oversizedContext = new string('x', 240) + "SHOULD_NOT_BE_SENT";

            RunCoroutine(client.GenerateUtterance(
                NpcExpressionTrigger.WaterNeeded,
                oversizedContext,
                value => result = value));

            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(result.Value.Trigger, Is.EqualTo(NpcExpressionTrigger.WaterNeeded));
            Assert.That(transport.LastJson, Does.Not.Contain("SHOULD_NOT_BE_SENT"));
            Assert.That(transport.LastJson, Does.Contain(new string('x', 200)));
            Assert.That(transport.LastJson, Does.Not.Contain(new string('x', 201)));
        }

        [Test]
        public void RemoteClient_Reflection_UsesMinimalGoalSnapshotAndValidatesSpec()
        {
            const string response =
                "{\"goal_id\":\"full_field_carrot_lifecycle\",\"outcome\":\"Completed\"," +
                "\"mood\":\"Proud\",\"emoji\":\"★\"," +
                "\"text\":\"九块地已经全部完成。\",\"provider\":\"mock\"}";
            var transport = new FakeTransport(AiGatewayHttpResult.Success(200, response));
            var client = new RemoteAiGatewayClient(
                "https://gateway.example",
                requestTimeoutSeconds: 5,
                gatewayTransport: transport);
            FarmGoalSpec goal = FarmGoalSpec.CreateFullFieldCarrotLifecycle();
            AiGatewayResult<NpcReflection> result = null;

            RunCoroutine(client.Reflect(
                goal,
                NpcReflectionOutcome.Completed,
                "九块目标土地已经全部收获。",
                value => result = value));

            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Value.GoalId, Is.EqualTo(goal.GoalId));
            Assert.That(result.Value.Outcome, Is.EqualTo(NpcReflectionOutcome.Completed));
            Assert.That(transport.LastUrl, Does.EndWith("/v1/reflect"));
            Assert.That(transport.LastJson, Does.Contain("\"goal\""));
            Assert.That(transport.LastJson, Does.Contain("\"event_summary\""));
            Assert.That(transport.LastJson, Does.Not.Contain("world_state").IgnoreCase);
            Assert.That(transport.LastJson.Length, Is.LessThan(1024));
        }

        [Test]
        public void RemoteClient_ReflectionTransportFailure_UsesLocalTemplate()
        {
            var transport = new FakeTransport(
                AiGatewayHttpResult.Failure(0, "Reflection service unavailable."));
            var client = new RemoteAiGatewayClient(
                "https://gateway.example",
                requestTimeoutSeconds: 3,
                gatewayTransport: transport);
            AiGatewayResult<NpcReflection> result = null;

            RunCoroutine(client.Reflect(
                FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                NpcReflectionOutcome.Completed,
                "九块土地已经收获。",
                value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(result.Value.Text, Is.Not.Empty);
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(client.LastRemoteFailure, Does.Contain("unavailable"));
        }

        [Test]
        public void RemoteClient_RejectsGatewayUrlContainingCredentials()
        {
            Assert.Throws<ArgumentException>(() =>
                new RemoteAiGatewayClient(
                    "https://user:secret@gateway.example",
                    requestTimeoutSeconds: 5,
                    gatewayTransport: new FakeTransport(
                        AiGatewayHttpResult.Success(200, ValidGoalJson))));
        }

        private static void RunCoroutine(IEnumerator root)
        {
            Assert.That(root, Is.Not.Null);
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            int iterations = 0;
            while (stack.Count > 0)
            {
                Assert.That(++iterations, Is.LessThan(1000), "Coroutine did not complete synchronously.");
                IEnumerator current = stack.Peek();
                if (!current.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                if (current.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else if (current.Current != null)
                {
                    Assert.Fail($"Unexpected asynchronous yield: {current.Current.GetType().Name}");
                }
            }
        }

        private sealed class FakeTransport : IAiGatewayTransport
        {
            private readonly AiGatewayHttpResult result;

            public FakeTransport(AiGatewayHttpResult result)
            {
                this.result = result;
            }

            public string LastUrl { get; private set; }

            public string LastJson { get; private set; }

            public int LastTimeoutSeconds { get; private set; }

            public IEnumerator PostJson(
                string url,
                string json,
                int timeoutSeconds,
                Action<AiGatewayHttpResult> completed)
            {
                LastUrl = url;
                LastJson = json;
                LastTimeoutSeconds = timeoutSeconds;
                completed(result);
                yield break;
            }
        }
    }
}
