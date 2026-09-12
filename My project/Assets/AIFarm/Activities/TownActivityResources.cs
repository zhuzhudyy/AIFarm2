using System;
using System.Collections.Generic;
using AIFarm.Core;
using AIFarm.Inventory;
using AIFarm.Npc;
using AIFarm.Time;

namespace AIFarm.Activities
{
    [Serializable]
    public sealed class ActivityRules
    {
        public double fishingGameSeconds = 1200d;
        public double pickingGameSeconds = 360d;
        public double supplyGameSeconds = 240d;
        public double fruitRegrowthGameSeconds = GameClock.SecondsPerDay;
        public int fruitsPerTree = 3;
        public int fishPerCatch = 1;
        public int waterCarryCapacity = 18;
        public int fertilizerCarryCapacity = 9;
    }

    [Serializable]
    public sealed class ActivityResourceSnapshot
    {
        public string targetId;
        public int fruitRemaining;
        public double regrowsAtGameSeconds;
    }

    /// <summary>
    /// Public town warehouse and exclusive interaction points. All settlement runs
    /// on the Unity main thread after navigation and the simulated interaction.
    /// Reservations deliberately do not survive loading: actors replan safely.
    /// </summary>
    public sealed class TownActivityResources
    {
        public const string FishingOneId = "fishing-1";
        public const string FishingTwoId = "fishing-2";
        public const string WellId = "well";
        public static readonly string[] OrchardIds = { "fruit-1", "fruit-2", "fruit-3", "fruit-4" };

        private readonly GameClock clock;
        private readonly FarmInventory warehouse;
        private readonly Dictionary<string, Reservation> reservations = new Dictionary<string, Reservation>();
        private readonly Dictionary<string, ActivityResourceSnapshot> trees = new Dictionary<string, ActivityResourceSnapshot>();

        public TownActivityResources(GameClock clock, FarmInventory warehouse, ActivityRules rules = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
            Rules = rules ?? new ActivityRules();
            if (!Positive(Rules.fishingGameSeconds) || !Positive(Rules.pickingGameSeconds) ||
                !Positive(Rules.supplyGameSeconds) || !Positive(Rules.fruitRegrowthGameSeconds) ||
                Rules.fruitsPerTree < 1 || Rules.fishPerCatch < 1 || Rules.waterCarryCapacity < 1 ||
                Rules.fertilizerCarryCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(rules));
            Reset();
        }

        public ActivityRules Rules { get; }

        public bool Contains(string targetId) => targetId != null &&
            (targetId == FishingOneId || targetId == FishingTwoId || targetId == WellId || trees.ContainsKey(targetId));

        public bool IsAvailable(string targetId)
        {
            Refresh();
            return Contains(targetId) && !reservations.ContainsKey(targetId) &&
                (!trees.TryGetValue(targetId, out ActivityResourceSnapshot tree) || tree.fruitRemaining > 0);
        }

        public ActionResult TryReserve(string targetId, ResidentId residentId)
        {
            if (!residentId.IsValid || !Contains(targetId)) return Failure("活动目标或居民不存在。");
            if (reservations.TryGetValue(targetId, out Reservation existing))
                return existing.owner == residentId ? ActionResult.Success("已持有活动位置。") : Failure("位置正被其他居民使用。");
            if (!IsAvailable(targetId))
                return ActionResult.Failure(ActionFailureReason.InsufficientResource, "果实尚未再生。");
            reservations.Add(targetId, new Reservation { owner = residentId, startedAt = clock.ElapsedGameSeconds });
            return ActionResult.Success("已预约活动位置。");
        }

        // Call on arrival so walking time cannot count as fishing or picking.
        public ActionResult BeginInteraction(string targetId, ResidentId residentId)
        {
            if (!Owns(targetId, residentId)) return Failure("居民没有持有该活动位置。");
            reservations[targetId].startedAt = clock.ElapsedGameSeconds;
            return ActionResult.Success("开始活动计时。");
        }

        public ActionResult Release(string targetId, ResidentId residentId)
        {
            if (Owns(targetId, residentId)) reservations.Remove(targetId);
            return ActionResult.Success("活动位置已释放。");
        }

        public void ReleaseAll(ResidentId residentId)
        {
            foreach (string id in new List<string>(reservations.Keys)) Release(id, residentId);
        }

        public int GetFruitRemaining(string targetId)
        {
            Refresh();
            return targetId != null && trees.TryGetValue(targetId, out ActivityResourceSnapshot tree) ? tree.fruitRemaining : 0;
        }

        public double GetRegrowthRemaining(string targetId)
        {
            Refresh();
            return targetId != null && trees.TryGetValue(targetId, out ActivityResourceSnapshot tree) && tree.fruitRemaining == 0
                ? Math.Max(0d, tree.regrowsAtGameSeconds - clock.ElapsedGameSeconds) : 0d;
        }

        public ActionResult TryFish(string targetId, ResidentId residentId)
        {
            if (targetId != FishingOneId && targetId != FishingTwoId) return Failure("这不是钓位。");
            ActionResult ready = CheckReady(targetId, residentId, Rules.fishingGameSeconds);
            if (ready.Failed) return ready;
            ActionResult result = warehouse.TryAdd(InventoryItem.Fish, Rules.fishPerCatch);
            if (result.Succeeded) reservations.Remove(targetId);
            return result.Succeeded ? ActionResult.Success($"钓到 {Rules.fishPerCatch} 条鱼，已存入公共仓库。") : result;
        }

        public ActionResult TryPickFruit(string targetId, ResidentId residentId)
        {
            if (targetId == null || !trees.TryGetValue(targetId, out ActivityResourceSnapshot tree)) return Failure("果树不存在。");
            ActionResult ready = CheckReady(targetId, residentId, Rules.pickingGameSeconds);
            if (ready.Failed) return ready;
            Refresh();
            if (tree.fruitRemaining < 1) return ActionResult.Failure(ActionFailureReason.InsufficientResource, "果实已经采完。");
            ActionResult result = warehouse.TryAdd(InventoryItem.Fruit);
            if (result.Failed) return result;
            tree.fruitRemaining--;
            if (tree.fruitRemaining == 0) tree.regrowsAtGameSeconds = clock.ElapsedGameSeconds + Rules.fruitRegrowthGameSeconds;
            reservations.Remove(targetId);
            return ActionResult.Success("采到 1 个水果，已存入公共仓库。");
        }

        public ActionResult RefillSupplies(ResidentId residentId)
        {
            ActionResult ready = CheckReady(WellId, residentId, Rules.supplyGameSeconds);
            if (ready.Failed) return ready;
            int water = Math.Max(0, Rules.waterCarryCapacity - warehouse.GetCount(InventoryItem.Water));
            int fertilizer = Math.Min(warehouse.GetCount(InventoryItem.Compost),
                Math.Max(0, Rules.fertilizerCarryCapacity - warehouse.GetCount(InventoryItem.Fertilizer)));
            ActionResult result = warehouse.TryApply(new Dictionary<InventoryItem, int>
            {
                [InventoryItem.Water] = water,
                [InventoryItem.Compost] = -fertilizer,
                [InventoryItem.Fertilizer] = fertilizer
            });
            if (result.Succeeded) reservations.Remove(WellId);
            return result.Succeeded ? ActionResult.Success($"水井补水 {water} 份；将 {fertilizer} 份杂草堆肥制成肥料。") : result;
        }

        public ActionResult ConsumeFood()
        {
            foreach (InventoryItem item in new[] { InventoryItem.Fruit, InventoryItem.Fish, InventoryItem.Carrot })
            {
                if (warehouse.Has(item)) return warehouse.TryRemove(item);
            }

            return ActionResult.Failure(ActionFailureReason.InsufficientResource, "公共仓库没有可食用的水果、鱼或胡萝卜。");
        }

        public ActivityResourceSnapshot[] Capture()
        {
            Refresh();
            var snapshots = new List<ActivityResourceSnapshot>();
            foreach (string id in OrchardIds)
            {
                ActivityResourceSnapshot tree = trees[id];
                snapshots.Add(new ActivityResourceSnapshot { targetId = id, fruitRemaining = tree.fruitRemaining,
                    regrowsAtGameSeconds = tree.regrowsAtGameSeconds });
            }
            return snapshots.ToArray();
        }

        public ActionResult Restore(ActivityResourceSnapshot[] snapshots)
        {
            if (snapshots == null || snapshots.Length == 0) { Reset(); return ActionResult.Success("旧存档采用初始果园资源。"); }
            if (snapshots.Length != OrchardIds.Length) return Failure("果园存档缺少果树。");
            var unique = new HashSet<string>();
            foreach (ActivityResourceSnapshot item in snapshots)
            {
                if (item == null || item.targetId == null || !trees.ContainsKey(item.targetId) || !unique.Add(item.targetId) ||
                    item.fruitRemaining < 0 || item.fruitRemaining > Rules.fruitsPerTree ||
                    double.IsNaN(item.regrowsAtGameSeconds) || double.IsInfinity(item.regrowsAtGameSeconds) || item.regrowsAtGameSeconds < 0d)
                    return Failure("果园存档含无效资源状态。");
            }
            reservations.Clear();
            foreach (ActivityResourceSnapshot item in snapshots)
                trees[item.targetId] = new ActivityResourceSnapshot { targetId = item.targetId,
                    fruitRemaining = item.fruitRemaining, regrowsAtGameSeconds = item.regrowsAtGameSeconds };
            return ActionResult.Success("果园资源已恢复，活动位置等待重新预约。");
        }

        public void Reset()
        {
            reservations.Clear();
            trees.Clear();
            foreach (string id in OrchardIds)
                trees.Add(id, new ActivityResourceSnapshot { targetId = id, fruitRemaining = Rules.fruitsPerTree });
        }

        private void Refresh()
        {
            foreach (ActivityResourceSnapshot tree in trees.Values)
                if (tree.fruitRemaining == 0 && clock.ElapsedGameSeconds >= tree.regrowsAtGameSeconds)
                {
                    tree.fruitRemaining = Rules.fruitsPerTree;
                    tree.regrowsAtGameSeconds = 0d;
                }
        }

        private bool Owns(string targetId, ResidentId residentId) => targetId != null && residentId.IsValid &&
            reservations.TryGetValue(targetId, out Reservation reservation) && reservation.owner == residentId;

        private ActionResult CheckReady(string targetId, ResidentId residentId, double duration)
        {
            if (!Owns(targetId, residentId)) return Failure("居民没有持有该活动位置。");
            return clock.ElapsedGameSeconds - reservations[targetId].startedAt + 0.000001d >= duration ?
                ActionResult.Success() : Failure("活动尚未完成，仍需等待模拟时间。");
        }

        private static bool Positive(double value) => value > 0d && !double.IsInfinity(value) && !double.IsNaN(value);
        private static ActionResult Failure(string message) => ActionResult.Failure(ActionFailureReason.InvalidState, message);
        private sealed class Reservation { public ResidentId owner; public double startedAt; }
    }
}
