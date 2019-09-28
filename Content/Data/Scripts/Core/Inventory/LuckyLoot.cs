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

namespace Equinox76561198048419394.Core.Inventory
{
    /// <summary>
    /// Copy of MyInventoryBaseLootExtensions with lucky rolls
    /// </summary>
    public static class LuckyLoot
    {
        public static readonly LootContext DefaultLoot = new LootContext(1, 0);

        public struct LootContext
        {
            public readonly float RollMultiplier;
            public readonly float RollAdditive;

            public LootContext(float rollMultiplier, float rollAdditive)
            {
                RollMultiplier = rollMultiplier;
                RollAdditive = rollAdditive;
            }
        }


        public static void GenerateLuckyContent(this MyInventoryBase inventory, MyLootTableDefinition lootTableDefinition,
            LootContext context, HashSet<MyStringHash> blacklistedLootTables = null)
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
                        var nestedTable = MyDefinitionManager.Get<MyLootTableDefinition>(row.ItemDefinition.Value);
                        if (nestedTable != null && !blacklistedLootTables.Contains(nestedTable.Id.SubtypeId))
                        {
                            inventory.GenerateLuckyContent(nestedTable, context, blacklistedLootTables);
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

            var rollLucky = Math.Round(lootTableDefinition.Rolls * context.RollMultiplier + context.RollAdditive);
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
                        var nestedTable = MyDefinitionManager.Get<MyLootTableDefinition>(row2.ItemDefinition.Value);
                        if (nestedTable != null)
                            inventory.GenerateLuckyContent(nestedTable, context);
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