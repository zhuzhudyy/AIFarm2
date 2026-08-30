using System;
using AIFarm.Core;
using AIFarm.Time;

namespace AIFarm.Farming
{
    public sealed class FarmSimulation
    {
        private readonly FarmField field;
        private readonly GameClock clock;
        private readonly DemoMode demoMode;
        private readonly double[] waterElapsed = new double[FarmField.PlotCount];
        private readonly double[] weedElapsed = new double[FarmField.PlotCount];
        private readonly double[] growthElapsed = new double[FarmField.PlotCount];
        private readonly bool[] waterHasDecayed = new bool[FarmField.PlotCount];

        public FarmSimulation(FarmField field, GameClock clock, DemoMode demoMode)
        {
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.demoMode = demoMode ?? throw new ArgumentNullException(nameof(demoMode));
        }

        public int WaterDecayEventCount { get; private set; }

        public int WeedEventCount { get; private set; }

        public ActionResult Advance(double realSeconds)
        {
            double previousGameSeconds = clock.ElapsedGameSeconds;
            ActionResult timeResult = clock.Advance(realSeconds);
            if (timeResult.Failed)
            {
                return timeResult;
            }

            double elapsedGameSeconds = clock.ElapsedGameSeconds - previousGameSeconds;
            if (elapsedGameSeconds <= 0d)
            {
                return ActionResult.Success("Farm simulation did not advance.");
            }

            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                FarmPlot plot = field.Plots[index];
                if (plot.State == PlotState.Empty)
                {
                    ResetPlotTimers(index);
                    continue;
                }

                if (plot.State == PlotState.Mature)
                {
                    continue;
                }

                ActionResult waterResult = AdvanceWater(plot, index, elapsedGameSeconds);
                if (waterResult.Failed)
                {
                    return waterResult;
                }

                ActionResult weedResult = AdvanceWeeds(plot, index, elapsedGameSeconds);
                if (weedResult.Failed)
                {
                    return weedResult;
                }

                ActionResult growthResult = AdvanceCropGrowth(plot, index, elapsedGameSeconds);
                if (growthResult.Failed)
                {
                    return growthResult;
                }
            }

            return ActionResult.Success("Farm simulation advanced.");
        }

        private ActionResult AdvanceWater(FarmPlot plot, int index, double elapsedGameSeconds)
        {
            if (waterHasDecayed[index] || plot.WaterLevel < FarmPlot.MaximumWaterLevel)
            {
                return ActionResult.Success();
            }

            waterElapsed[index] += elapsedGameSeconds;
            if (waterElapsed[index] < demoMode.WaterDecayGameSeconds)
            {
                return ActionResult.Success();
            }

            ActionResult result = plot.DecreaseWater();
            if (result.Succeeded)
            {
                waterHasDecayed[index] = true;
                WaterDecayEventCount++;
            }

            return result;
        }

        private ActionResult AdvanceWeeds(FarmPlot plot, int index, double elapsedGameSeconds)
        {
            if (!plot.IsFertilized || plot.HasWeeds || plot.HasBeenWeeded)
            {
                return ActionResult.Success();
            }

            weedElapsed[index] += elapsedGameSeconds;
            if (weedElapsed[index] < demoMode.WeedDelayGameSeconds)
            {
                return ActionResult.Success();
            }

            ActionResult result = plot.IntroduceWeeds();
            if (result.Succeeded)
            {
                WeedEventCount++;
            }

            return result;
        }

        private ActionResult AdvanceCropGrowth(FarmPlot plot, int index, double elapsedGameSeconds)
        {
            if (!plot.HasBeenWeeded || plot.HasWeeds || !plot.IsFertilized ||
                plot.WaterLevel < FarmPlot.RequiredWaterLevel)
            {
                return ActionResult.Success();
            }

            growthElapsed[index] += elapsedGameSeconds;
            int targetProgress = (int)Math.Floor(
                growthElapsed[index] / demoMode.MaturityGameSeconds * FarmPlot.RequiredGrowth);
            targetProgress = Math.Min(FarmPlot.RequiredGrowth, targetProgress);
            int amount = targetProgress - plot.GrowthProgress;
            return amount > 0
                ? plot.AdvanceGrowth(amount)
                : ActionResult.Success();
        }

        private void ResetPlotTimers(int index)
        {
            waterElapsed[index] = 0d;
            weedElapsed[index] = 0d;
            growthElapsed[index] = 0d;
            waterHasDecayed[index] = false;
        }
    }
}
