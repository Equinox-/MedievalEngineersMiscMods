using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Inventory;
using VRage.Collections;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Utils;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util
{
    /// <summary>
    /// Copy of MyInventoryBaseLootExtensions with lucky rolls
    /// </summary>
    public static class LuckyLoot
    {

        public static void GenerateLuckyContent(this MyInventoryBase inventory, MyLootTableDefinition lootTableDefinition,
            float rollMultiplier, float rollAdditive, HashSet<MyStringHash> blacklistedLootTables = null)
        {
            var cachingHashSet = new CachingHashSet<MyLootTableDefinition.Row>();
            foreach (var item in lootTableDefinition.LootTable)
                cachingHashSet.Add(item);
            cachingHashSet.ApplyChanges();
            var num = 0f;
            if (blacklistedLootTables == null)
                blacklistedLootTables = new HashSet<MyStringHash>();
            blacklistedLootTables.Add(lootTableDefinition.Id.SubtypeId);
            foreach (var row in cachingHashSet)
            {
                if (row.AlwaysDrops && row.ItemDefinition != null)
                {
                    if (row.ItemDefinition.Value.TypeId == typeof(MyObjectBuilder_LootTableDefinition))
                    {
                        var myLootTableDefinition = MyDefinitionManager.Get<MyLootTableDefinition>(row.ItemDefinition.Value);
                        if (myLootTableDefinition != null && !blacklistedLootTables.Contains(myLootTableDefinition.Id.SubtypeId))
                        {
                            inventory.GenerateLuckyContent(myLootTableDefinition, rollMultiplier, rollAdditive, blacklistedLootTables);
                        }
                    }
                    else
                    {
                        AddItemsFuzzy(inventory, row.ItemDefinition.Value, row.Amount);
                    }

                    if (row.IsUnique)
                    {
                        cachingHashSet.Remove(row, false);
                        continue;
                    }
                }

                num += row.Weight;
            }

            var rollLucky = Math.Round(lootTableDefinition.Rolls * rollMultiplier + rollAdditive);
            for (var j = 0; j < rollLucky; j++)
            {
                var num2 = MyRandom.Instance.NextFloat(0f, num);
                cachingHashSet.ApplyChanges();
                foreach (var row2 in cachingHashSet)
                {
                    num2 -= row2.Weight;
                    if (num2 > 0f || row2.ItemDefinition == null)
                        continue;
                    if (row2.ItemDefinition.Value.TypeId == typeof(MyObjectBuilder_LootTableDefinition))
                    {
                        var myLootTableDefinition2 = MyDefinitionManager.Get<MyLootTableDefinition>(row2.ItemDefinition.Value);
                        if (myLootTableDefinition2 != null)
                        {
                            inventory.GenerateLuckyContent(myLootTableDefinition2, rollMultiplier, rollAdditive);
                        }
                    }
                    else
                    {
                        AddItemsFuzzy(inventory, row2.ItemDefinition.Value, row2.Amount);
                    }

                    if (row2.IsUnique)
                    {
                        cachingHashSet.Remove(row2, false);
                        num -= row2.Weight;
                    }

                    break;
                }
            }

            blacklistedLootTables.Remove(lootTableDefinition.Id.SubtypeId);
        }

        private static void AddItemsFuzzy(MyInventoryBase inventory, MyDefinitionId itemDefinition, int amount)
        {
            if (itemDefinition.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
            {
                inventory.AddItemsWithTag(itemDefinition.SubtypeId, amount, true);
                return;
            }

            inventory.AddItems(itemDefinition, amount);
        }
    }
}