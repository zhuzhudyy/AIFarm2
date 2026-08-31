using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Town
{
    public interface IResidentLocationNavigator
    {
        bool IsMoving { get; }

        ActionResult BeginMove(string interactionPointId);

        ActionResult Tick(float deltaTime, out bool arrived);

        ActionResult CancelMove();
    }

    public enum ResidentScheduleState
    {
        WaitingForSchedule = 0,
        Moving,
        Working,
        WaitingToRetry,
        Suspended
    }

    public sealed class ResidentScheduleRuntime
    {
        private readonly TownScheduler scheduler;
        private readonly InteractionPointReservationService reservationService;
        private readonly TownLocationPointDirectory pointDirectory;
        private readonly IResidentLocationNavigator navigator;
        private readonly HashSet<string> failedPointIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly float arrivalTimeoutSeconds;
        private readonly float retryDelaySeconds;
        private readonly double reservationLeaseSeconds;
        private InteractionPointReservation reservation;
        private DailyScheduleEntry activeEntry;
        private float movementElapsedSeconds;
        private float retryElapsedSeconds;

        public ResidentScheduleRuntime(
            ResidentId residentId,
            TownScheduler townScheduler,
            InteractionPointReservationService reservations,
            TownLocationPointDirectory locations,
            IResidentLocationNavigator locationNavigator,
            float arrivalTimeoutSeconds = 8f,
            float retryDelaySeconds = 1f,
            double reservationLeaseSeconds = 12d)
        {
            if (!residentId.IsValid)
            {
                throw new ArgumentException("A valid ResidentId is required.", nameof(residentId));
            }

            if (arrivalTimeoutSeconds <= 0f || float.IsNaN(arrivalTimeoutSeconds) ||
                float.IsInfinity(arrivalTimeoutSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(arrivalTimeoutSeconds));
            }

            if (retryDelaySeconds < 0f || float.IsNaN(retryDelaySeconds) ||
                float.IsInfinity(retryDelaySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(retryDelaySeconds));
            }

            if (reservationLeaseSeconds <= arrivalTimeoutSeconds ||
                double.IsNaN(reservationLeaseSeconds) ||
                double.IsInfinity(reservationLeaseSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reservationLeaseSeconds),
                    "Reservation lease must outlast one arrival attempt.");
            }

            ResidentId = residentId;
            scheduler = townScheduler ?? throw new ArgumentNullException(nameof(townScheduler));
            reservationService = reservations ?? throw new ArgumentNullException(nameof(reservations));
            pointDirectory = locations ?? throw new ArgumentNullException(nameof(locations));
            navigator = locationNavigator ?? throw new ArgumentNullException(nameof(locationNavigator));
            this.arrivalTimeoutSeconds = arrivalTimeoutSeconds;
            this.retryDelaySeconds = retryDelaySeconds;
            this.reservationLeaseSeconds = reservationLeaseSeconds;
        }

        public ResidentId ResidentId { get; }

        public ResidentScheduleState State { get; private set; } =
            ResidentScheduleState.WaitingForSchedule;

        public DailyScheduleEntry ActiveEntry => activeEntry;

        public TownLocationId ScheduledLocationId =>
            activeEntry == null ? default : activeEntry.TargetLocationId;

        public TownLocationId CurrentLocationId { get; private set; }

        public string ActiveInteractionPointId => reservation.IsValid
            ? reservation.InteractionPointId
            : string.Empty;

        public int FailedAttemptCount { get; private set; }

        public string LastFailureReason { get; private set; } = string.Empty;

        public ActionResult Tick(
            int minuteOfDay,
            double elapsedSeconds,
            float deltaTime)
        {
            if (minuteOfDay < 0 || minuteOfDay >= DailyScheduleDefinition.MinutesPerDay ||
                double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0d || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Schedule ticks require valid time values.");
            }

            if (State == ResidentScheduleState.Suspended)
            {
                return ActionResult.Success("Resident schedule is suspended.");
            }

            ActionResult resolved = scheduler.TryResolve(
                ResidentId,
                minuteOfDay,
                out DailyScheduleEntry entry);
            if (resolved.Failed)
            {
                StopCurrentActivity();
                State = ResidentScheduleState.WaitingForSchedule;
                LastFailureReason = resolved.Message;
                return resolved;
            }

            if (!EntriesMatch(activeEntry, entry))
            {
                SwitchTo(entry, elapsedSeconds);
            }

            switch (State)
            {
                case ResidentScheduleState.Moving:
                    return TickMoving(elapsedSeconds, deltaTime);
                case ResidentScheduleState.Working:
                    return KeepReservationAlive(elapsedSeconds);
                case ResidentScheduleState.WaitingToRetry:
                    retryElapsedSeconds += deltaTime;
                    if (retryElapsedSeconds >= retryDelaySeconds)
                    {
                        failedPointIds.Clear();
                        retryElapsedSeconds = 0f;
                        return TryStartMove(elapsedSeconds);
                    }

                    return ActionResult.Success();
                case ResidentScheduleState.Suspended:
                    return ActionResult.Success();
                default:
                    return TryStartMove(elapsedSeconds);
            }
        }

        public ActionResult Suspend()
        {
            StopCurrentActivity();
            activeEntry = null;
            State = ResidentScheduleState.Suspended;
            return ActionResult.Success("Resident schedule suspended.");
        }

        public ActionResult Resume()
        {
            if (State == ResidentScheduleState.Suspended)
            {
                State = ResidentScheduleState.WaitingForSchedule;
            }

            return ActionResult.Success("Resident schedule resumed.");
        }

        private void SwitchTo(DailyScheduleEntry entry, double elapsedSeconds)
        {
            StopCurrentActivity();
            activeEntry = entry;
            CurrentLocationId = default;
            failedPointIds.Clear();
            retryElapsedSeconds = 0f;
            LastFailureReason = string.Empty;
            State = ResidentScheduleState.WaitingForSchedule;
            TryStartMove(elapsedSeconds);
        }

        private ActionResult TryStartMove(double elapsedSeconds)
        {
            if (activeEntry == null)
            {
                State = ResidentScheduleState.WaitingForSchedule;
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "No schedule entry is active.");
            }

            ActionResult found = pointDirectory.TryGetPointIds(
                activeEntry.TargetLocationId,
                out IReadOnlyList<string> pointIds);
            if (found.Failed)
            {
                State = ResidentScheduleState.WaitingToRetry;
                LastFailureReason = found.Message;
                return found;
            }

            foreach (string pointId in pointIds)
            {
                if (failedPointIds.Contains(pointId))
                {
                    continue;
                }

                ActionResult reserved = reservationService.TryReserve(
                    ResidentId,
                    pointId,
                    elapsedSeconds,
                    reservationLeaseSeconds,
                    out InteractionPointReservation nextReservation);
                if (reserved.Failed)
                {
                    continue;
                }

                ActionResult movement = navigator.BeginMove(pointId);
                if (movement.Succeeded)
                {
                    reservation = nextReservation;
                    movementElapsedSeconds = 0f;
                    State = ResidentScheduleState.Moving;
                    LastFailureReason = string.Empty;
                    return movement;
                }

                reservationService.Release(nextReservation);
                failedPointIds.Add(pointId);
                FailedAttemptCount++;
                LastFailureReason = movement.Message;
            }

            State = ResidentScheduleState.WaitingToRetry;
            retryElapsedSeconds = 0f;
            return ActionResult.Success("No free reachable point is available; retry scheduled.");
        }

        private ActionResult TickMoving(double elapsedSeconds, float deltaTime)
        {
            movementElapsedSeconds += deltaTime;
            ActionResult movement = navigator.Tick(deltaTime, out bool arrived);
            if (movement.Succeeded && arrived)
            {
                CurrentLocationId = activeEntry.TargetLocationId;
                State = ResidentScheduleState.Working;
                movementElapsedSeconds = 0f;
                LastFailureReason = string.Empty;
                return KeepReservationAlive(elapsedSeconds);
            }

            if (movement.Failed || movementElapsedSeconds >= arrivalTimeoutSeconds)
            {
                string reason = movement.Failed
                    ? movement.Message
                    : $"Arrival at '{activeEntry.TargetLocationId}' timed out.";
                return RecoverFromFailedArrival(elapsedSeconds, reason);
            }

            return KeepReservationAlive(elapsedSeconds);
        }

        private ActionResult RecoverFromFailedArrival(double elapsedSeconds, string reason)
        {
            string failedPointId = ActiveInteractionPointId;
            navigator.CancelMove();
            ReleaseReservation();
            if (!string.IsNullOrEmpty(failedPointId))
            {
                failedPointIds.Add(failedPointId);
            }

            FailedAttemptCount++;
            LastFailureReason = reason;
            movementElapsedSeconds = 0f;
            ActionResult replanned = TryStartMove(elapsedSeconds);
            if (replanned.Failed)
            {
                LastFailureReason = reason;
            }

            return ActionResult.Success(reason);
        }

        private ActionResult KeepReservationAlive(double elapsedSeconds)
        {
            if (!reservation.IsValid)
            {
                State = ResidentScheduleState.WaitingToRetry;
                LastFailureReason = "The active interaction point reservation was lost.";
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    LastFailureReason);
            }

            ActionResult renewed = reservationService.Renew(
                reservation,
                elapsedSeconds,
                reservationLeaseSeconds);
            if (renewed.Failed)
            {
                navigator.CancelMove();
                reservation = default;
                State = ResidentScheduleState.WaitingToRetry;
                retryElapsedSeconds = 0f;
                LastFailureReason = renewed.Message;
            }

            return renewed;
        }

        private void StopCurrentActivity()
        {
            if (navigator.IsMoving)
            {
                navigator.CancelMove();
            }

            ReleaseReservation();
            movementElapsedSeconds = 0f;
            retryElapsedSeconds = 0f;
            CurrentLocationId = default;
        }

        private void ReleaseReservation()
        {
            if (reservation.IsValid)
            {
                reservationService.Release(reservation);
                reservation = default;
            }
        }

        private static bool EntriesMatch(DailyScheduleEntry left, DailyScheduleEntry right)
        {
            return left != null && right != null &&
                left.StartMinute == right.StartMinute &&
                left.EndMinute == right.EndMinute &&
                left.TargetLocationId == right.TargetLocationId &&
                left.Activity == right.Activity;
        }
    }
}
