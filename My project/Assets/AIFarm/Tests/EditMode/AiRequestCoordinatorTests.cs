using System;
using System.Collections;
using System.Collections.Generic;
using AIFarm.Ai;
using AIFarm.Npc;
using AIFarm.Presentation;
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

            Assert.That(gateway.CompletedOperationCount, Is.Zero,
                "Cancellation must stop advancing the abandoned gateway routine.");
            Assert.That(gateway.ActiveOperationCount, Is.Zero);
            Assert.That(gateway.DisposedOperationCount, Is.EqualTo(1));
            Assert.That(publicCompletionCount, Is.Zero,
                "A stale response must not reach owning state.");
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
        }

        [Test]
        public void DisposedActiveRequest_ReleasesGlobalSlotAndResidentOwnership()
        {
            var gateway = new RecordingGatewayClient();
            var coordinator = new AiRequestCoordinator(gateway, 1);
            IEnumerator abandoned = coordinator.GenerateConversationScript(
                CreateConversationRequest(),
                _ => Assert.Fail("A disposed stale request must not publish a result."));

            Assert.That(abandoned.MoveNext(), Is.True);
            Assert.That(abandoned.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));

            ((IDisposable)abandoned).Dispose();

            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);

            AiGatewayResult<ResidentDecisionSpec> nextResult = null;
            IEnumerator next = coordinator.DecideResident(
                CreateResidentDecisionRequest(),
                value => nextResult = value);
            while (next.MoveNext())
            {
            }

            Assert.That(nextResult, Is.Not.Null);
            Assert.That(nextResult.Succeeded, Is.True, nextResult.Outcome.Message);
        }

        [Test]
        public void AuthoritativeReset_CancelsActiveAndPending_IgnoresLateResult_AndRemainsReusable()
        {
            var gateway = new RecordingGatewayClient(blockFirstUtterance: true);
            var coordinator = new AiRequestCoordinator(gateway, 1);
            int activeCompletionCount = 0;
            int pendingCompletionCount = 0;
            AiGatewayResult<NpcExpression> replacementResult = null;
            IEnumerator active = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.CommandAccepted,
                "active",
                _ => activeCompletionCount++);
            IEnumerator pending = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.WaterNeeded,
                "pending",
                _ => pendingCompletionCount++);

            Assert.That(active.MoveNext(), Is.True);
            Assert.That(pending.MoveNext(), Is.True);
            Assert.That(active.MoveNext(), Is.True);
            Assert.That(pending.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.PendingRequestCount, Is.EqualTo(1));

            int cancelledCount = coordinator.CancelAllForAuthoritativeReset();

            Assert.That(cancelledCount, Is.EqualTo(2));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
            Assert.That(gateway.ActiveOperationCount, Is.Zero);
            Assert.That(gateway.DisposedOperationCount, Is.EqualTo(1));

            // A transport can still race a callback after disposal. Its result belongs
            // to the old authoritative state and must never reach its public owner.
            gateway.CompleteAbandonedUtterance();
            Assert.That(active.MoveNext(), Is.False);
            Assert.That(pending.MoveNext(), Is.False);
            Assert.That(activeCompletionCount, Is.Zero);
            Assert.That(pendingCompletionCount, Is.Zero);

            IEnumerator replacement = coordinator.GenerateUtterance(
                ResidentIds.Yaya,
                NpcExpressionTrigger.CommandAccepted,
                "replacement",
                value => replacementResult = value);
            while (replacement.MoveNext())
            {
            }

            Assert.That(replacementResult, Is.Not.Null);
            Assert.That(replacementResult.Succeeded, Is.True, replacementResult.Outcome.Message);
            Assert.That(replacementResult.ResidentId, Is.EqualTo(ResidentIds.Yaya));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
            Assert.That(coordinator.IsShutdown, Is.False);
        }

        [Test]
        public void FourResidents_RespectConfiguredConcurrency_AndShutdownAllFallbackLocally()
        {
            var transport = new BlockingTransport();
            var remote = new RemoteAiGatewayClient(
                "http://127.0.0.1:1",
                60,
                transport,
                new LocalAiGatewayClient());
            var coordinator = new AiRequestCoordinator(
                remote,
                2,
                new LocalAiGatewayClient());
            ResidentId[] residentIds =
            {
                ResidentIds.Yaya,
                ResidentIds.Amu,
                ResidentIds.Xiaosui,
                ResidentIds.Momo
            };
            var requests = new IEnumerator[residentIds.Length];
            var results = new AiGatewayResult<ResidentDecisionSpec>[residentIds.Length];
            for (int index = 0; index < residentIds.Length; index++)
            {
                int capturedIndex = index;
                requests[index] = coordinator.DecideResident(
                    CreateResidentDecisionRequest(residentIds[index], includeIdle: true),
                    value => results[capturedIndex] = value);
                Assert.That(requests[index].MoveNext(), Is.True);
            }

            for (int index = 0; index < requests.Length; index++)
            {
                Assert.That(requests[index].MoveNext(), Is.True);
            }

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(2));
            Assert.That(transport.MaximumObservedConcurrency, Is.EqualTo(2));
            coordinator.Shutdown();

            bool anyRunning = true;
            int guard = 0;
            while (anyRunning && guard++ < 20)
            {
                anyRunning = false;
                foreach (IEnumerator request in requests)
                {
                    if (request.MoveNext())
                    {
                        anyRunning = true;
                    }
                }
            }

            Assert.That(guard, Is.LessThan(20));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
            Assert.That(transport.ActiveOperationCount, Is.Zero);
            Assert.That(transport.CompletedOperationCount, Is.Zero);
            Assert.That(transport.DisposedOperationCount, Is.EqualTo(2));
            foreach (AiGatewayResult<ResidentDecisionSpec> result in results)
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
                Assert.That(result.Source, Is.EqualTo(AiGatewayMode.Local));
                Assert.That(result.Value.Intent, Is.EqualTo(ResidentHighLevelIntents.Idle));
            }
        }

        [Test]
        public void RemoteNestedTransport_CallerCancellationDisposesOnlyItsRequestAndReleasesOwner()
        {
            var transport = new BlockingTransport();
            var remote = new RemoteAiGatewayClient(
                "http://127.0.0.1:1",
                60,
                transport,
                new LocalAiGatewayClient());
            var coordinator = new AiRequestCoordinator(remote, 2);
            var firstCancellation = new AiRequestCancellation();
            int firstCompletions = 0;
            int secondCompletions = 0;
            int replacementCompletions = 0;
            IEnumerator first = coordinator.DecideResident(
                CreateResidentDecisionRequest(ResidentIds.Yaya, includeIdle: true),
                AiRequestPriority.Normal,
                firstCancellation,
                _ => firstCompletions++);
            IEnumerator second = coordinator.DecideResident(
                CreateResidentDecisionRequest(ResidentIds.Amu, includeIdle: true),
                _ => secondCompletions++);

            Assert.That(first.MoveNext(), Is.True);
            Assert.That(second.MoveNext(), Is.True);
            Assert.That(first.MoveNext(), Is.True);
            Assert.That(second.MoveNext(), Is.True);
            Assert.That(transport.ActiveOperationCount, Is.EqualTo(2));

            firstCancellation.Cancel();
            Assert.That(first.MoveNext(), Is.False);
            Assert.That(firstCompletions, Is.Zero);
            Assert.That(secondCompletions, Is.Zero);
            Assert.That(transport.ActiveOperationCount, Is.EqualTo(1));
            Assert.That(transport.DisposedOperationCount, Is.EqualTo(1));
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));

            IEnumerator replacement = coordinator.DecideResident(
                CreateResidentDecisionRequest(ResidentIds.Yaya, includeIdle: true),
                _ => replacementCompletions++);
            Assert.That(replacement.MoveNext(), Is.True);
            Assert.That(replacement.MoveNext(), Is.True);
            Assert.That(transport.ActiveOperationCount, Is.EqualTo(2),
                "Cancelling Yaya must release only Yaya's owner lock while Amu remains active.");

            ((IDisposable)second).Dispose();
            Assert.That(transport.ActiveOperationCount, Is.EqualTo(1));
            Assert.That(secondCompletions, Is.Zero);
            ((IDisposable)replacement).Dispose();
            Assert.That(transport.ActiveOperationCount, Is.Zero);
            Assert.That(transport.DisposedOperationCount, Is.EqualTo(3));
            Assert.That(replacementCompletions, Is.Zero);
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.PendingRequestCount, Is.Zero);
        }

        [Test]
        public void ResidentDecision_UsesRequestResidentAsItsCoordinatorOwner()
        {
            var gateway = new RecordingGatewayClient();
            var coordinator = new AiRequestCoordinator(gateway, 2);
            ResidentDecisionRequest decisionRequest = CreateResidentDecisionRequest();
            AiGatewayResult<ResidentDecisionSpec> result = null;
            IEnumerator request = coordinator.DecideResident(
                decisionRequest,
                value => result = value);

            Assert.That(request.MoveNext(), Is.True);
            Assert.That(request.MoveNext(), Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(gateway.StartedOperations, Is.EqualTo(new[] { "decision" }));

            Assert.That(request.MoveNext(), Is.False);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.True, result.Outcome.Message);
            Assert.That(result.ResidentId, Is.EqualTo(ResidentIds.Xiaosui));
            Assert.That(result.Value.ResidentId, Is.EqualTo(ResidentIds.Xiaosui));
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
        }

        private static ResidentDecisionRequest CreateResidentDecisionRequest()
        {
            return CreateResidentDecisionRequest(ResidentIds.Xiaosui, includeIdle: false);
        }

        private static ResidentDecisionRequest CreateResidentDecisionRequest(
            ResidentId residentId,
            bool includeIdle)
        {
            ResidentDefinition definition = null;
            foreach (ResidentDefinition candidate in ResidentDefinition.TownResidents)
            {
                if (candidate.ResidentId == residentId)
                {
                    definition = candidate;
                    break;
                }
            }

            Assert.That(definition, Is.Not.Null);
            ResidentId relationshipTarget = residentId == ResidentIds.Yaya
                ? ResidentIds.Amu
                : ResidentIds.Yaya;
            var context = new ResidentContext(
                residentId,
                ResidentPersonaSnapshot.FromDefinition(definition),
                "idle",
                new[]
                {
                    new RelationshipSnapshot(
                        residentId,
                        relationshipTarget,
                        0,
                        0)
                },
                Array.Empty<ResidentMemorySnapshot>());
            return new ResidentDecisionRequest(
                residentId,
                context,
                "harvest known",
                includeIdle
                    ? new[]
                    {
                        ResidentHighLevelIntents.ProposeTownEvent,
                        ResidentHighLevelIntents.Idle
                    }
                    : new[] { ResidentHighLevelIntents.ProposeTownEvent },
                Array.Empty<ResidentId>());
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
            private readonly bool blockFirstUtterance;
            private int activeOperations;
            private int utteranceStartCount;
            private Action abandonedUtteranceCompletion;

            public RecordingGatewayClient(bool blockFirstUtterance = false)
            {
                this.blockFirstUtterance = blockFirstUtterance;
            }

            public AiGatewayMode ConfiguredMode => AiGatewayMode.Remote;

            public AiGatewayMode ActiveMode => AiGatewayMode.Remote;

            public IReadOnlyList<string> StartedOperations => startedOperations;

            public int MaximumObservedConcurrency { get; private set; }

            public int CompletedOperationCount { get; private set; }

            public int ActiveOperationCount => activeOperations;

            public int DisposedOperationCount { get; private set; }

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
                if (blockFirstUtterance && utteranceStartCount++ == 0)
                {
                    return BlockUtterance(
                        residentId,
                        new NpcExpression(trigger, NpcMood.Focused, "!", context),
                        completed);
                }

                return Complete(
                    "utterance",
                    residentId,
                    new NpcExpression(trigger, NpcMood.Focused, "!", context),
                    completed);
            }

            public void CompleteAbandonedUtterance()
            {
                Assert.That(abandonedUtteranceCompletion, Is.Not.Null);
                abandonedUtteranceCompletion();
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

            public IEnumerator DecideResident(
                ResidentDecisionRequest request,
                Action<AiGatewayResult<ResidentDecisionSpec>> completed)
            {
                var decision = new ResidentDecisionSpec(
                    request.ResidentId,
                    request.AllowedIntents[0],
                    targetResidentId: null,
                    "test decision",
                    "mock");
                return Complete("decision", request.ResidentId, decision, completed);
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
                try
                {
                    yield return null;
                    CompletedOperationCount++;
                    completed(AiGatewayResult<T>.Success(
                        residentId,
                        value,
                        AiGatewayMode.Remote));
                }
                finally
                {
                    activeOperations--;
                    DisposedOperationCount++;
                }
            }

            private IEnumerator BlockUtterance(
                ResidentId residentId,
                NpcExpression value,
                Action<AiGatewayResult<NpcExpression>> completed)
            {
                startedOperations.Add("utterance");
                activeOperations++;
                MaximumObservedConcurrency = Math.Max(
                    MaximumObservedConcurrency,
                    activeOperations);
                abandonedUtteranceCompletion = () => completed(
                    AiGatewayResult<NpcExpression>.Success(
                        residentId,
                        value,
                        AiGatewayMode.Remote));
                try
                {
                    while (true)
                    {
                        yield return null;
                    }
                }
                finally
                {
                    activeOperations--;
                    DisposedOperationCount++;
                }
            }
        }

        private sealed class BlockingTransport : IAiGatewayTransport
        {
            private int activeOperations;

            public int ActiveOperationCount => activeOperations;

            public int CompletedOperationCount { get; private set; }

            public int DisposedOperationCount { get; private set; }

            public int MaximumObservedConcurrency { get; private set; }

            public IEnumerator PostJson(
                string url,
                string json,
                int timeoutSeconds,
                Action<AiGatewayHttpResult> completed)
            {
                activeOperations++;
                MaximumObservedConcurrency = Math.Max(
                    MaximumObservedConcurrency,
                    activeOperations);
                try
                {
                    while (true)
                    {
                        yield return null;
                    }
                }
                finally
                {
                    activeOperations--;
                    DisposedOperationCount++;
                }
            }
        }
    }
}
