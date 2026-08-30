using AIFarm.Core;
using AIFarm.Inventory;
using NUnit.Framework;

namespace AIFarm.Tests.EditMode
{
    public sealed class FarmInventoryTests
    {
        [Test]
        public void TryRemove_WhenInsufficient_FailsWithoutChangingCount()
        {
            var inventory = new FarmInventory(carrotSeeds: 1);

            ActionResult result = inventory.TryRemove(InventoryItem.CarrotSeed, 2);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InsufficientResource));
            Assert.That(inventory.GetCount(InventoryItem.CarrotSeed), Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void TryAdd_WithNonPositiveAmount_FailsWithoutChangingCount(int amount)
        {
            var inventory = new FarmInventory(carrots: 3);

            ActionResult result = inventory.TryAdd(InventoryItem.Carrot, amount);

            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(ActionFailureReason.InvalidArgument));
            Assert.That(inventory.GetCount(InventoryItem.Carrot), Is.EqualTo(3));
        }
    }
}
