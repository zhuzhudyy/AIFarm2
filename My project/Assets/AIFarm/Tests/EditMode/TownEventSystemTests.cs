using System;
using System.Collections.Generic;
using System.Linq;
using AIFarm.Ai;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class TownEventSystemTests
    {
        private const double FirstDayHarvestDinnerStart = 18d * 60d * 60d;

        [Test]
        public void ValidProposal_MapsToFixedHarvestDinnerAtNextEighteenInPlaza()
        {
            var fixture = new Fixture();
            const double proposedAt = 17d * 60d * 60d + 59d * 60d;
            TownEventProposal proposal = CreateValidProposal(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                proposedAt,
                "conversation-harvest-fixed",
                "fact-harvest-fixed");

            ActionResult scheduled = fixture.Coordinator.TrySchedule(
                proposal,
                out TownEventSession session);

            Assert.That(scheduled.Succeeded, Is.True, scheduled.Message);
            Assert.That(proposal.EventType, Is.EqualTo(TownEventType.HarvestDinner));
            Assert.That(session.EventType, Is.EqualTo(TownEventType.HarvestDinner));
            Assert.That(
                session.DefinitionId,
                Is.EqualTo(TownEventCoordinator.HarvestDinnerDefinitionId));
            Assert.That(session.ConversationSourceResidentId, Is.EqualTo(ResidentIds.Amu));
            Assert.That(
                session.LocationId,
                Is.EqualTo(TownEventCoordinator.HarvestDinnerLocationId));
            Assert.That(session.ScheduledStartGameSeconds, Is.EqualTo(FirstDayHarvestDinnerStart));
            Assert.That(
                TownEventCoordinator.ResolveNextHarvestDinnerStart(
                    FirstDayHarvestDinnerStart - 1d),
                Is.EqualTo(FirstDayHarvestDinnerStart));
            Assert.That(
                TownEventCoordinator.ResolveNextHarvestDinnerStart(
                    FirstDayHarvestDinnerStart),
                Is.EqualTo(FirstDayHarvestDinnerStart));
            Assert.That(
                TownEventCoordinator.ResolveNextHarvestDinnerStart(
                    FirstDayHarvestDinnerStart + 1d),
                Is.EqualTo(
                    TownEventCoordinator.GameSecondsPerDay +
                    FirstDayHarvestDinnerStart));
            Assert.That(session.Invitations.Select(value => value.ResidentId),
                Is.EquivalentTo(ResidentIds.TownResidents));
            fixture.AssertProjectionSucceeded();
        }

        [Test]
        public void Schedule_RejectsMissingOrNonConversationSourceKnowledge()
        {
            const double proposedAt = 17d * 60d * 60d;
            var missingFixture = new Fixture();
            TownEventProposal missingProposal = CreateProposal(
                ResidentIds.Yaya,
                "knowledge:resident-001:missing",
                "conversation-harvest-missing",
                proposedAt);

            ActionResult missingResult = missingFixture.Coordinator.TrySchedule(
                missingProposal,
                out TownEventSession missingSession);

            Assert.That(missingResult.Failed, Is.True);
            Assert.That(missingSession, Is.Null);
            Assert.That(missingFixture.Coordinator.CurrentSession, Is.Null);
            Assert.That(missingFixture.Events.Entries, Is.Empty);

            var wrongSourceFixture = new Fixture();
            ResidentRuntimeState yaya = wrongSourceFixture.Runtime(ResidentIds.Yaya);
            ActionResult perceived = yaya.Memories.AddObservation(
                ResidentIds.Yaya,
                proposedAt - 1d,
                "芽芽亲眼看见胡萝卜收获，但这不是对话来源。",
                9,
                WorldEventKind.GoalCompleted,
                MemorySourceKind.Perception,
                sourceEventId: "perception-harvest-not-conversation",
                rootFactId: "fact-harvest-not-conversation",
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: new[] { "harvest", "carrot" },
                isShareable: true,
                entry: out MemoryEntry perceivedKnowledge);
            Assert.That(perceived.Succeeded, Is.True, perceived.Message);
            TownEventProposal wrongSourceProposal = CreateProposal(
                ResidentIds.Yaya,
                perceivedKnowledge.KnowledgeId,
                perceivedKnowledge.SourceEventId,
                proposedAt);

            ActionResult wrongSourceResult = wrongSourceFixture.Coordinator.TrySchedule(
                wrongSourceProposal,
                out TownEventSession wrongSourceSession);

            Assert.That(wrongSourceResult.Failed, Is.True);
            Assert.That(wrongSourceSession, Is.Null);
            Assert.That(wrongSourceFixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(wrongSourceFixture.Events.Entries, Is.Empty);
            wrongSourceFixture.AssertProjectionSucceeded();
        }

        [Test]
        public void Coordinator_AllowsOnlyOneNonTerminalTownEvent()
        {
            var fixture = new Fixture();
            const double proposedAt = 17d * 60d * 60d;
            TownEventProposal first = CreateValidProposal(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                proposedAt,
                "conversation-harvest-first",
                "fact-harvest-first");
            TownEventProposal second = CreateValidProposal(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Xiaosui,
                proposedAt + 1d,
                "conversation-harvest-second",
                "fact-harvest-second");

            Assert.That(
                fixture.Coordinator.TrySchedule(first, out TownEventSession firstSession).Succeeded,
                Is.True);
            ActionResult duplicate = fixture.Coordinator.TrySchedule(second, out _);

            Assert.That(duplicate.Failed, Is.True);
            Assert.That(fixture.Coordinator.ActiveTownEventCount, Is.EqualTo(1));
            Assert.That(fixture.Coordinator.CurrentSession, Is.SameAs(firstSession));
            Assert.That(
                fixture.Events.Entries.Count(
                    entry => entry.Kind == WorldEventKind.TownEventProposed),
                Is.EqualTo(1));
            fixture.AssertProjectionSucceeded();
        }

        [Test]
        public void ConsumedHarvestFact_RemainsRejectedAfterTransientCoordinatorReset()
        {
            var fixture = new Fixture();
            const double proposedAt = 17d * 60d * 60d;
            TownEventProposal proposal = CreateValidProposal(
                fixture,
                ResidentIds.Xiaosui,
                ResidentIds.Yaya,
                proposedAt,
                "conversation-harvest-persisted-consumption",
                "fact-harvest-persisted-consumption");
            Assert.That(
                fixture.Coordinator.TrySchedule(proposal, out _).Succeeded,
                Is.True);
            int proposedEventCount = fixture.Events.Entries.Count(entry =>
                entry.Kind == WorldEventKind.TownEventProposed);

            Assert.That(fixture.Coordinator.Reset().Succeeded, Is.True);
            ActionResult replayed = fixture.Coordinator.TrySchedule(proposal, out _);

            Assert.That(replayed.Failed, Is.True);
            Assert.That(fixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(
                fixture.Events.Entries.Count(entry =>
                    entry.Kind == WorldEventKind.TownEventProposed),
                Is.EqualTo(proposedEventCount));
            Assert.That(
                fixture.Runtime(ResidentIds.Xiaosui).Memories.Entries.Any(entry =>
                    entry.RootFactId == "fact-harvest-persisted-consumption" &&
                    string.IsNullOrEmpty(entry.ParentKnowledgeId) &&
                    entry.Tags.Contains("town-event-proposal-consumed")),
                Is.True);
            fixture.AssertProjectionSucceeded();
        }

        [Test]
        public void Invitations_CanBeAcceptedOrDeclined_AndRequireAtLeastTwoResidents()
        {
            var validFixture = new Fixture();
            TownEventSession validSession = ScheduleAtFirstDayDinner(validFixture);

            Assert.That(
                validFixture.Coordinator.DecideInvitation(
                    ResidentIds.Amu,
                    TownEventInvitationStatus.Accepted).Succeeded,
                Is.True);
            Assert.That(
                validFixture.Coordinator.DecideInvitation(
                    ResidentIds.Xiaosui,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                validFixture.Coordinator.DecideInvitation(
                    ResidentIds.Momo,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);

            ActionResult gathering = validFixture.Coordinator.TryBeginGathering(
                validSession.ScheduledStartGameSeconds,
                monotonicSeconds: 0d);

            Assert.That(gathering.Succeeded, Is.True, gathering.Message);
            Assert.That(validSession.State, Is.EqualTo(TownEventState.Gathering));
            Assert.That(
                validSession.ParticipantResidentIds,
                Is.EquivalentTo(new[] { ResidentIds.Yaya, ResidentIds.Amu }));
            Assert.That(validFixture.Reservations.ReservationCount, Is.EqualTo(2));

            var insufficientFixture = new Fixture();
            TownEventSession insufficientSession = ScheduleAtFirstDayDinner(insufficientFixture);
            foreach (ResidentId residentId in new[]
            {
                ResidentIds.Amu,
                ResidentIds.Xiaosui,
                ResidentIds.Momo
            })
            {
                Assert.That(
                    insufficientFixture.Coordinator.DecideInvitation(
                        residentId,
                        TownEventInvitationStatus.Declined).Succeeded,
                    Is.True);
            }

            ActionResult insufficient = insufficientFixture.Coordinator.TryBeginGathering(
                insufficientSession.ScheduledStartGameSeconds,
                monotonicSeconds: 0d);

            Assert.That(insufficient.Failed, Is.True);
            Assert.That(insufficientSession.State, Is.EqualTo(TownEventState.Cancelled));
            Assert.That(insufficientFixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(insufficientFixture.Reservations.ReservationCount, Is.Zero);
            validFixture.AssertProjectionSucceeded();
            insufficientFixture.AssertProjectionSucceeded();
        }

        [Test]
        public void Gathering_ReservesDifferentAttendancePointForEveryParticipant()
        {
            var fixture = new Fixture();
            TownEventSession session = ScheduleAtFirstDayDinner(fixture);
            foreach (ResidentId residentId in new[]
            {
                ResidentIds.Amu,
                ResidentIds.Xiaosui,
                ResidentIds.Momo
            })
            {
                Assert.That(
                    fixture.Coordinator.DecideInvitation(
                        residentId,
                        TownEventInvitationStatus.Accepted).Succeeded,
                    Is.True);
            }

            ActionResult gathering = fixture.Coordinator.TryBeginGathering(
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 0d);

            Assert.That(gathering.Succeeded, Is.True, gathering.Message);
            var pointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                Assert.That(
                    session.TryGetReservation(
                        residentId,
                        out InteractionPointReservation reservation),
                    Is.True);
                Assert.That(reservation.ResidentId, Is.EqualTo(residentId));
                Assert.That(
                    reservation.InteractionPointId,
                    Is.EqualTo(fixture.Coordinator.GetAttendancePointId(residentId)));
                Assert.That(pointIds.Add(reservation.InteractionPointId), Is.True);
                Assert.That(
                    fixture.Reservations.TryGetOwner(
                        reservation.InteractionPointId,
                        0d,
                        out ResidentId owner),
                    Is.True);
                Assert.That(owner, Is.EqualTo(residentId));
            }

            Assert.That(pointIds, Has.Count.EqualTo(4));
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(4));
            fixture.AssertProjectionSucceeded();
        }

        [Test]
        public void CompletedEvent_ProjectsPrivateAttendedMemoryOnlyToActualParticipants()
        {
            var fixture = new Fixture();
            TownEventSession session = ScheduleAtFirstDayDinner(fixture);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Xiaosui,
                    TownEventInvitationStatus.Accepted).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Amu,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Momo,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.TryBeginGathering(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 0d).Succeeded,
                Is.True);

            ActionResult yayaArrived = fixture.Coordinator.MarkArrived(
                ResidentIds.Yaya,
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 1d);
            Assert.That(yayaArrived.Succeeded, Is.True, yayaArrived.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Gathering));
            ActionResult xiaosuiArrived = fixture.Coordinator.MarkArrived(
                ResidentIds.Xiaosui,
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 1.1d);
            Assert.That(xiaosuiArrived.Succeeded, Is.True, xiaosuiArrived.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Active));

            ActionResult completed = fixture.Coordinator.Tick(
                session.ActiveStartGameSeconds +
                    TownEventCoordinator.DefaultDurationGameSeconds,
                monotonicSeconds: 2d);

            Assert.That(completed.Succeeded, Is.True, completed.Message);
            Assert.That(session.State, Is.EqualTo(TownEventState.Completed));
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
            fixture.AssertProjectionSucceeded();

            var actualParticipants = new HashSet<ResidentId>
            {
                ResidentIds.Yaya,
                ResidentIds.Xiaosui
            };
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                IReadOnlyList<MemoryEntry> memories = fixture.Runtime(residentId).Memories.Entries;
                Assert.That(
                    memories.Count(entry =>
                        entry.SourceEventKind == WorldEventKind.TownEventProposed &&
                        entry.SourceKind == MemorySourceKind.PublicTownEvent),
                    Is.EqualTo(1),
                    $"{residentId} should receive the public invitation projection.");
                Assert.That(
                    memories.Count(entry =>
                        entry.SourceEventKind == WorldEventKind.TownEventStarted),
                    Is.EqualTo(1),
                    $"{residentId} should receive the public start projection.");

                MemoryEntry[] attended = memories.Where(entry =>
                    entry.SourceEventKind == WorldEventKind.TownEventCompleted &&
                    entry.Tags.Contains("attended")).ToArray();
                if (actualParticipants.Contains(residentId))
                {
                    Assert.That(attended, Has.Length.EqualTo(1));
                    Assert.That(attended[0].OwnerResidentId, Is.EqualTo(residentId));
                    Assert.That(attended[0].SourceKind, Is.EqualTo(MemorySourceKind.Perception));
                }
                else
                {
                    Assert.That(
                        attended,
                        Is.Empty,
                        $"{residentId} did not attend and must not receive a private attended memory.");
                }
            }
        }

        [Test]
        public void GatheringTimeout_ReleasesEveryAttendanceReservation()
        {
            var fixture = new Fixture(
                durationGameSeconds: 60d,
                arrivalTimeoutSeconds: 10d,
                reservationLeaseSeconds: 5d);
            TownEventSession session = ScheduleAtFirstDayDinner(fixture);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Amu,
                    TownEventInvitationStatus.Accepted).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Xiaosui,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Momo,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.TryBeginGathering(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 0d).Succeeded,
                Is.True);
            string[] reservedPointIds = session.ParticipantResidentIds
                .Select(fixture.Coordinator.GetAttendancePointId)
                .ToArray();
            Assert.That(
                fixture.Coordinator.MarkArrived(
                    ResidentIds.Yaya,
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 1d).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.Tick(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 4d).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.Tick(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 8d).Succeeded,
                Is.True);

            ActionResult timedOut = fixture.Coordinator.Tick(
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 10d);

            Assert.That(timedOut.Failed, Is.True);
            Assert.That(session.State, Is.EqualTo(TownEventState.Cancelled));
            StringAssert.Contains("timed out", session.EndReason);
            Assert.That(fixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
            foreach (string pointId in reservedPointIds)
            {
                Assert.That(
                    fixture.Reservations.TryGetOwner(pointId, 10d, out _),
                    Is.False);
            }

            fixture.AssertProjectionSucceeded();
            foreach (ResidentId residentId in ResidentIds.TownResidents)
            {
                IReadOnlyList<MemoryEntry> memories = fixture.Runtime(residentId).Memories.Entries;
                Assert.That(
                    memories.Any(entry =>
                        entry.SourceEventKind == WorldEventKind.TownEventCancelled),
                    Is.True);
                Assert.That(
                    memories.Any(entry =>
                        entry.SourceEventKind == WorldEventKind.TownEventCompleted),
                    Is.False);
            }
        }

        [Test]
        public void ArrivalAtDeadline_CancelsInsteadOfStartingLate()
        {
            var fixture = new Fixture(
                durationGameSeconds: 60d,
                arrivalTimeoutSeconds: 10d,
                reservationLeaseSeconds: 5d);
            TownEventSession session = ScheduleAtFirstDayDinner(fixture);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Amu,
                    TownEventInvitationStatus.Accepted).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Xiaosui,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Momo,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.TryBeginGathering(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 0d).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.Tick(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 4d).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.Tick(
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 8d).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.MarkArrived(
                    ResidentIds.Yaya,
                    session.ScheduledStartGameSeconds,
                    monotonicSeconds: 9d).Succeeded,
                Is.True);

            ActionResult lateArrival = fixture.Coordinator.MarkArrived(
                ResidentIds.Amu,
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 10d);

            Assert.That(lateArrival.Failed, Is.True);
            Assert.That(session.State, Is.EqualTo(TownEventState.Cancelled));
            Assert.That(fixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.Zero);
            fixture.AssertProjectionSucceeded();
        }

        [Test]
        public void InitialSeatConflict_CancelsWithoutLeakingEventReservations()
        {
            var fixture = new Fixture();
            TownEventSession session = ScheduleAtFirstDayDinner(fixture);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Amu,
                    TownEventInvitationStatus.Accepted).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Xiaosui,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            Assert.That(
                fixture.Coordinator.DecideInvitation(
                    ResidentIds.Momo,
                    TownEventInvitationStatus.Declined).Succeeded,
                Is.True);
            string blockedPoint = fixture.Coordinator.GetAttendancePointId(ResidentIds.Yaya);
            Assert.That(
                fixture.Reservations.TryReserve(
                    ResidentIds.Momo,
                    blockedPoint,
                    nowSeconds: 0d,
                    leaseSeconds: 30d,
                    out InteractionPointReservation externalReservation).Succeeded,
                Is.True);

            ActionResult gathering = fixture.Coordinator.TryBeginGathering(
                session.ScheduledStartGameSeconds,
                monotonicSeconds: 0d);

            Assert.That(gathering.Failed, Is.True);
            Assert.That(session.State, Is.EqualTo(TownEventState.Cancelled));
            Assert.That(fixture.Coordinator.ActiveTownEventCount, Is.Zero);
            Assert.That(fixture.Reservations.ReservationCount, Is.EqualTo(1));
            Assert.That(
                fixture.Reservations.TryGetOwner(
                    blockedPoint,
                    nowSeconds: 0d,
                    out ResidentId owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(ResidentIds.Momo));
            Assert.That(externalReservation.ResidentId, Is.EqualTo(ResidentIds.Momo));
            fixture.AssertProjectionSucceeded();
        }

        private static TownEventSession ScheduleAtFirstDayDinner(Fixture fixture)
        {
            TownEventProposal proposal = CreateValidProposal(
                fixture,
                ResidentIds.Yaya,
                ResidentIds.Amu,
                proposedAtGameSeconds: 17d * 60d * 60d,
                sourceConversationId: "conversation-harvest-default",
                rootFactId: "fact-harvest-default");
            ActionResult scheduled = fixture.Coordinator.TrySchedule(
                proposal,
                out TownEventSession session);
            Assert.That(scheduled.Succeeded, Is.True, scheduled.Message);
            Assert.That(session.ScheduledStartGameSeconds, Is.EqualTo(FirstDayHarvestDinnerStart));
            return session;
        }

        private static TownEventProposal CreateValidProposal(
            Fixture fixture,
            ResidentId ownerResidentId,
            ResidentId sourceResidentId,
            double proposedAtGameSeconds,
            string sourceConversationId,
            string rootFactId)
        {
            ResidentRuntimeState sourceRuntime = fixture.Runtime(sourceResidentId);
            ActionResult sourceStored = sourceRuntime.Memories.AddObservation(
                sourceResidentId,
                proposedAtGameSeconds - 2d,
                "我知道农田里的胡萝卜已经完成收获。",
                9,
                WorldEventKind.GoalCompleted,
                MemorySourceKind.Perception,
                sourceEventId: "harvest-source:" + sourceConversationId,
                rootFactId: rootFactId,
                parentKnowledgeId: null,
                immediateSourceResidentId: default,
                tags: new[] { "harvest", "carrot" },
                isShareable: true,
                entry: out MemoryEntry sourceKnowledge);
            Assert.That(sourceStored.Succeeded, Is.True, sourceStored.Message);
            ResidentRuntimeState runtime = fixture.Runtime(ownerResidentId);
            ActionResult stored = runtime.Memories.AddObservation(
                ownerResidentId,
                proposedAtGameSeconds - 1d,
                "我在实际完成的双人对话中得知胡萝卜已经收获，可以办收获晚餐。",
                9,
                WorldEventKind.ConversationCompleted,
                MemorySourceKind.Conversation,
                sourceEventId: sourceConversationId,
                rootFactId: rootFactId,
                parentKnowledgeId: sourceKnowledge.KnowledgeId,
                immediateSourceResidentId: sourceResidentId,
                tags: new[] { "harvest", "carrot", "conversation" },
                isShareable: true,
                entry: out MemoryEntry knowledge);
            Assert.That(stored.Succeeded, Is.True, stored.Message);
            return CreateProposal(
                ownerResidentId,
                knowledge.KnowledgeId,
                sourceConversationId,
                proposedAtGameSeconds);
        }

        private static TownEventProposal CreateProposal(
            ResidentId proposerResidentId,
            string sourceKnowledgeId,
            string sourceConversationId,
            double proposedAtGameSeconds)
        {
            var decision = new ResidentDecisionSpec(
                proposerResidentId,
                ResidentHighLevelIntents.ProposeTownEvent,
                targetResidentId: null,
                reason: "胡萝卜已经收获，建议举办收获晚餐。",
                provider: "test");
            ActionResult created = TownEventProposal.TryCreateFromDecision(
                decision,
                sourceKnowledgeId,
                sourceConversationId,
                proposedAtGameSeconds,
                out TownEventProposal proposal);
            Assert.That(created.Succeeded, Is.True, created.Message);
            return proposal;
        }

        private sealed class Fixture
        {
            private readonly Dictionary<ResidentId, ResidentRuntimeState> runtimes =
                new Dictionary<ResidentId, ResidentRuntimeState>();
            private readonly Dictionary<ResidentId, ObservationService> projections =
                new Dictionary<ResidentId, ObservationService>();
            private readonly List<string> projectionFailures = new List<string>();

            public Fixture(
                double durationGameSeconds =
                    TownEventCoordinator.DefaultDurationGameSeconds,
                double arrivalTimeoutSeconds =
                    TownEventCoordinator.DefaultArrivalTimeoutSeconds,
                double reservationLeaseSeconds =
                    TownEventCoordinator.DefaultReservationLeaseSeconds)
            {
                Registry = new ResidentRegistry();
                foreach (ResidentDefinition definition in ResidentDefinition.TownResidents)
                {
                    var runtime = new ResidentRuntimeState(definition);
                    ActionResult registered = Registry.Register(definition, runtime);
                    Assert.That(registered.Succeeded, Is.True, registered.Message);
                    runtimes.Add(definition.ResidentId, runtime);
                    projections.Add(
                        definition.ResidentId,
                        new ObservationService(definition.ResidentId));
                }

                Events = new WorldEventLog(
                    capacity: 64,
                    stableEventNamespace: "town-event-system-tests");
                Events.EntryRecorded += ProjectToAllResidents;
                Reservations = new InteractionPointReservationService();
                var attendancePoints = new Dictionary<ResidentId, string>
                {
                    { ResidentIds.Yaya, "plaza-harvest-seat-yaya" },
                    { ResidentIds.Amu, "plaza-harvest-seat-amu" },
                    { ResidentIds.Xiaosui, "plaza-harvest-seat-xiaosui" },
                    { ResidentIds.Momo, "plaza-harvest-seat-momo" }
                };
                Coordinator = new TownEventCoordinator(
                    Registry,
                    Reservations,
                    Events,
                    attendancePoints,
                    durationGameSeconds,
                    arrivalTimeoutSeconds,
                    reservationLeaseSeconds);
            }

            public ResidentRegistry Registry { get; }

            public WorldEventLog Events { get; }

            public InteractionPointReservationService Reservations { get; }

            public TownEventCoordinator Coordinator { get; }

            public ResidentRuntimeState Runtime(ResidentId residentId)
            {
                Assert.That(runtimes.TryGetValue(residentId, out ResidentRuntimeState runtime),
                    Is.True);
                return runtime;
            }

            public void AssertProjectionSucceeded()
            {
                Assert.That(projectionFailures, Is.Empty);
            }

            private void ProjectToAllResidents(WorldEventEntry worldEvent)
            {
                foreach (KeyValuePair<ResidentId, ObservationService> projection in projections)
                {
                    ActionResult observed = projection.Value.Observe(
                        worldEvent,
                        runtimes[projection.Key].Memories,
                        out _);
                    if (observed.Failed)
                    {
                        projectionFailures.Add(
                            projection.Key + ": " + observed.Message);
                    }
                }
            }
        }
    }
}
