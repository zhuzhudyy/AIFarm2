using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Social;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class AiRequestCoordinatorTests
    {
        [Test]
        public void SameFrame_HighPriorityStartsBeforeEarlierLowPriorityRequest()
        {
            var gateway = new RecordingGatewayClient();
            var coordinator = new AiRequestCoordinator(gateway, 1);
            AiGatewayResult<NpcReflection> reflectionResult = null;
            AiGatewayResult<FarmGoalSpec> commandResult = null;
            IEnumerator lowPriority = coordinator.Reflect(
                ResidentIds.Yaya,
                FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                NpcReflectionOutcome.Completed,
                "completed",
                value => reflectionResult = value);
            IEnumerator highPriority = coordinator.InterpretCommand(
                ResidentIds.Yaya,
                "farm",
                value => commandResult = value);

            Assert.That(lowPriority.MoveNext(), Is.True);
            Assert.That(highPriority.MoveNext(), Is.True);
            Assert.That(lowPriority.MoveNext(), Is.True, "Low priority should remain queued.");
            Assert.That(highPriority.MoveNext(), Is.True, "High priority should start first.");
            Assert.That(gateway.StartedOperations, Is.EqualTo(new[] { "interpret" }));

            Assert.That(highPriority.MoveNext(), Is.False);
            Assert.That(commandResult, Is.Not.Null);
            Assert.That(lowPriority.MoveNext(), Is.True);
            Assert.That(gateway.StartedOperations, Is.EqualTo(new[] { "interpret", "reflect" }));
            Assert.That(lowPriority.MoveNext(), Is.False);
            Assert.That(reflectionResult, Is.Not.Null);
        }

        [Test]
        public void SameResident_CannotHaveTwoConcurrentRequests()
        {
            var gateway = new RecordingGatewayClient();
            var coordinator = new AiRequestCoordinator(gateway, 2);
            AiGatewayResult<NpcExpression> firstResult = null;
            AiGatewayResult<NpcExpression> secondResult = null;
            IEnumerator first = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.CommandAccepted,
                "first",
                value => firstResult = value);
            IEnumerator second = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.WaterNeeded,
                "second",
                value => secondResult = value);

            Assert.That(first.MoveNext(), Is.True);
            Assert.That(second.MoveNext(), Is.True);
            Assert.That(first.MoveNext(), Is.True);
            Assert.That(second.MoveNext(), Is.True, "The second request must wait for its resident.");
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(gateway.MaximumObservedConcurrency, Is.EqualTo(1));

            Assert.That(first.MoveNext(), Is.False);
            Assert.That(second.MoveNext(), Is.True);
            Assert.That(gateway.MaximumObservedConcurrency, Is.EqualTo(1));
            Assert.That(second.MoveNext(), Is.False);
            Assert.That(firstResult, Is.Not.Null);
            Assert.That(secondResult, Is.Not.Null);
        }

        [Test]
        public void CancelledConversation_IgnoresLateGatewayResultAndReleasesOwners()
        {
            var gateway = new RecordingGatewayClient();
            var coordinator = new AiRequestCoordinator(gateway, 2);
            var cancellation = new AiRequestCancellation();
            int publicCompletionCount = 0;
            IEnumerator request = coordinator.GenerateConversationScript(
                CreateConversationRequest(),
                AiRequestPriority.Normal,
                cancellation,
                _ => publicCompletionCount++);

            Assert.That(request.MoveNext(), Is.True);
            Assert.That(request.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(gateway.ConversationStartCount, Is.EqualTo(1));

            cancellation.Cancel();
            Assert.That(request.MoveNext(), Is.False);

            Assert.That(gateway.CompletedOperationCount, Is.EqualTo(1),
                "The fake gateway simulates a response arriving after cancellation.");
            Assert.That(publicCompletionCount, Is.Zero,
                "A stale response must not reach owning state.");
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
        }

        private static ConversationScriptRequest CreateConversationRequest()
        {
            var yaya = new ResidentContext(
                ResidentIds.Yaya,
                ResidentPersonaSnapshot.FromDefinition(ResidentDefinition.Yaya),
                "idle",
                new[]
                {
                    new RelationshipSnapshot(ResidentIds.Yaya, ResidentIds.Amu, 0, 0)
                },
                Array.Empty<ResidentMemorySnapshot>());
            var amu = new ResidentContext(
                ResidentIds.Amu,
                ResidentPersonaSnapshot.FromDefinition(ResidentDefinition.Amu),
                "idle",
                new[]
                {
                    new RelationshipSnapshot(ResidentIds.Amu, ResidentIds.Yaya, 0, 0)
                },
                Array.Empty<ResidentMemorySnapshot>());
            return new ConversationScriptRequest(
                ResidentIds.Yaya,
                new[] { ResidentIds.Yaya, ResidentIds.Amu },
                new[] { yaya, amu },
                "well maintenance",
                4);
        }

        private sealed class RecordingGatewayClient : IAiGatewayClient
        {
            private readonly List<string> startedOperations = new List<string>();
            private int activeOperations;

            public AiGatewayMode ConfiguredMode => AiGatewayMode.Remote;

            public AiGatewayMode ActiveMode => AiGatewayMode.Remote;

            public IReadOnlyList<string> StartedOperations => startedOperations;

            public int MaximumObservedConcurrency { get; private set; }

            public int CompletedOperationCount { get; private set; }

            public int ConversationStartCount { get; private set; }

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
                return Complete(
                    "interpret",
                    residentId,
                    FarmGoalSpec.CreateFullFieldCarrotLifecycle(),
                    completed);
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
                return Complete(
                    "utterance",
                    residentId,
                    new NpcExpression(trigger, NpcMood.Focused, "!", context),
                    completed);
            }

            public IEnumerator Reflect(
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                return Reflect(ResidentIds.Yaya, goal, outcome, eventSummary, completed);
            }

            public IEnumerator Reflect(
                ResidentId residentId,
                FarmGoalSpec goal,
                NpcReflectionOutcome outcome,
                string eventSummary,
                Action<AiGatewayResult<NpcReflection>> completed)
            {
                return Complete(
                    "reflect",
                    residentId,
                    new NpcReflection(goal.GoalId, outcome, NpcMood.Proud, "!", eventSummary),
                    completed);
            }

            public IEnumerator GenerateConversationScript(
                ConversationScriptRequest request,
                Action<AiGatewayResult<ConversationScriptSpec>> completed)
            {
                ConversationStartCount++;
                var script = new ConversationScriptSpec(
                    request.ResidentId,
                    new[]
                    {
                        new ConversationLineSpec(
                            request.ParticipantIds[0],
                            NpcMood.Happy,
                            ":)",
                            "Hello."),
                        new ConversationLineSpec(
                            request.ParticipantIds[1],
                            NpcMood.Focused,
                            "!",
                            "Hello back.")
                    },
                    ConversationOutcome.Neutral,
                    "test");
                return Complete("conversation", request.ResidentId, script, completed);
            }

            private IEnumerator Complete<T>(
                string operation,
                ResidentId residentId,
                T value,
                Action<AiGatewayResult<T>> completed)
                where T : class
            {
                startedOperations.Add(operation);
                activeOperations++;
                MaximumObservedConcurrency = Math.Max(
                    MaximumObservedConcurrency,
                    activeOperations);
                yield return null;
                activeOperations--;
                CompletedOperationCount++;
                completed(AiGatewayResult<T>.Success(
                    residentId,
                    value,
                    AiGatewayMode.Remote));
            }
        }
    }
}
