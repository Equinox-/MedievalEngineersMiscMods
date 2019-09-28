using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.Inventory;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.Core.Mirz.Extensions
{
    public static class MrzInventoryExtensions
    {
        #region Inventory        

        /// <summary>
        /// Add items of the definition id specified.
        /// 
        /// If Id is an item, simply add the items.
        /// If Id is a tag, add a random item with that tag.
        /// If Id is loot table, add results of that loot table.
        /// </summary>
        /// <param name="inventory"></param>
        /// <param name="id"></param>
        /// <param name="amount"></param>
        /// <returns>True if item added, false if item didn't fit</returns>
        public static bool AddItemsFuzzyOrLoot(this MyInventoryBase inventory, MyDefinitionId id, int amount = 1)
        {
            var result = true;
            if (id.TypeId == typeof(MyObjectBuilder_LootTableDefinition))
            {
                var lootTableDefinition = MyDefinitionManager.Get<MyLootTableDefinition>(id);
                for (var i = 0; i < amount; i++)
                    inventory.GenerateContent(lootTableDefinition);
            }
            else if (id.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
            {
                result = inventory.AddItemsWithTag(id.SubtypeId, amount, true);
            }
            else
            {
                result = inventory.AddItems(id, amount);
            }

            return result;
        }

        /// <summary>
        /// Removes all items from the inventory. Call on the server-side for best results.
        /// </summary>
        /// <param name="inventory"></param>
        public static void Clear(this MyInventoryBase inventory)
        {
            if (inventory == null)
                return;

            var items = inventory.Items;
            for (var i = items.Count - 1; i >= 0; i--)
                inventory.Remove(items.ItemAt(i));
        }

        #endregion
    }
}
