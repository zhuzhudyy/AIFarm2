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
            int carrots = 0)
        {
            ValidateInitialCount(carrotSeeds, nameof(carrotSeeds));
            ValidateInitialCount(water, nameof(water));
            ValidateInitialCount(fertilizer, nameof(fertilizer));
            ValidateInitialCount(carrots, nameof(carrots));

            counts = new Dictionary<InventoryItem, int>
            {
                [InventoryItem.CarrotSeed] = carrotSeeds,
                [InventoryItem.Water] = water,
                [InventoryItem.Fertilizer] = fertilizer,
                [InventoryItem.Carrot] = carrots
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
            int carrots)
        {
            if (carrotSeeds < 0 || water < 0 || fertilizer < 0 || carrots < 0)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "Saved inventory counts cannot be negative.");
            }

            counts[InventoryItem.CarrotSeed] = carrotSeeds;
            counts[InventoryItem.Water] = water;
            counts[InventoryItem.Fertilizer] = fertilizer;
            counts[InventoryItem.Carrot] = carrots;
            return ActionResult.Success("Farm inventory restored.");
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
