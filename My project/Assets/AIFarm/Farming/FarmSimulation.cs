using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Npc;
using AIFarm.Time;

namespace AIFarm.Farming
{
    public sealed class FarmSimulation
    {
        private readonly FarmField field;
        private readonly GameClock clock;
        private readonly DemoMode demoMode;
        private readonly WorldEventLog eventLog;
        private readonly ResidentId[] farmObserverResidentIds;
        private readonly double[] waterElapsed = new double[FarmField.PlotCount];
        private readonly double[] weedElapsed = new double[FarmField.PlotCount];
        private readonly double[] growthElapsed = new double[FarmField.PlotCount];
        private readonly bool[] waterHasDecayed = new bool[FarmField.PlotCount];

        public FarmSimulation(
            FarmField field,
            GameClock clock,
            DemoMode demoMode,
            WorldEventLog eventLog = null,
            IEnumerable<ResidentId> farmObservers = null)
        {
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.demoMode = demoMode ?? throw new ArgumentNullException(nameof(demoMode));
            this.eventLog = eventLog;
            farmObserverResidentIds = NormalizeFarmObservers(farmObservers);
        }

        public int WaterDecayEventCount { get; private set; }

        public int WeedEventCount { get; private set; }

        public void CaptureRuntimeState(
            out double[] savedWaterElapsed,
            out double[] savedWeedElapsed,
            out double[] savedGrowthElapsed,
            out bool[] savedWaterHasDecayed)
        {
            savedWaterElapsed = (double[])waterElapsed.Clone();
            savedWeedElapsed = (double[])weedElapsed.Clone();
            savedGrowthElapsed = (double[])growthElapsed.Clone();
            savedWaterHasDecayed = (bool[])waterHasDecayed.Clone();
        }

        public ActionResult RestoreRuntimeState(
            int waterDecayEventCount,
            int weedEventCount,
            double[] savedWaterElapsed,
            double[] savedWeedElapsed,
            double[] savedGrowthElapsed,
            bool[] savedWaterHasDecayed)
        {
            if (waterDecayEventCount < 0 || weedEventCount < 0 ||
                !IsValidTimerArray(savedWaterElapsed) ||
                !IsValidTimerArray(savedWeedElapsed) ||
                !IsValidTimerArray(savedGrowthElapsed) ||
                savedWaterHasDecayed == null ||
                savedWaterHasDecayed.Length != FarmField.PlotCount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Saved farm simulation state is invalid.");
            }

            WaterDecayEventCount = waterDecayEventCount;
            WeedEventCount = weedEventCount;
            Array.Copy(savedWaterElapsed, waterElapsed, FarmField.PlotCount);
            Array.Copy(savedWeedElapsed, weedElapsed, FarmField.PlotCount);
            Array.Copy(savedGrowthElapsed, growthElapsed, FarmField.PlotCount);
            Array.Copy(savedWaterHasDecayed, waterHasDecayed, FarmField.PlotCount);
            return ActionResult.Success("Farm simulation runtime restored.");
        }

        public ActionResult ResetRuntimeState()
        {
            WaterDecayEventCount = 0;
            WeedEventCount = 0;
            Array.Clear(waterElapsed, 0, waterElapsed.Length);
            Array.Clear(weedElapsed, 0, weedElapsed.Length);
            Array.Clear(growthElapsed, 0, growthElapsed.Length);
            Array.Clear(waterHasDecayed, 0, waterHasDecayed.Length);
            return ActionResult.Success("Farm simulation runtime reset.");
        }

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
                eventLog?.RecordPerceivable(
                    clock.ElapsedGameSeconds,
                    WorldEventKind.MoistureChanged,
                    $"{plot.PlotNumber:00} 号地水分下降到 {plot.WaterLevel}。",
                    farmObserverResidentIds,
                    plotNumber: plot.PlotNumber,
                    tags: new[] { "farm", "moisture", $"plot-{plot.PlotNumber:00}" });
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
                eventLog?.RecordPerceivable(
                    clock.ElapsedGameSeconds,
                    WorldEventKind.WeedsAppeared,
                    $"{plot.PlotNumber:00} 号地出现杂草。",
                    farmObserverResidentIds,
                    plotNumber: plot.PlotNumber,
                    tags: new[] { "farm", "weeds", $"plot-{plot.PlotNumber:00}" });
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
            if (amount <= 0)
            {
                return ActionResult.Success();
            }

            ActionResult result = plot.AdvanceGrowth(amount);
            if (result.Succeeded && plot.State == PlotState.Mature)
            {
                eventLog?.RecordPerceivable(
                    clock.ElapsedGameSeconds,
                    WorldEventKind.CropMatured,
                    $"{plot.PlotNumber:00} 号地的胡萝卜成熟了。",
                    farmObserverResidentIds,
                    plotNumber: plot.PlotNumber,
                    tags: new[] { "farm", "carrot", "mature", $"plot-{plot.PlotNumber:00}" });
            }

            return result;
        }

        private void ResetPlotTimers(int index)
        {
            waterElapsed[index] = 0d;
            weedElapsed[index] = 0d;
            growthElapsed[index] = 0d;
            waterHasDecayed[index] = false;
        }

        private static ResidentId[] NormalizeFarmObservers(
            IEnumerable<ResidentId> farmObservers)
        {
            var normalized = new List<ResidentId>();
            var unique = new HashSet<ResidentId>();
            if (farmObservers != null)
            {
                foreach (ResidentId observer in farmObservers)
                {
                    if (!observer.IsValid)
                    {
                        throw new ArgumentException(
                            "Farm observers require valid ResidentIds.",
                            nameof(farmObservers));
                    }

                    if (unique.Add(observer))
                    {
                        normalized.Add(observer);
                    }
                }
            }

            if (normalized.Count == 0)
            {
                normalized.Add(ResidentIds.Yaya);
            }

            return normalized.ToArray();
        }

        private static bool IsValidTimerArray(double[] values)
        {
            if (values == null || values.Length != FarmField.PlotCount)
            {
                return false;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (double.IsNaN(values[index]) ||
                    double.IsInfinity(values[index]) ||
                    values[index] < 0d)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
