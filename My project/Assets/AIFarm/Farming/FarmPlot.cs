using AIFarm.Core;
using AIFarm.Inventory;

namespace AIFarm.Farming
{
    public sealed class FarmPlot
    {
        public const int RequiredWaterLevel = 1;
        public const int MaximumWaterLevel = 2;
        public const int RequiredGrowth = 100;

        public FarmPlot(int plotNumber = 1)
        {
            if (plotNumber < 1 || plotNumber > FarmField.PlotCount)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(plotNumber),
                    $"Plot number must be between 1 and {FarmField.PlotCount}.");
            }

            PlotNumber = plotNumber;
        }

        public int PlotNumber { get; }

        public PlotState State { get; private set; } = PlotState.Empty;

        public CropType? Crop { get; private set; }

        public int WaterLevel { get; private set; }

        public bool IsFertilized { get; private set; }

        public bool HasWeeds { get; private set; }

        public bool HasBeenWeeded { get; private set; }

        public int GrowthProgress { get; private set; }

        public ActionResult RestoreState(
            PlotState state,
            CropType? crop,
            int waterLevel,
            bool isFertilized,
            bool hasWeeds,
            bool hasBeenWeeded,
            int growthProgress)
        {
            if (!System.Enum.IsDefined(typeof(PlotState), state) ||
                (crop.HasValue && !System.Enum.IsDefined(typeof(CropType), crop.Value)) ||
                waterLevel < 0 || waterLevel > MaximumWaterLevel ||
                growthProgress < 0 || growthProgress > RequiredGrowth)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    $"Plot {PlotNumber:00} contains an invalid saved value.");
            }

            bool validEmpty = state == PlotState.Empty &&
                !crop.HasValue &&
                waterLevel == 0 &&
                !isFertilized &&
                !hasWeeds &&
                !hasBeenWeeded &&
                growthProgress == 0;
            bool validGrowing = state == PlotState.Growing &&
                crop == CropType.Carrot &&
                growthProgress < RequiredGrowth &&
                !(hasWeeds && hasBeenWeeded) &&
                (isFertilized || (!hasWeeds && !hasBeenWeeded));
            bool validMature = state == PlotState.Mature &&
                crop == CropType.Carrot &&
                waterLevel >= RequiredWaterLevel &&
                isFertilized &&
                !hasWeeds &&
                hasBeenWeeded &&
                growthProgress == RequiredGrowth;

            if (!validEmpty && !validGrowing && !validMature)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    $"Plot {PlotNumber:00} saved state is internally inconsistent.");
            }

            State = state;
            Crop = crop;
            WaterLevel = waterLevel;
            IsFertilized = isFertilized;
            HasWeeds = hasWeeds;
            HasBeenWeeded = hasBeenWeeded;
            GrowthProgress = growthProgress;
            return ActionResult.Success($"Plot {PlotNumber:00} restored.");
        }

        public ActionResult Sow(FarmInventory inventory)
        {
            if (inventory == null)
            {
                return InvalidInventory();
            }

            if (State != PlotState.Empty)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only an empty plot can be sown.");
            }

            ActionResult consumption = inventory.TryRemove(InventoryItem.CarrotSeed);
            if (consumption.Failed)
            {
                return consumption;
            }

            State = PlotState.Growing;
            Crop = CropType.Carrot;
            WaterLevel = 0;
            IsFertilized = false;
            HasWeeds = false;
            HasBeenWeeded = false;
            GrowthProgress = 0;
            return ActionResult.Success("Carrot seed sown.");
        }

        public ActionResult Water(FarmInventory inventory)
        {
            if (inventory == null)
            {
                return InvalidInventory();
            }

            if (State != PlotState.Growing)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a growing crop can be watered.");
            }

            if (WaterLevel >= MaximumWaterLevel)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "This plot is already fully watered.");
            }

            ActionResult consumption = inventory.TryRemove(InventoryItem.Water);
            if (consumption.Failed)
            {
                return consumption;
            }

            WaterLevel = MaximumWaterLevel;
            return ActionResult.Success("Plot watered.");
        }

        public ActionResult DecreaseWater(int amount = 1)
        {
            if (amount <= 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Water decrease must be positive.");
            }

            if (State != PlotState.Growing)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a growing crop can lose water.");
            }

            if (WaterLevel < amount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "The plot does not contain enough water for that decrease.");
            }

            WaterLevel -= amount;
            return ActionResult.Success("Plot water decreased.");
        }

        public ActionResult Fertilize(FarmInventory inventory)
        {
            if (inventory == null)
            {
                return InvalidInventory();
            }

            if (State != PlotState.Growing)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a growing crop can be fertilized.");
            }

            if (WaterLevel < RequiredWaterLevel)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "A crop must be watered before it can be fertilized.");
            }

            if (IsFertilized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "This plot has already been fertilized.");
            }

            ActionResult consumption = inventory.TryRemove(InventoryItem.Fertilizer);
            if (consumption.Failed)
            {
                return consumption;
            }

            IsFertilized = true;
            return ActionResult.Success("Plot fertilized.");
        }

        public ActionResult IntroduceWeeds()
        {
            if (State != PlotState.Growing || !IsFertilized)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Weeds can only appear on a fertilized, growing plot.");
            }

            if (HasWeeds || HasBeenWeeded)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "This plot cannot enter the weeding stage again.");
            }

            HasWeeds = true;
            return ActionResult.Success("Weeds appeared.");
        }

        public ActionResult Weed()
        {
            if (State != PlotState.Growing || !HasWeeds)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "This plot has no weeds to remove.");
            }

            HasWeeds = false;
            HasBeenWeeded = true;
            return ActionResult.Success("Weeds removed.");
        }

        public ActionResult AdvanceGrowth(int amount)
        {
            if (amount <= 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Growth amount must be positive.");
            }

            if (State != PlotState.Growing)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a growing crop can gain growth progress.");
            }

            if (WaterLevel < RequiredWaterLevel || !IsFertilized || HasWeeds || !HasBeenWeeded)
            {
                return ActionResult.Failure(
                    ActionFailureReason.MissingGrowthCondition,
                    "Watering, fertilizing, and weeding must be complete before growth can advance.");
            }

            long nextProgress = (long)GrowthProgress + amount;
            if (nextProgress >= RequiredGrowth)
            {
                GrowthProgress = RequiredGrowth;
                State = PlotState.Mature;
            }
            else
            {
                GrowthProgress = (int)nextProgress;
            }

            return ActionResult.Success("Growth advanced.");
        }

        public ActionResult Harvest(FarmInventory inventory)
        {
            if (inventory == null)
            {
                return InvalidInventory();
            }

            if (State != PlotState.Mature || Crop != CropType.Carrot)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidState,
                    "Only a mature carrot crop can be harvested.");
            }

            ActionResult addition = inventory.TryAdd(InventoryItem.Carrot);
            if (addition.Failed)
            {
                return addition;
            }

            State = PlotState.Empty;
            Crop = null;
            WaterLevel = 0;
            IsFertilized = false;
            HasWeeds = false;
            HasBeenWeeded = false;
            GrowthProgress = 0;
            return ActionResult.Success("Carrot harvested and plot cleared.");
        }

        private static ActionResult InvalidInventory()
        {
            return ActionResult.Failure(
                ActionFailureReason.InvalidArgument,
                "An inventory is required for this action.");
        }
    }
}
