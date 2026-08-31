using System.Collections.Generic;
using System.Linq;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Social;
using AIFarm.Town;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class OfflineSocialSystemTests
    {
        private static readonly TownLocationId Plaza =
            new TownLocationId("location-social-test");

        [Test]
        public void TwoIdleResidents_CanEstablishConversation()
        {
            SocialFixture fixture = CreateFixture();
            SocialOpportunity opportunity = Detect(
                fixture,
                Status(ResidentIds.Yaya),
                Status(ResidentIds.Amu));

            ActionResult started = fixture.Coordinator.TryStartConversation(
                opportunity.FirstResidentId,
                opportunity.SecondResidentId,
                "conversation-seat-01",
                "conversation-seat-02",
                targetSentenceCount: 4,
                monotonicSeconds: 10d,
                out ConversationSession session);

            Assert.That(started.Succeeded, Is.True, started.Message);
            Assert.That(session, Is.Not.Null);
            Assert.That(session.State, Is.EqualTo(ConversationState.Active));
            Assert.That(session.FirstResidentId, Is.Not.EqualTo(session.SecondResidentId));
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.EqualTo(2));
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(2));
        }

        [Test]
        public void ThirdResident_CannotTakeResidentAlreadyInConversation()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession first = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-a",
                "seat-b");

            ActionResult attempted = fixture.Coordinator.TryStartConversation(
                ResidentIds.Xiaosui,
                ResidentIds.Amu,
                "seat-c",
                "seat-d",
                targetSentenceCount: 2,
                monotonicSeconds: 0.1d,
                out ConversationSession rejected);

            Assert.That(first.State, Is.EqualTo(ConversationState.Active));
            Assert.That(attempted.Failed, Is.True);
            Assert.That(rejected, Is.Null);
            Assert.That(fixture.ParticipantLock.IsLocked(ResidentIds.Xiaosui), Is.False);
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.EqualTo(1));
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(2));
        }

        [Test]
        public void Resident_CannotBelongToTwoConversationSessions()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession first = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-a",
                "seat-b");

            ActionResult second = fixture.Coordinator.TryStartConversation(
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "seat-c",
                "seat-d",
                targetSentenceCount: 2,
                monotonicSeconds: 0.1d,
                out _);

            Assert.That(second.Failed, Is.True);
            Assert.That(
                fixture.ParticipantLock.TryGetConversationId(
                    ResidentIds.Yaya,
                    out ConversationId activeId),
                Is.True);
            Assert.That(activeId, Is.EqualTo(first.ConversationId));
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.EqualTo(1));
        }

        [Test]
        public void Timeout_AlwaysReleasesParticipantLocksAndPositionReservations()
        {
            SocialFixture fixture = CreateFixture(timeoutSeconds: 2d);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-timeout-a",
                "seat-timeout-b",
                monotonicSeconds: 5d);

            int timedOut = fixture.Coordinator.TickTimeouts(7d);

            Assert.That(timedOut, Is.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(ConversationState.TimedOut));
            Assert.That(session.EndReason, Is.EqualTo(ConversationEndReason.TimedOut));
            Assert.That(fixture.Coordinator.ActiveSessionCount, Is.Zero);
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
        }

        [Test]
        public void CompletedOutcome_IsStoredInEachParticipantsIndependentMemoryOnly()
        {
            SocialFixture fixture = CreateFixture(
                outcomeSelector: _ => ConversationOutcome.Positive);
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-memory-a",
                "seat-memory-b",
                targetSentenceCount: 2);

            Complete(fixture, session);
            ResidentRuntimeState first = Runtime(fixture, ResidentIds.Yaya);
            ResidentRuntimeState second = Runtime(fixture, ResidentIds.Amu);
            ResidentRuntimeState third = Runtime(fixture, ResidentIds.Xiaosui);

            Assert.That(session.Outcome, Is.EqualTo(ConversationOutcome.Positive));
            Assert.That(first.Memories.Entries, Has.Count.EqualTo(1));
            Assert.That(second.Memories.Entries, Has.Count.EqualTo(1));
            Assert.That(third.Memories.Entries, Is.Empty);
            Assert.That(first.Memories, Is.Not.SameAs(second.Memories));
            Assert.That(
                first.Memories.Entries.Single().OwnerResidentId,
                Is.EqualTo(ResidentIds.Yaya));
            Assert.That(
                second.Memories.Entries.Single().OwnerResidentId,
                Is.EqualTo(ResidentIds.Amu));
            Assert.That(
                first.Memories.Entries.Single().SourceEventKind,
                Is.EqualTo(WorldEventKind.ConversationCompleted));
            Assert.That(
                second.Memories.Entries.Single().SourceEventKind,
                Is.EqualTo(WorldEventKind.ConversationCompleted));
        }

        [Test]
        public void HelpfulOutcome_IncreasesTrustByFixedRuleForBothDirections()
        {
            SocialFixture fixture = CreateFixture(
                outcomeSelector: _ => ConversationOutcome.Helpful);
            fixture.Graph.TryGetRelationship(
                ResidentIds.Yaya,
                ResidentIds.Amu,
                out RelationshipState yayaBefore);
            fixture.Graph.TryGetRelationship(
                ResidentIds.Amu,
                ResidentIds.Yaya,
                out RelationshipState amuBefore);
            int initialYayaTrust = yayaBefore.Trust;
            int initialAmuTrust = amuBefore.Trust;
            ConversationSession session = Start(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-helpful-a",
                "seat-helpful-b",
                targetSentenceCount: 3);

            Complete(fixture, session);

            Assert.That(
                yayaBefore.Trust,
                Is.EqualTo(initialYayaTrust + ConversationOutcomeApplier.HelpfulTrustIncrease));
            Assert.That(
                amuBefore.Trust,
                Is.EqualTo(initialAmuTrust + ConversationOutcomeApplier.HelpfulTrustIncrease));
            Assert.That(yayaBefore.RelationVersion, Is.EqualTo(1));
            Assert.That(amuBefore.RelationVersion, Is.EqualTo(1));
        }

        [Test]
        public void NetworkUnavailable_EntireConversationCompletesUsingOnlyLocalTemplates()
        {
            SocialFixture fixture = CreateFixture();
            ConversationSession session = Start(
                fixture,
                ResidentIds.Xiaosui,
                ResidentIds.Momo,
                "seat-offline-a",
                "seat-offline-b",
                targetSentenceCount: ConversationSession.MaximumSentenceCount);

            Complete(fixture, session);

            Assert.That(session.State, Is.EqualTo(ConversationState.Completed));
            Assert.That(session.Utterances, Has.Count.EqualTo(6));
            Assert.That(
                session.Utterances.All(line => !string.IsNullOrWhiteSpace(line.Text)),
                Is.True);
            Assert.That(
                System.Enum.IsDefined(typeof(ConversationOutcome), session.Outcome.Value),
                Is.True);
            Assert.That(fixture.ParticipantLock.LockedResidentCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
        }

        [Test]
        public void LocalTemplates_ChangeWithBothNamesAndPersonas()
        {
            SocialFixture firstFixture = CreateFixture();
            ConversationSession firstSession = Start(
                firstFixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                "seat-template-a",
                "seat-template-b");
            Assert.That(
                firstFixture.Coordinator.AdvanceConversation(
                    firstSession.ConversationId,
                    0.1d,
                    10d,
                    out ConversationUtterance firstLine).Succeeded,
                Is.True);

            SocialFixture secondFixture = CreateFixture();
            ConversationSession secondSession = Start(
                secondFixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                "seat-template-c",
                "seat-template-d");
            Assert.That(
                secondFixture.Coordinator.AdvanceConversation(
                    secondSession.ConversationId,
                    0.1d,
                    10d,
                    out ConversationUtterance secondLine).Succeeded,
                Is.True);

            Assert.That(firstLine.Text, Is.Not.EqualTo(secondLine.Text));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Yaya.DisplayName));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Amu.DisplayName));
            Assert.That(secondLine.Text, Does.Contain(ResidentDefinition.Yaya.DisplayName));
            Assert.That(secondLine.Text, Does.Contain(ResidentDefinition.Xiaosui.DisplayName));
            Assert.That(firstLine.Text, Does.Contain(ResidentDefinition.Yaya.Persona.Role));
        }

        [Test]
        public void OpportunityDetector_EnforcesCooldownAndPriorityExclusions()
        {
            SocialFixture fixture = CreateFixture(cooldownGameSeconds: 100d);
            SocialOpportunity first = Detect(
                fixture,
                Status(ResidentIds.Yaya),
                Status(ResidentIds.Amu));
            Assert.That(
                fixture.Detector.RecordConversationStarted(first, 50d).Succeeded,
                Is.True);

            ActionResult coolingDown = fixture.Detector.TryDetect(
                new[] { Status(ResidentIds.Yaya), Status(ResidentIds.Amu) },
                149d,
                fixture.ParticipantLock,
                out _);
            ActionResult priorityBlocked = fixture.Detector.TryDetect(
                new[]
                {
                    Status(ResidentIds.Xiaosui, emergencyFarmWork: true),
                    Status(ResidentIds.Momo, goingHomeToSleep: true)
                },
                200d,
                fixture.ParticipantLock,
                out _);
            ActionResult playerInstructionBlocked = fixture.Detector.TryDetect(
                new[]
                {
                    Status(ResidentIds.Xiaosui, playerInstruction: true),
                    Status(ResidentIds.Momo)
                },
                200d,
                fixture.ParticipantLock,
                out _);

            Assert.That(coolingDown.Failed, Is.True);
            Assert.That(priorityBlocked.Failed, Is.True);
            Assert.That(playerInstructionBlocked.Failed, Is.True);
            Assert.That(fixture.Detector.IsCooldownComplete(ResidentIds.Yaya, 150d), Is.True);
            Assert.That(fixture.Detector.IsCooldownComplete(ResidentIds.Yaya, 0d), Is.True);
        }

        private static SocialFixture CreateFixture(
            double timeoutSeconds = ConversationCoordinator.DefaultTimeoutSeconds,
            double cooldownGameSeconds = SocialOpportunityDetector.DefaultCooldownGameSeconds,
            System.Func<ConversationSession, ConversationOutcome> outcomeSelector = null)
        {
            var registry = new ResidentRegistry();
            foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
            {
                ActionResult registered = registry.Register(
                    definition,
                    new ResidentRuntimeState(definition));
                Assert.That(registered.Succeeded, Is.True, registered.Message);
            }

            var graph = new SocialGraph(registry.ResidentIds);
            var participantLock = new ConversationParticipantLock();
            var reservations = new InteractionPointReservationService();
            var templates = new LocalConversationTemplateService(outcomeSelector);
            var applier = new ConversationOutcomeApplier(registry, graph);
            var coordinator = new ConversationCoordinator(
                registry,
                participantLock,
                reservations,
                templates,
                applier,
                timeoutSeconds);
            return new SocialFixture(
                registry,
                graph,
                participantLock,
                reservations,
                new SocialOpportunityDetector(cooldownGameSeconds),
                coordinator);
        }

        private static SocialOpportunity Detect(
            SocialFixture fixture,
            params SocialResidentStatus[] statuses)
        {
            ActionResult detected = fixture.Detector.TryDetect(
                statuses,
                gameSeconds: 0d,
                fixture.ParticipantLock,
                out SocialOpportunity opportunity);
            Assert.That(detected.Succeeded, Is.True, detected.Message);
            return opportunity;
        }

        private static SocialResidentStatus Status(
            ResidentId residentId,
            bool emergencyFarmWork = false,
            bool playerInstruction = false,
            bool goingHomeToSleep = false)
        {
            return new SocialResidentStatus(
                residentId,
                Plaza,
                isIdle: true,
                hasActiveAction: false,
                isPerformingEmergencyFarmWork: emergencyFarmWork,
                hasPlayerInstruction: playerInstruction,
                isGoingHomeToSleep: goingHomeToSleep);
        }

        private static ConversationSession Start(
            SocialFixture fixture,
            ResidentId firstResidentId,
            ResidentId secondResidentId,
            string firstPoint,
            string secondPoint,
            int targetSentenceCount = 4,
            double monotonicSeconds = 0d)
        {
            ActionResult started = fixture.Coordinator.TryStartConversation(
                firstResidentId,
                secondResidentId,
                firstPoint,
                secondPoint,
                targetSentenceCount,
                monotonicSeconds,
                out ConversationSession session);
            Assert.That(started.Succeeded, Is.True, started.Message);
            return session;
        }

        private static void Complete(SocialFixture fixture, ConversationSession session)
        {
            double monotonicSeconds = session.StartedAtMonotonicSeconds;
            while (session.State == ConversationState.Active)
            {
                monotonicSeconds += 0.1d;
                ActionResult advanced = fixture.Coordinator.AdvanceConversation(
                    session.ConversationId,
                    monotonicSeconds,
                    gameSeconds: 100d + monotonicSeconds,
                    out _);
                Assert.That(advanced.Succeeded, Is.True, advanced.Message);
            }
        }

        private static ResidentRuntimeState Runtime(
            SocialFixture fixture,
            ResidentId residentId)
        {
            ActionResult resolved = fixture.Registry.TryGetRuntimeState(
                residentId,
                out ResidentRuntimeState runtime);
            Assert.That(resolved.Succeeded, Is.True, resolved.Message);
            return runtime;
        }

        private sealed class SocialFixture
        {
            public SocialFixture(
                ResidentRegistry registry,
                SocialGraph graph,
                ConversationParticipantLock participantLock,
                InteractionPointReservationService reservations,
                SocialOpportunityDetector detector,
                ConversationCoordinator coordinator)
            {
                Registry = registry;
                Graph = graph;
                ParticipantLock = participantLock;
                Reservations = reservations;
                Detector = detector;
                Coordinator = coordinator;
            }

            public ResidentRegistry Registry { get; }

            public SocialGraph Graph { get; }

            public ConversationParticipantLock ParticipantLock { get; }

            public InteractionPointReservationService Reservations { get; }

            public SocialOpportunityDetector Detector { get; }

            public ConversationCoordinator Coordinator { get; }
        }
    }
}
