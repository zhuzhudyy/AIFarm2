using System.Collections.Generic;
using System.Collections.ObjectModel;
using AIFarm.Farming;

namespace AIFarm.Npc
{
    public sealed class FarmGoalSpec
    {
        private static readonly ReadOnlyCollection<int> FullFieldPlots =
            new ReadOnlyCollection<int>(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        private FarmGoalSpec()
        {
        }

        public CropType Crop => CropType.Carrot;

        public IReadOnlyList<int> TargetPlotNumbers => FullFieldPlots;

        public bool RequiresSowing => true;

        public bool RequiresWatering => true;

        public bool RequiresFertilizing => true;

        public bool RequiresWeeding => true;

        public bool RequiresHarvesting => true;

        public string Summary => "完成 3×3 农田的胡萝卜全周期";

        public static FarmGoalSpec CreateFullFieldCarrotLifecycle()
        {
            return new FarmGoalSpec();
        }
    }
}
