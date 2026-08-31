using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;

namespace AIFarm.Town
{
    public readonly struct InteractionPointReservation
    {
        internal InteractionPointReservation(
            string interactionPointId,
            ResidentId residentId,
            long revision)
        {
            InteractionPointId = interactionPointId;
            ResidentId = residentId;
            Revision = revision;
        }

        public string InteractionPointId { get; }

        public ResidentId ResidentId { get; }

        public long Revision { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(InteractionPointId) &&
            ResidentId.IsValid && Revision > 0;
    }

    public sealed class InteractionPointReservationService
    {
        private sealed class ReservationRecord
        {
            public ReservationRecord(
                InteractionPointReservation reservation,
                double expiresAtSeconds)
            {
                Reservation = reservation;
                ExpiresAtSeconds = expiresAtSeconds;
            }

            public InteractionPointReservation Reservation { get; }

            public double ExpiresAtSeconds { get; set; }
        }

        private readonly Dictionary<string, ReservationRecord> reservationsByPoint =
            new Dictionary<string, ReservationRecord>(StringComparer.Ordinal);
        private readonly Dictionary<ResidentId, string> pointByResident =
            new Dictionary<ResidentId, string>();
        private long nextRevision;

        public int ReservationCount => reservationsByPoint.Count;

        public ActionResult TryReserve(
            ResidentId residentId,
            string interactionPointId,
            double nowSeconds,
            double leaseSeconds,
            out InteractionPointReservation reservation)
        {
            reservation = default;
            if (!residentId.IsValid || string.IsNullOrWhiteSpace(interactionPointId) ||
                !IsFiniteNonNegative(nowSeconds) || !IsFinitePositive(leaseSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A reservation requires a resident, point ID, current time, and positive lease.");
            }

            string pointId = interactionPointId.Trim();
            ExpireReservations(nowSeconds);
            if (reservationsByPoint.TryGetValue(pointId, out ReservationRecord occupied))
            {
                if (occupied.Reservation.ResidentId != residentId)
                {
                    return ActionResult.Failure(
                        ActionFailureReason.InvalidState,
                        $"Interaction point '{pointId}' is already reserved.");
                }

                occupied.ExpiresAtSeconds = nowSeconds + leaseSeconds;
                reservation = occupied.Reservation;
                return ActionResult.Success($"Reservation for '{pointId}' renewed.");
            }

            if (pointByResident.TryGetValue(residentId, out string existingPoint))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Resident '{residentId}' already reserves '{existingPoint}'.");
            }

            reservation = new InteractionPointReservation(pointId, residentId, ++nextRevision);
            reservationsByPoint.Add(
                pointId,
                new ReservationRecord(reservation, nowSeconds + leaseSeconds));
            pointByResident.Add(residentId, pointId);
            return ActionResult.Success($"Interaction point '{pointId}' reserved for {residentId}.");
        }

        public ActionResult Renew(
            InteractionPointReservation reservation,
            double nowSeconds,
            double leaseSeconds)
        {
            if (!reservation.IsValid || !IsFiniteNonNegative(nowSeconds) ||
                !IsFinitePositive(leaseSeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid reservation and positive lease are required.");
            }

            ExpireReservations(nowSeconds);
            if (!reservationsByPoint.TryGetValue(
                    reservation.InteractionPointId,
                    out ReservationRecord record) ||
                !Matches(record.Reservation, reservation))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The reservation is stale or no longer active.");
            }

            record.ExpiresAtSeconds = nowSeconds + leaseSeconds;
            return ActionResult.Success("Reservation renewed.");
        }

        public ActionResult Release(InteractionPointReservation reservation)
        {
            if (!reservation.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid reservation is required.");
            }

            if (!reservationsByPoint.TryGetValue(
                    reservation.InteractionPointId,
                    out ReservationRecord record) ||
                !Matches(record.Reservation, reservation))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The reservation is stale or no longer active.");
            }

            Remove(record.Reservation);
            return ActionResult.Success("Reservation released.");
        }

        public ActionResult ReleaseByResident(ResidentId residentId)
        {
            if (!residentId.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid ResidentId is required.");
            }

            if (pointByResident.TryGetValue(residentId, out string pointId) &&
                reservationsByPoint.TryGetValue(pointId, out ReservationRecord record))
            {
                Remove(record.Reservation);
            }

            return ActionResult.Success();
        }

        public int ExpireReservations(double nowSeconds)
        {
            if (!IsFiniteNonNegative(nowSeconds))
            {
                return 0;
            }

            var expired = new List<InteractionPointReservation>();
            foreach (ReservationRecord record in reservationsByPoint.Values)
            {
                if (record.ExpiresAtSeconds <= nowSeconds)
                {
                    expired.Add(record.Reservation);
                }
            }

            foreach (InteractionPointReservation reservation in expired)
            {
                Remove(reservation);
            }

            return expired.Count;
        }

        public bool TryGetOwner(
            string interactionPointId,
            double nowSeconds,
            out ResidentId residentId)
        {
            residentId = default;
            if (string.IsNullOrWhiteSpace(interactionPointId) || !IsFiniteNonNegative(nowSeconds))
            {
                return false;
            }

            ExpireReservations(nowSeconds);
            if (!reservationsByPoint.TryGetValue(
                    interactionPointId.Trim(),
                    out ReservationRecord record))
            {
                return false;
            }

            residentId = record.Reservation.ResidentId;
            return true;
        }

        public bool IsReserved(string interactionPointId, double nowSeconds)
        {
            return TryGetOwner(interactionPointId, nowSeconds, out _);
        }

        private static bool Matches(
            InteractionPointReservation left,
            InteractionPointReservation right)
        {
            return left.Revision == right.Revision &&
                left.ResidentId == right.ResidentId &&
                string.Equals(
                    left.InteractionPointId,
                    right.InteractionPointId,
                    StringComparison.Ordinal);
        }

        private void Remove(InteractionPointReservation reservation)
        {
            reservationsByPoint.Remove(reservation.InteractionPointId);
            pointByResident.Remove(reservation.ResidentId);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
