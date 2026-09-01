using System;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Town;
using UnityEngine;

namespace AIFarm.Presentation
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class TownScheduleCoordinator : MonoBehaviour
    {
        [SerializeField]
        private GameBootstrap bootstrap;

        [SerializeField]
        private DailyScheduleDefinitionAsset[] scheduleAssets =
            Array.Empty<DailyScheduleDefinitionAsset>();

        [SerializeField]
        private LocationArrivalPoint[] arrivalPoints =
            Array.Empty<LocationArrivalPoint>();

        [SerializeField]
        private TownResidentScheduleController[] residents =
            Array.Empty<TownResidentScheduleController>();

        private GameBootstrap subscribedBootstrap;

        public TownScheduler Scheduler { get; private set; }

        public InteractionPointReservationService ReservationService { get; private set; }

        public TownLocationPointDirectory PointDirectory { get; private set; }

        public bool IsInitialized { get; private set; }

        public ActionResult Configure(
            GameBootstrap gameBootstrap,
            DailyScheduleDefinitionAsset[] schedules,
            LocationArrivalPoint[] points,
            TownResidentScheduleController[] residentControllers)
        {
            if (gameBootstrap == null || schedules == null || schedules.Length == 0 ||
                points == null || points.Length == 0 || residentControllers == null ||
                residentControllers.Length == 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Town scheduling requires bootstrap, schedules, arrival points, and residents.");
            }

            bootstrap = gameBootstrap;
            scheduleAssets = (DailyScheduleDefinitionAsset[])schedules.Clone();
            arrivalPoints = (LocationArrivalPoint[])points.Clone();
            residents = (TownResidentScheduleController[])residentControllers.Clone();
            Array.Sort(
                residents,
                (left, right) => left.ResidentId.CompareTo(right.ResidentId));
            if (IsInitialized)
            {
                SubscribeToAuthoritativeStateReset();
            }

            return ActionResult.Success("Town schedule scene references configured.");
        }

        public ActionResult Initialize()
        {
            if (IsInitialized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town schedule coordinator is already initialized.");
            }

            if (bootstrap == null || !bootstrap.IsInitialized || scheduleAssets == null ||
                arrivalPoints == null || residents == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town schedule coordinator requires initialized scene references.");
            }

            Scheduler = new TownScheduler();
            ReservationService = new InteractionPointReservationService();
            PointDirectory = new TownLocationPointDirectory();
            foreach (DailyScheduleDefinitionAsset scheduleAsset in scheduleAssets)
            {
                if (scheduleAsset == null)
                {
                    return InvalidConfiguration("A schedule asset is missing.");
                }

                ActionResult created = scheduleAsset.TryCreateDefinition(
                    out DailyScheduleDefinition schedule);
                if (created.Failed)
                {
                    return created;
                }

                ActionResult registered = Scheduler.RegisterSchedule(schedule);
                if (registered.Failed)
                {
                    return registered;
                }
            }

            foreach (LocationArrivalPoint point in arrivalPoints)
            {
                if (point == null || !point.LocationId.IsValid ||
                    string.IsNullOrWhiteSpace(point.InteractionPointId))
                {
                    return InvalidConfiguration("An arrival point is incomplete.");
                }

                ActionResult registered = PointDirectory.Register(point.CreateDefinition());
                if (registered.Failed)
                {
                    return registered;
                }
            }

            foreach (TownResidentScheduleController resident in residents)
            {
                if (resident == null)
                {
                    return InvalidConfiguration("A resident schedule controller is missing.");
                }

                ActionResult initialized = resident.Initialize(
                    Scheduler,
                    ReservationService,
                    PointDirectory);
                if (initialized.Failed)
                {
                    return initialized;
                }
            }

            IsInitialized = true;
            SubscribeToAuthoritativeStateReset();
            return ActionResult.Success("Deterministic town scheduling initialized.");
        }

        public ActionResult TryGetResidentController(
            ResidentId residentId,
            out TownResidentScheduleController residentController)
        {
            residentController = null;
            if (!residentId.IsValid)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "A valid ResidentId is required to find a schedule controller.");
            }

            foreach (TownResidentScheduleController resident in residents)
            {
                if (resident != null && resident.ResidentId == residentId)
                {
                    residentController = resident;
                    return ActionResult.Success(
                        $"Schedule controller found for resident '{residentId}'.");
                }
            }

            return ActionResult.Failure(
                ActionFailureReason.InvalidState,
                $"No schedule controller is registered for resident '{residentId}'.");
        }

        public ActionResult ResetForAuthoritativeStateChange()
        {
            if (!IsInitialized || ReservationService == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town scheduling must be initialized before it can be reset.");
            }

            ActionResult firstFailure = ActionResult.Success();
            foreach (TownResidentScheduleController resident in residents)
            {
                if (resident == null)
                {
                    continue;
                }

                ActionResult reset = resident.ResetForAuthoritativeStateChange();
                if (reset.Failed && firstFailure.Succeeded)
                {
                    firstFailure = reset;
                }
            }

            // Participant cleanup normally releases its own tokens, but a damaged
            // save or missing participant may leave an unowned anchor lease behind.
            // The authoritative reset boundary invalidates those leases as well.
            ReservationService.InvalidateAll();
            return firstFailure.Failed
                ? firstFailure
                : ActionResult.Success(
                    "Town schedules and all transient interaction reservations were reset.");
        }

        public ActionResult TickSchedules(float deltaTime, double elapsedRealSeconds)
        {
            if (!IsInitialized || bootstrap == null || bootstrap.Clock == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Town scheduling must be initialized before ticking.");
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f ||
                double.IsNaN(elapsedRealSeconds) || double.IsInfinity(elapsedRealSeconds) ||
                elapsedRealSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Town schedule tick time is invalid.");
            }

            ReservationService.ExpireReservations(elapsedRealSeconds);
            int minuteOfDay = TownScheduler.GetMinuteOfDay(bootstrap.Clock.ElapsedGameSeconds);
            ActionResult firstFailure = ActionResult.Success();
            foreach (TownResidentScheduleController resident in residents)
            {
                if (resident == null || !resident.isActiveAndEnabled)
                {
                    continue;
                }

                ActionResult result = resident.TickSchedule(
                    minuteOfDay,
                    elapsedRealSeconds,
                    deltaTime);
                if (result.Failed && firstFailure.Succeeded)
                {
                    firstFailure = result;
                }
            }

            return firstFailure;
        }

        private void Awake()
        {
            ActionResult result = Initialize();
            if (result.Failed)
            {
                Debug.LogError($"Town scheduling did not start: {result.Message}", this);
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            ActionResult result = TickSchedules(
                UnityEngine.Time.deltaTime,
                UnityEngine.Time.realtimeSinceStartupAsDouble);
            if (result.Failed)
            {
                Debug.LogWarning($"Town schedule recovered from: {result.Message}", this);
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromAuthoritativeStateReset();
        }

        private void SubscribeToAuthoritativeStateReset()
        {
            if (subscribedBootstrap == bootstrap)
            {
                return;
            }

            UnsubscribeFromAuthoritativeStateReset();
            if (bootstrap != null)
            {
                bootstrap.AuthoritativeStateResetting += HandleAuthoritativeStateResetting;
                subscribedBootstrap = bootstrap;
            }
        }

        private void UnsubscribeFromAuthoritativeStateReset()
        {
            if (subscribedBootstrap != null)
            {
                subscribedBootstrap.AuthoritativeStateResetting -=
                    HandleAuthoritativeStateResetting;
                subscribedBootstrap = null;
            }
        }

        private void HandleAuthoritativeStateResetting()
        {
            ActionResult reset = ResetForAuthoritativeStateChange();
            if (reset.Failed)
            {
                Debug.LogWarning(
                    $"Town schedule reset completed with a recoverable error: {reset.Message}",
                    this);
            }
        }

        private static ActionResult InvalidConfiguration(string message)
        {
            return ActionResult.Failure(ActionFailureReason.InvalidResponse, message);
        }
    }
}
