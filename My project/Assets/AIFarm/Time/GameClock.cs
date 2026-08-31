using System;
using AIFarm.Core;

namespace AIFarm.Time
{
    public sealed class GameClock
    {
        public GameClock(double initialElapsedGameSeconds = 0d, double initialTimeScale = 1d)
        {
            if (!IsFinite(initialElapsedGameSeconds) || initialElapsedGameSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialElapsedGameSeconds),
                    "Initial elapsed game time must be finite and non-negative.");
            }

            if (!IsFinite(initialTimeScale) || initialTimeScale <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialTimeScale),
                    "Initial time scale must be finite and positive.");
            }

            ElapsedGameSeconds = initialElapsedGameSeconds;
            TimeScale = initialTimeScale;
        }

        public double ElapsedGameSeconds { get; private set; }

        public double TimeScale { get; private set; }

        public bool IsPaused { get; private set; }

        public ActionResult Restore(
            double elapsedGameSeconds,
            double timeScale,
            bool isPaused)
        {
            if (!IsFinite(elapsedGameSeconds) || elapsedGameSeconds < 0d ||
                !IsFinite(timeScale) || timeScale <= 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Saved game time and time scale must be finite and valid.");
            }

            ElapsedGameSeconds = elapsedGameSeconds;
            TimeScale = timeScale;
            IsPaused = isPaused;
            return ActionResult.Success("Game clock restored.");
        }

        public ActionResult Pause()
        {
            if (IsPaused)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The game clock is already paused.");
            }

            IsPaused = true;
            return ActionResult.Success("Game clock paused.");
        }

        public ActionResult Resume()
        {
            if (!IsPaused)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The game clock is not paused.");
            }

            IsPaused = false;
            return ActionResult.Success("Game clock resumed.");
        }

        public ActionResult SetTimeScale(double timeScale)
        {
            if (!IsFinite(timeScale) || timeScale <= 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Time scale must be finite and positive.");
            }

            TimeScale = timeScale;
            return ActionResult.Success("Game clock time scale changed.");
        }

        public ActionResult Advance(double realSeconds)
        {
            if (!IsFinite(realSeconds) || realSeconds < 0d)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Elapsed real time must be finite and non-negative.");
            }

            if (IsPaused || realSeconds == 0d)
            {
                return ActionResult.Success("Game clock did not advance.");
            }

            double gameSeconds = realSeconds * TimeScale;
            double nextTime = ElapsedGameSeconds + gameSeconds;
            if (!IsFinite(gameSeconds) || !IsFinite(nextTime))
            {
                return ActionResult.Failure(
                    ActionFailureReason.CapacityExceeded,
                    "Advancing would exceed the supported game-time range.");
            }

            ElapsedGameSeconds = nextTime;
            return ActionResult.Success("Game clock advanced.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
