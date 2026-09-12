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
        private readonly int[] lifecycleVersions = new int[FarmField.PlotCount];
        private readonly int[] wateringVersions = new int[FarmField.PlotCount];
        private double lastObservedGameSeconds;

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
            lastObservedGameSeconds = clock.ElapsedGameSeconds;
        }

        public int WaterDecayEventCount { get; private set; }

        public int WeedEventCount { get; private set; }

        public double GetRemainingGrowthGameSeconds(int plotNumber)
        {
            FarmPlot plot = field.GetPlot(plotNumber);
            return plot.State == PlotState.Empty || plot.State == PlotState.Mature ? 0d :
                Math.Max(0d, demoMode.MaturityGameSeconds - Math.Max(growthElapsed[plotNumber - 1],
                    plot.GrowthProgress / (double)FarmPlot.RequiredGrowth * demoMode.MaturityGameSeconds));
        }

        public string GetGrowthBlockReason(int plotNumber)
        {
            FarmPlot plot = field.GetPlot(plotNumber);
            if (plot.State != PlotState.Growing) return string.Empty;
            if (plot.WaterLevel < FarmPlot.RequiredWaterLevel) return "缺水，等待浇水";
            if (!plot.IsFertilized) return "等待施肥";
            if (plot.HasWeeds) return "杂草阻碍生长，等待除草";
            if (!plot.HasBeenWeeded) return "等待首次除草";
            return string.Empty;
        }

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
            lastObservedGameSeconds = clock.ElapsedGameSeconds;
            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                lifecycleVersions[index] = field.Plots[index].LifecycleVersion;
                wateringVersions[index] = field.Plots[index].WateringVersion;
            }
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
            lastObservedGameSeconds = clock.ElapsedGameSeconds;
            return ActionResult.Success("Farm simulation runtime reset.");
        }

        public ActionResult Advance(double realSeconds)
        {
            ActionResult timeResult = clock.Advance(realSeconds);
            if (timeResult.Failed)
            {
                return timeResult;
            }

            return AdvanceToClock();
        }

        // Called once by the world owner. Repeated observers are harmless: they
        // cannot grow the same plot twice or multiply the speed again.
        public ActionResult AdvanceToClock()
        {
            double elapsedGameSeconds = clock.ElapsedGameSeconds - lastObservedGameSeconds;
            lastObservedGameSeconds = clock.ElapsedGameSeconds;
            if (elapsedGameSeconds <= 0d)
            {
                return ActionResult.Success("Farm simulation did not advance.");
            }

            for (int index = 0; index < FarmField.PlotCount; index++)
            {
                FarmPlot plot = field.Plots[index];
                if (lifecycleVersions[index] != plot.LifecycleVersion)
                {
                    ResetPlotTimers(index);
                    lifecycleVersions[index] = plot.LifecycleVersion;
                }

                if (wateringVersions[index] != plot.WateringVersion)
                {
                    waterElapsed[index] = 0d;
                    wateringVersions[index] = plot.WateringVersion;
                }
                if (plot.State == PlotState.Empty)
                {
                    ResetPlotTimers(index);
                    continue;
                }

                if (plot.State == PlotState.Mature)
                {
                    continue;
                }

                // Integrate up to condition boundaries. A large clock step cannot
                // apply today's dry state retroactively to yesterday's growth.
                double remaining = elapsedGameSeconds;
                while (remaining > 0.000001d && plot.State == PlotState.Growing)
                {
                    double step = remaining;
                    if (plot.WaterLevel > 0)
                        step = Math.Min(step, Math.Max(0.000001d,
                            demoMode.WaterDecayGameSeconds - waterElapsed[index]));
                    if (plot.IsFertilized && !plot.HasWeeds && !plot.HasBeenWeeded)
                        step = Math.Min(step, Math.Max(0.000001d,
                            demoMode.WeedDelayGameSeconds - weedElapsed[index]));

                    ActionResult growthResult = AdvanceCropGrowth(plot, index, step);
                    if (growthResult.Failed) return growthResult;
                    if (plot.State == PlotState.Mature) break;
                    ActionResult waterResult = AdvanceWater(plot, index, step);
                    if (waterResult.Failed) return waterResult;
                    ActionResult weedResult = AdvanceWeeds(plot, index, step);
                    if (weedResult.Failed) return weedResult;
                    remaining -= step;
                }
            }

            return ActionResult.Success("Farm simulation advanced.");
        }

        private ActionResult AdvanceWater(FarmPlot plot, int index, double elapsedGameSeconds)
        {
            if (plot.WaterLevel <= 0)
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
                waterElapsed[index] = 0d;
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

            // Preserve valid exact timer fractions while bounding legacy timing
            // against the saved visible stage when production defaults changed.
            growthElapsed[index] = Math.Max(plot.GrowthProgress /
                (double)FarmPlot.RequiredGrowth * demoMode.MaturityGameSeconds,
                Math.Min(growthElapsed[index], (plot.GrowthProgress + 0.999d) /
                    FarmPlot.RequiredGrowth * demoMode.MaturityGameSeconds));
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
