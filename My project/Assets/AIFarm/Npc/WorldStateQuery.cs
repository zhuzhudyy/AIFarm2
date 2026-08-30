using System;
using System.Collections.Generic;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Time;

namespace AIFarm.Npc
{
    public sealed class WorldStateQuery
    {
        public WorldStateQuery(FarmField field, FarmInventory inventory, GameClock clock)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public FarmField Field { get; }

        public FarmInventory Inventory { get; }

        public GameClock Clock { get; }

        public bool AreAllTargetsHarvested(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            if (goal == null || harvestedPlotNumbers == null)
            {
                return false;
            }

            foreach (int plotNumber in goal.TargetPlotNumbers)
            {
                if (!Contains(harvestedPlotNumbers, plotNumber))
                {
                    return false;
                }
            }

            return true;
        }

        public FarmPlot FindFirstEmptyTarget(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            return FindFirst(goal, harvestedPlotNumbers, plot => plot.State == PlotState.Empty);
        }

        public FarmPlot FindFirstUnfertilizedTarget(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            return FindFirst(
                goal,
                harvestedPlotNumbers,
                plot => plot.State == PlotState.Growing && !plot.IsFertilized);
        }

        public FarmPlot FindFirstUnderwateredTarget(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            return FindFirst(
                goal,
                harvestedPlotNumbers,
                plot => plot.State == PlotState.Growing &&
                    plot.WaterLevel < FarmPlot.RequiredWaterLevel);
        }

        public FarmPlot FindFirstWeedyTarget(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            return FindFirst(
                goal,
                harvestedPlotNumbers,
                plot => plot.State == PlotState.Growing && plot.HasWeeds);
        }

        public FarmPlot FindFirstMatureTarget(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers)
        {
            return FindFirst(
                goal,
                harvestedPlotNumbers,
                plot => plot.State == PlotState.Mature && plot.Crop == CropType.Carrot);
        }

        private FarmPlot FindFirst(
            FarmGoalSpec goal,
            IReadOnlyCollection<int> harvestedPlotNumbers,
            Func<FarmPlot, bool> predicate)
        {
            if (goal == null || harvestedPlotNumbers == null || predicate == null)
            {
                return null;
            }

            foreach (int plotNumber in goal.TargetPlotNumbers)
            {
                if (Contains(harvestedPlotNumbers, plotNumber))
                {
                    continue;
                }

                FarmPlot plot = Field.GetPlot(plotNumber);
                if (predicate(plot))
                {
                    return plot;
                }
            }

            return null;
        }

        private static bool Contains(IReadOnlyCollection<int> values, int expected)
        {
            foreach (int value in values)
            {
                if (value == expected)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
