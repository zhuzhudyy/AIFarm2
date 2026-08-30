using AIFarm.Core;
using AIFarm.Time;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class GameClockTests
    {
        [Test]
        public void Clock_CanAdvancePauseResumeAndApplyTimeScale()
        {
            var clock = new GameClock();

            AssertSuccess(clock.Advance(5d));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(5d));

            AssertSuccess(clock.SetTimeScale(3d));
            AssertSuccess(clock.Advance(2d));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(11d));

            AssertSuccess(clock.Pause());
            AssertSuccess(clock.Advance(10d));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(11d));

            AssertSuccess(clock.Resume());
            AssertSuccess(clock.Advance(1d));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(14d));
        }

        [Test]
        public void PauseOrResume_WhenAlreadyInRequestedState_FailsWithoutChangingState()
        {
            var clock = new GameClock();
            AssertSuccess(clock.Pause());

            ActionResult secondPause = clock.Pause();

            Assert.That(secondPause.Failed, Is.True);
            Assert.That(secondPause.FailureReason, Is.EqualTo(ActionFailureReason.InvalidState));
            Assert.That(clock.IsPaused, Is.True);

            AssertSuccess(clock.Resume());
            ActionResult secondResume = clock.Resume();

            Assert.That(secondResume.Failed, Is.True);
            Assert.That(secondResume.FailureReason, Is.EqualTo(ActionFailureReason.InvalidState));
            Assert.That(clock.IsPaused, Is.False);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void SetTimeScale_WithInvalidValue_FailsWithoutChangingScale(double value)
        {
            var clock = new GameClock(initialTimeScale: 2d);

            ActionResult result = clock.SetTimeScale(value);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(clock.TimeScale, Is.EqualTo(2d));
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Advance_WithInvalidDuration_FailsWithoutChangingTime(double value)
        {
            var clock = new GameClock(initialElapsedGameSeconds: 10d);

            ActionResult result = clock.Advance(value);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(clock.ElapsedGameSeconds, Is.EqualTo(10d));
        }

        private static void AssertSuccess(ActionResult result)
        {
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.None));
        }
    }
}
