using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIFarm.Farming
{
    public sealed class FarmField
    {
        public const int RowCount = 3;
        public const int ColumnCount = 3;
        public const int PlotCount = RowCount * ColumnCount;

        private readonly FarmPlot[] plots;
        private readonly ReadOnlyCollection<FarmPlot> readOnlyPlots;

        public FarmField()
        {
            plots = new FarmPlot[PlotCount];
            for (int index = 0; index < plots.Length; index++)
            {
                plots[index] = new FarmPlot(index + 1);
            }

            readOnlyPlots = new ReadOnlyCollection<FarmPlot>(plots);
        }

        public IReadOnlyList<FarmPlot> Plots => readOnlyPlots;

        public FarmPlot GetPlot(int plotNumber)
        {
            if (plotNumber < 1 || plotNumber > PlotCount)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(plotNumber),
                    $"Plot number must be between 1 and {PlotCount}.");
            }

            return plots[plotNumber - 1];
        }
    }
}
