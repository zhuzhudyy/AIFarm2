using System.Collections;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIFarm.Tests.PlayMode
{
    public sealed class LiveAiGatewayPlayModeTests
    {
        [UnityTest]
        [Explicit("Requires a deliberately started AI gateway on 127.0.0.1:8000.")]
        [Category("NetworkIntegration")]
        public IEnumerator RunningGateway_ReturnsValidatedStructuredSpecs()
        {
            RequireNetworkIntegrationOptIn();
            var client = new RemoteAiGatewayClient(
                "http://127.0.0.1:8000",
                requestTimeoutSeconds: 5);

            AiGatewayResult<FarmGoalSpec> goalResult = null;
            yield return client.InterpretCommand(
                "把地种满胡萝卜并照顾到收获。",
                result => goalResult = result);

            Assert.That(goalResult, Is.Not.Null);
            Assert.That(goalResult.Succeeded, Is.True, goalResult.Outcome.Message);
            Assert.That(goalResult.Source, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(
                goalResult.Value.GoalId,
                Is.EqualTo(FarmGoalSpec.FullFieldCarrotLifecycleId));

            AiGatewayResult<NpcExpression> utteranceResult = null;
            yield return client.GenerateUtterance(
                NpcExpressionTrigger.CommandAccepted,
                "目标已通过结构化校验。",
                result => utteranceResult = result);

            Assert.That(utteranceResult, Is.Not.Null);
            Assert.That(utteranceResult.Succeeded, Is.True, utteranceResult.Outcome.Message);
            Assert.That(utteranceResult.Source, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(
                utteranceResult.Value.Trigger,
                Is.EqualTo(NpcExpressionTrigger.CommandAccepted));
            Assert.That(utteranceResult.Value.Text, Is.Not.Empty);

            AiGatewayResult<NpcReflection> reflectionResult = null;
            yield return client.Reflect(
                goalResult.Value,
                NpcReflectionOutcome.Completed,
                "九块地已经全部收获。",
                result => reflectionResult = result);

            Assert.That(reflectionResult, Is.Not.Null);
            Assert.That(reflectionResult.Succeeded, Is.True, reflectionResult.Outcome.Message);
            Assert.That(reflectionResult.Source, Is.EqualTo(AiGatewayMode.Remote));
            Assert.That(
                reflectionResult.Value.Outcome,
                Is.EqualTo(NpcReflectionOutcome.Completed));
            Assert.That(reflectionResult.Value.Text, Is.Not.Empty);
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Remote));
        }

        [UnityTest]
        [Explicit("Requires the gateway to be stopped during the synchronization delay.")]
        [Category("NetworkIntegration")]
        public IEnumerator GatewayInterrupted_FallsBackWithoutHanging()
        {
            RequireNetworkIntegrationOptIn();
            var client = new RemoteAiGatewayClient(
                "http://127.0.0.1:8000",
                requestTimeoutSeconds: 2);

            AiGatewayResult<FarmGoalSpec> initialResult = null;
            yield return client.InterpretCommand(
                "把地种满胡萝卜并照顾到收获。",
                result => initialResult = result);

            Assert.That(initialResult, Is.Not.Null);
            Assert.That(initialResult.Succeeded, Is.True, initialResult.Outcome.Message);
            Assert.That(initialResult.Source, Is.EqualTo(AiGatewayMode.Remote));

            // The integration harness stops the gateway during this window.
            yield return new WaitForSecondsRealtime(20f);

            float startedAt = UnityEngine.Time.realtimeSinceStartup;
            AiGatewayResult<NpcExpression> fallbackResult = null;
            yield return client.GenerateUtterance(
                NpcExpressionTrigger.SowingStarted,
                "开始播种。",
                result => fallbackResult = result);
            float elapsedSeconds = UnityEngine.Time.realtimeSinceStartup - startedAt;

            Assert.That(fallbackResult, Is.Not.Null);
            Assert.That(fallbackResult.Succeeded, Is.True, fallbackResult.Outcome.Message);
            Assert.That(fallbackResult.Source, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(fallbackResult.Value.Text, Is.Not.Empty);
            Assert.That(client.ActiveMode, Is.EqualTo(AiGatewayMode.Local));
            Assert.That(elapsedSeconds, Is.LessThan(4f));
        }

        private static void RequireNetworkIntegrationOptIn()
        {
            if (System.Environment.GetEnvironmentVariable(
                    "RUN_UNITY_GATEWAY_INTEGRATION") != "1")
            {
                Assert.Ignore(
                    "Set RUN_UNITY_GATEWAY_INTEGRATION=1 before launching Unity " +
                    "to run live gateway tests.");
            }
        }
    }
}
