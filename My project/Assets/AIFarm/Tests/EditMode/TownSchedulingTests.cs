using System.Collections.Generic;
using System.Linq;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class TownSchedulingTests
    {
        [Test]
        public void FixedTownResidents_HaveFourUniqueStableIds()
        {
            IReadOnlyList<ResidentDefinition> residents = ResidentDefinition.TownResidents;

            Assert.That(residents, Has.Count.EqualTo(4));
            Assert.That(
                residents.Select(resident => resident.ResidentId).Distinct().Count(),
                Is.EqualTo(4));
            Assert.That(
                residents.Select(resident => resident.DisplayName),
                Is.EquivalentTo(new[] { "芽芽", "阿木", "小穗", "墨墨" }));
            Assert.That(ResidentIds.TownResidents, Is.Ordered);
        }

        [Test]
        public void DailySchedule_ParsesAndResolvesTimeSlotsAtBoundaries()
        {
            Assert.That(DailyScheduleDefinition.TryParseTime("08:30", out int morning), Is.True);
            Assert.That(morning, Is.EqualTo(8 * 60 + 30));
            Assert.That(DailyScheduleDefinition.TryParseTime("24:00", out int midnight), Is.True);
            Assert.That(midnight, Is.EqualTo(DailyScheduleDefinition.MinutesPerDay));
            Assert.That(DailyScheduleDefinition.TryParseTime("24:01", out _), Is.False);
            Assert.That(DailyScheduleDefinition.TryParseTime("8:30", out _), Is.False);

            var home = new TownLocationId("location-home-test");
            var work = new TownLocationId("location-work-test");
            var schedule = new DailyScheduleDefinition(
                new ResidentId("resident-schedule-test"),
                new[]
                {
                    new DailyScheduleEntry("00:00", "08:00", home, ResidentActivityKind.Home),
                    new DailyScheduleEntry("08:00", "17:00", work, ResidentActivityKind.Work),
                    new DailyScheduleEntry("17:00", "24:00", home, ResidentActivityKind.Home)
                });

            Assert.That(schedule.TryResolve(7 * 60 + 59, out DailyScheduleEntry before), Is.True);
            Assert.That(before.TargetLocationId, Is.EqualTo(home));
            Assert.That(schedule.TryResolve(8 * 60, out DailyScheduleEntry atStart), Is.True);
            Assert.That(atStart.TargetLocationId, Is.EqualTo(work));
            Assert.That(schedule.TryResolve(16 * 60 + 59, out DailyScheduleEntry beforeEnd), Is.True);
            Assert.That(beforeEnd.TargetLocationId, Is.EqualTo(work));
            Assert.That(schedule.TryResolve(17 * 60, out DailyScheduleEntry atEnd), Is.True);
            Assert.That(atEnd.TargetLocationId, Is.EqualTo(home));

            var overnight = new DailyScheduleEntry(
                "22:00",
                "06:00",
                home,
                ResidentActivityKind.Home);
            Assert.That(overnight.ContainsMinute(23 * 60), Is.True);
            Assert.That(overnight.ContainsMinute(5 * 60 + 59), Is.True);
            Assert.That(overnight.ContainsMinute(12 * 60), Is.False);
        }

        [Test]
        public void ReservationService_DoesNotDoubleBookInteractionPoint()
        {
            var service = new InteractionPointReservationService();
            var firstResident = new ResidentId("resident-reservation-a");
            var secondResident = new ResidentId("resident-reservation-b");

            ActionResult first = service.TryReserve(
                firstResident,
                "workbench-01",
                nowSeconds: 0d,
                leaseSeconds: 10d,
                out InteractionPointReservation firstReservation);
            ActionResult duplicate = service.TryReserve(
                secondResident,
                "workbench-01",
                nowSeconds: 0.1d,
                leaseSeconds: 10d,
                out _);

            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(duplicate.Failed, Is.True);
            Assert.That(
                service.TryGetOwner("workbench-01", 0.1d, out ResidentId owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(firstResident));
            Assert.That(service.Release(firstReservation).Succeeded, Is.True);
            Assert.That(
                service.TryReserve(
                    secondResident,
                    "workbench-01",
                    nowSeconds: 0.2d,
                    leaseSeconds: 10d,
                    out _).Succeeded,
                Is.True);
        }

        [Test]
        public void ReservationService_AuthoritativeResetInvalidatesOrphanAnchorAndOldToken()
        {
            var service = new InteractionPointReservationService();
            var missingParticipant = new ResidentId("resident-missing-participant");
            var replacementResident = new ResidentId("resident-anchor-replacement");

            Assert.That(
                service.TryReserve(
                    missingParticipant,
                    "conversation-anchor-01:a",
                    nowSeconds: 0d,
                    leaseSeconds: 30d,
                    out InteractionPointReservation staleReservation).Succeeded,
                Is.True);

            Assert.That(service.InvalidateAll(), Is.EqualTo(1));
            Assert.That(service.ReservationCount, Is.Zero);
            Assert.That(
                service.TryReserve(
                    replacementResident,
                    "conversation-anchor-01:a",
                    nowSeconds: 1d,
                    leaseSeconds: 30d,
                    out InteractionPointReservation replacementReservation).Succeeded,
                Is.True);
            Assert.That(replacementReservation.Revision, Is.GreaterThan(staleReservation.Revision));
            Assert.That(service.Release(staleReservation).Failed, Is.True);
            Assert.That(
                service.TryGetOwner(
                    "conversation-anchor-01:a",
                    nowSeconds: 1d,
                    out ResidentId owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(replacementResident));
        }

        [Test]
        public void ResidentRuntime_EntersNewTargetWhenScheduleSlotChanges()
        {
            var residentId = new ResidentId("resident-switch-test");
            var home = new TownLocationId("location-home-test");
            var workshop = new TownLocationId("location-workshop-test");
            TownScheduler scheduler = CreateScheduler(
                residentId,
                new DailyScheduleEntry("00:00", "08:00", home, ResidentActivityKind.Home),
                new DailyScheduleEntry("08:00", "24:00", workshop, ResidentActivityKind.Work));
            TownLocationPointDirectory points = CreatePointDirectory(
                new TownInteractionPointDefinition("home-point-01", home),
                new TownInteractionPointDefinition("work-point-01", workshop));
            var navigator = new FakeLocationNavigator();
            var runtime = new ResidentScheduleRuntime(
                residentId,
                scheduler,
                new InteractionPointReservationService(),
                points,
                navigator,
                arrivalTimeoutSeconds: 1f,
                retryDelaySeconds: 0.1f,
                reservationLeaseSeconds: 2d);

            Assert.That(runtime.Tick(7 * 60 + 59, 0d, 0.1f).Succeeded, Is.True);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(runtime.CurrentLocationId, Is.EqualTo(home));
            Assert.That(runtime.ActiveInteractionPointId, Is.EqualTo("home-point-01"));

            Assert.That(runtime.Tick(8 * 60, 0.1d, 0.1f).Succeeded, Is.True);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(runtime.CurrentLocationId, Is.EqualTo(workshop));
            Assert.That(runtime.ActiveInteractionPointId, Is.EqualTo("work-point-01"));
            Assert.That(navigator.BeginMoveHistory, Is.EqualTo(new[]
            {
                "home-point-01",
                "work-point-01"
            }));
        }

        [Test]
        public void ResidentRuntime_TimesOutReleasesPointAndRecoversAtAlternative()
        {
            var residentId = new ResidentId("resident-recovery-test");
            var workshop = new TownLocationId("location-workshop-test");
            TownScheduler scheduler = CreateScheduler(
                residentId,
                new DailyScheduleEntry("00:00", "24:00", workshop, ResidentActivityKind.Work));
            TownLocationPointDirectory points = CreatePointDirectory(
                new TownInteractionPointDefinition("work-point-01", workshop),
                new TownInteractionPointDefinition("work-point-02", workshop));
            var reservations = new InteractionPointReservationService();
            var navigator = new FakeLocationNavigator
            {
                UnreachablePointId = "work-point-01"
            };
            var runtime = new ResidentScheduleRuntime(
                residentId,
                scheduler,
                reservations,
                points,
                navigator,
                arrivalTimeoutSeconds: 0.5f,
                retryDelaySeconds: 0.1f,
                reservationLeaseSeconds: 2d);

            ActionResult timedOut = runtime.Tick(8 * 60, 0d, 0.6f);

            Assert.That(timedOut.Succeeded, Is.True);
            Assert.That(runtime.FailedAttemptCount, Is.EqualTo(1));
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Moving));
            Assert.That(runtime.ActiveInteractionPointId, Is.EqualTo("work-point-02"));
            Assert.That(reservations.IsReserved("work-point-01", 0d), Is.False);
            Assert.That(
                reservations.TryGetOwner("work-point-02", 0d, out ResidentId owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(residentId));

            ActionResult recovered = runtime.Tick(8 * 60, 0.1d, 0.1f);

            Assert.That(recovered.Succeeded, Is.True, recovered.Message);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(runtime.CurrentLocationId, Is.EqualTo(workshop));
            Assert.That(navigator.BeginMoveHistory, Is.EqualTo(new[]
            {
                "work-point-01",
                "work-point-02"
            }));
        }

        [Test]
        public void ResidentRuntime_AuthoritativeResetCancelsMoveAndNextTickReplans()
        {
            var residentId = new ResidentId("resident-load-reset-test");
            var workshop = new TownLocationId("location-reset-workshop");
            TownScheduler scheduler = CreateScheduler(
                residentId,
                new DailyScheduleEntry(
                    "00:00",
                    "24:00",
                    workshop,
                    ResidentActivityKind.Work));
            TownLocationPointDirectory points = CreatePointDirectory(
                new TownInteractionPointDefinition("reset-work-point-01", workshop));
            var reservations = new InteractionPointReservationService();
            var navigator = new FakeLocationNavigator
            {
                UnreachablePointId = "reset-work-point-01"
            };
            var runtime = new ResidentScheduleRuntime(
                residentId,
                scheduler,
                reservations,
                points,
                navigator,
                arrivalTimeoutSeconds: 2f,
                retryDelaySeconds: 0.1f,
                reservationLeaseSeconds: 4d);

            Assert.That(runtime.Tick(9 * 60, 0d, 0.1f).Succeeded, Is.True);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Moving));
            Assert.That(navigator.IsMoving, Is.True);
            Assert.That(reservations.ReservationCount, Is.EqualTo(1));

            ActionResult reset = runtime.ResetForAuthoritativeStateChange();

            Assert.That(reset.Succeeded, Is.True, reset.Message);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.WaitingForSchedule));
            Assert.That(runtime.ActiveEntry, Is.Null);
            Assert.That(runtime.ActiveInteractionPointId, Is.Empty);
            Assert.That(navigator.IsMoving, Is.False);
            Assert.That(reservations.ReservationCount, Is.Zero);

            navigator.UnreachablePointId = null;
            ActionResult replanned = runtime.Tick(9 * 60, 1d, 0.1f);

            Assert.That(replanned.Succeeded, Is.True, replanned.Message);
            Assert.That(runtime.State, Is.EqualTo(ResidentScheduleState.Working));
            Assert.That(runtime.CurrentLocationId, Is.EqualTo(workshop));
            Assert.That(runtime.ActiveInteractionPointId, Is.EqualTo("reset-work-point-01"));
            Assert.That(
                navigator.BeginMoveHistory,
                Is.EqualTo(new[] { "reset-work-point-01", "reset-work-point-01" }));
        }

        private static TownScheduler CreateScheduler(
            ResidentId residentId,
            params DailyScheduleEntry[] entries)
        {
            var scheduler = new TownScheduler();
            ActionResult registered = scheduler.RegisterSchedule(
                new DailyScheduleDefinition(residentId, entries));
            Assert.That(registered.Succeeded, Is.True, registered.Message);
            return scheduler;
        }

        private static TownLocationPointDirectory CreatePointDirectory(
            params TownInteractionPointDefinition[] points)
        {
            var directory = new TownLocationPointDirectory();
            foreach (TownInteractionPointDefinition point in points)
            {
                ActionResult registered = directory.Register(point);
                Assert.That(registered.Succeeded, Is.True, registered.Message);
            }

            return directory;
        }

        private sealed class FakeLocationNavigator : IResidentLocationNavigator
        {
            private string currentPointId;

            public bool IsMoving { get; private set; }

            public string UnreachablePointId { get; set; }

            public List<string> BeginMoveHistory { get; } = new List<string>();

            public ActionResult BeginMove(string interactionPointId)
            {
                if (IsMoving)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        "Fake navigator is already moving.");
                }

                currentPointId = interactionPointId;
                BeginMoveHistory.Add(interactionPointId);
                IsMoving = true;
                return ActionResult.Success();
            }

            public ActionResult Tick(float deltaTime, out bool arrived)
            {
                arrived = currentPointId != UnreachablePointId;
                if (arrived)
                {
                    IsMoving = false;
                }

                return ActionResult.Success();
            }

            public ActionResult CancelMove()
            {
                IsMoving = false;
                currentPointId = null;
                return ActionResult.Success();
            }
        }
    }
}
