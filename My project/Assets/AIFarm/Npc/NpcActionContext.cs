using System;
using AIFarm.Farming;
using AIFarm.Inventory;
using AIFarm.Time;

namespace AIFarm.Npc
{
    public sealed class NpcActionContext
    {
        public NpcActionContext(FarmField field, FarmInventory inventory, GameClock clock)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public FarmField Field { get; }

        public FarmInventory Inventory { get; }

        public GameClock Clock { get; }
    }
}
