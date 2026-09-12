using System;
using System.Collections.Generic;
using AIFarm.Core;

namespace AIFarm.Inventory
{
    public sealed class FarmInventory
    {
        private readonly Dictionary<InventoryItem, int> counts;

        public FarmInventory(
            int carrotSeeds = 0,
            int water = 0,
            int fertilizer = 0,
            int carrots = 0,
            int fish = 0,
            int fruit = 0,
            int compost = 0)
        {
            ValidateInitialCount(carrotSeeds, nameof(carrotSeeds));
            ValidateInitialCount(water, nameof(water));
            ValidateInitialCount(fertilizer, nameof(fertilizer));
            ValidateInitialCount(carrots, nameof(carrots));
            ValidateInitialCount(fish, nameof(fish));
            ValidateInitialCount(fruit, nameof(fruit));
            ValidateInitialCount(compost, nameof(compost));

            counts = new Dictionary<InventoryItem, int>
            {
                [InventoryItem.CarrotSeed] = carrotSeeds,
                [InventoryItem.Water] = water,
                [InventoryItem.Fertilizer] = fertilizer,
                [InventoryItem.Carrot] = carrots,
                [InventoryItem.Fish] = fish,
                [InventoryItem.Fruit] = fruit,
                [InventoryItem.Compost] = compost
            };
        }

        public int GetCount(InventoryItem item)
        {
            return counts[item];
        }

        public bool Has(InventoryItem item, int amount = 1)
        {
            return amount > 0 && counts.TryGetValue(item, out int count) && count >= amount;
        }

        public ActionResult RestoreCounts(
            int carrotSeeds,
            int water,
            int fertilizer,
            int carrots,
            int fish = 0,
            int fruit = 0,
            int compost = 0)
        {
            if (carrotSeeds < 0 || water < 0 || fertilizer < 0 || carrots < 0 ||
                fish < 0 || fruit < 0 || compost < 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Saved inventory counts cannot be negative.");
            }

            counts[InventoryItem.CarrotSeed] = carrotSeeds;
            counts[InventoryItem.Water] = water;
            counts[InventoryItem.Fertilizer] = fertilizer;
            counts[InventoryItem.Carrot] = carrots;
            counts[InventoryItem.Fish] = fish;
            counts[InventoryItem.Fruit] = fruit;
            counts[InventoryItem.Compost] = compost;
            return ActionResult.Success("Farm inventory restored.");
        }

        // Validate every delta before writing any count: harvest and compost exchange
        // must never partially settle when another item is full or unavailable.
        public ActionResult TryApply(IReadOnlyDictionary<InventoryItem, int> deltas)
        {
            if (deltas == null || deltas.Count == 0)
            {
                return ActionResult.Failure(ActionFailureReason.InvalidArgument, "Inventory transaction is empty.");
            }

            foreach (KeyValuePair<InventoryItem, int> delta in deltas)
            {
                if (!counts.TryGetValue(delta.Key, out int current))
                {
                    return ActionResult.Failure(ActionFailureReason.InvalidArgument, "Unknown inventory item.");
                }

                long next = (long)current + delta.Value;
                if (next < 0 || next > int.MaxValue)
                {
                    return ActionResult.Failure(next < 0 ? ActionFailureReason.InsufficientResource :
                        ActionFailureReason.CapacityExceeded, $"Cannot settle {delta.Key}: available {current}.");
                }
            }

            foreach (KeyValuePair<InventoryItem, int> delta in deltas)
            {
                counts[delta.Key] += delta.Value;
            }

            return ActionResult.Success("Inventory transaction settled.");
        }

        public ActionResult TryAdd(InventoryItem item, int amount = 1)
        {
            if (amount <= 0 || !counts.TryGetValue(item, out int current))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "The item must be valid and the amount must be positive.");
            }

            if (current > int.MaxValue - amount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.CapacityExceeded,
                    $"Adding {amount} {item} would exceed inventory capacity.");
            }

            counts[item] = current + amount;
            return ActionResult.Success($"Added {amount} {item}.");
        }

        public ActionResult TryRemove(InventoryItem item, int amount = 1)
        {
            if (amount <= 0 || !counts.TryGetValue(item, out int current))
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "The item must be valid and the amount must be positive.");
            }

            if (current < amount)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InsufficientResource,
                    $"Not enough {item}: required {amount}, available {current}.");
            }

            counts[item] = current - amount;
            return ActionResult.Success($"Removed {amount} {item}.");
        }

        private static void ValidateInitialCount(int count, string parameterName)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Initial inventory counts cannot be negative.");
            }
        }
    }
}
