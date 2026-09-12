using System;

namespace AIFarm.Core
{
    public sealed class DemoMode
    {
        public DemoMode(
            double recommendedTimeScale = 1d,
            double waterDecayGameSeconds = 28800d,
            double weedDelayGameSeconds = 14400d,
            double maturityGameSeconds = 172800d,
            float sowActionSeconds = 0.2f,
            float fertilizeActionSeconds = 0.22f,
            float waterActionSeconds = 0.2f,
            float weedActionSeconds = 0.2f,
            float harvestActionSeconds = 0.25f,
            float waitActionSeconds = 0.2f,
            float expressionCooldownSeconds = 12f,
            float expressionDisplaySeconds = 2.5f)
        {
            RecommendedTimeScale = recommendedTimeScale;
            WaterDecayGameSeconds = waterDecayGameSeconds;
            WeedDelayGameSeconds = weedDelayGameSeconds;
            MaturityGameSeconds = maturityGameSeconds;
            SowActionSeconds = sowActionSeconds;
            FertilizeActionSeconds = fertilizeActionSeconds;
            WaterActionSeconds = waterActionSeconds;
            WeedActionSeconds = weedActionSeconds;
            HarvestActionSeconds = harvestActionSeconds;
            WaitActionSeconds = waitActionSeconds;
            ExpressionCooldownSeconds = expressionCooldownSeconds;
            ExpressionDisplaySeconds = expressionDisplaySeconds;

            ActionResult validation = Validate();
            if (validation.Failed)
            {
                throw new ArgumentOutOfRangeException(nameof(recommendedTimeScale), validation.Message);
            }
        }

        public double RecommendedTimeScale { get; }

        public double WaterDecayGameSeconds { get; }

        public double WeedDelayGameSeconds { get; }

        public double MaturityGameSeconds { get; }

        public float SowActionSeconds { get; }

        public float FertilizeActionSeconds { get; }

        public float WaterActionSeconds { get; }

        public float WeedActionSeconds { get; }

        public float HarvestActionSeconds { get; }

        public float WaitActionSeconds { get; }

        public float ExpressionCooldownSeconds { get; }

        public float ExpressionDisplaySeconds { get; }

        public bool IsAiServiceRequired => false;

        public ActionResult Validate()
        {
            if (!IsFinitePositive(RecommendedTimeScale) ||
                !IsFinitePositive(WaterDecayGameSeconds) ||
                !IsFinitePositive(WeedDelayGameSeconds) ||
                !IsFinitePositive(MaturityGameSeconds) ||
                !IsFinitePositive(SowActionSeconds) ||
                !IsFinitePositive(FertilizeActionSeconds) ||
                !IsFinitePositive(WaterActionSeconds) ||
                !IsFinitePositive(WeedActionSeconds) ||
                !IsFinitePositive(HarvestActionSeconds) ||
                !IsFinitePositive(WaitActionSeconds) ||
                !IsFinitePositive(ExpressionCooldownSeconds) ||
                !IsFinitePositive(ExpressionDisplaySeconds))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "DemoMode timing values must be finite and positive.");
            }

            if (WaterDecayGameSeconds >= MaturityGameSeconds)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "DemoMode must decrease water before the crop matures.");
            }

            return ActionResult.Success("Offline DemoMode configuration is valid.");
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }
    }
}
