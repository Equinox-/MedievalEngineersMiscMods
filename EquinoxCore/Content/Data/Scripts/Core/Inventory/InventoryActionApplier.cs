using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.Inventory;
using VRage.Collections;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Inventory
{
    public static class InventoryActionApplier
    {
        public static bool CanApply(MyInventoryBase items, ListReader<ImmutableInventoryAction> actions,
            LuckyLoot.LootContext? luckContext = null)
        {
            return CanApply<byte>(items, actions, luckContext);
        }

        public static bool Apply(MyInventoryBase items, ListReader<ImmutableInventoryAction> actions,
            LuckyLoot.LootContext? luckContext = null, bool continueOnFailure = false)
        {
            return Apply<byte>(items, actions, luckContext, continueOnFailure: continueOnFailure);
        }

        public static bool CanApply<TInstance>(MyInventoryBase items, ListReader<ImmutableInventoryAction> actions,
            LuckyLoot.LootContext? luckContext = null,
            ActionWithArg<TInstance, ImmutableInventoryAction>? errorReporter = null)
        {
            using (ItemCollection.Borrow(out var collection))
            {
                collection.OverwriteFrom(items);
                return Apply(collection, actions, luckContext, errorReporter);
            }
        }

        public static bool Apply<TInstance>(MyInventoryBase items, ListReader<ImmutableInventoryAction> actions,
            LuckyLoot.LootContext? luckContext = null,
            ActionWithArg<TInstance, ImmutableInventoryAction>? errorReporter = null,
            bool continueOnFailure = false)
        {
            var luck = luckContext ?? LuckyLoot.DefaultLoot;
            var success = true;
            foreach (var action in actions)
            {
                switch (action.Mode)
                {
                    case ImmutableInventoryAction.InventoryActionMode.GiveTakeItem:
                    {
                        if (action.Amount > 0)
                            success = action.TargetId.TypeId == typeof(MyObjectBuilder_ItemTagDefinition)
                                ? items.AddItemsWithTag(action.TargetId.SubtypeId, action.Amount)
                                : items.AddItems(action.TargetId, action.Amount);
                        else
                            success = action.TargetId.TypeId == typeof(MyObjectBuilder_ItemTagDefinition)
                                ? items.RemoveItemsWithTag(action.TargetId.SubtypeId, -action.Amount)
                                : items.RemoveItems(action.TargetId, -action.Amount);
                        break;
                    }

                    case ImmutableInventoryAction.InventoryActionMode.RepairDamageItem:
                    {
                        var remaining = action.Amount;
                        if (action.TargetId.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
                        {
                            foreach (var item in items.Items)
                                if (item is MyDurableItem durable && item.HasTag(action.TargetId.SubtypeId))
                                {
                                    ApplyDurability(durable, ref remaining);
                                    if (durable.Durability == 0)
                                    {
                                        var broken = durable.GetDefinition().BrokenItem;
                                        var amount = item.Amount;
                                        if (!items.Remove(item))
                                            break;

                                        if (broken.HasValue && !items.AddItems(broken.Value, amount))
                                            break;
                                    }

                                    if (remaining == 0)
                                        break;
                                }
                        }
                        else
                        {
                            foreach (var item in items.Items)
                                if (item is MyDurableItem durable && item.DefinitionId == action.TargetId)
                                {
                                    ApplyDurability(durable, ref remaining);
                                    if (durable.Durability == 0)
                                    {
                                        var broken = durable.GetDefinition().BrokenItem;
                                        var amount = item.Amount;
                                        if (!items.Remove(item))
                                            break;
                                        if (broken.HasValue && !items.AddItems(broken.Value, amount))
                                            break;
                                    }

                                    if (remaining == 0)
                                        break;
                                }
                        }

                        success = remaining == 0;
                        break;
                    }

                    case ImmutableInventoryAction.InventoryActionMode.GiveTakeLootTable:
                    {
                        using (ItemCollection.Borrow(out var tmp))
                        {
                            using (PoolManager.Get(out HashSet<MyStringHash> tmpSet))
                            {
                                var table = MyDefinitionManager.Get<MyLootTableDefinition>(action.TargetId);
                                for (var pass = 0; pass < Math.Abs(action.Amount); pass++)
                                {
                                    tmpSet.Clear();
                                    tmp.GenerateLuckyContent(table, luck, tmpSet);
                                }
                            }

                            if (action.Amount > 0)
                            {
                                foreach (var item in tmp.Items)
                                    success &= items.Add(item);
                            }
                            else
                            {
                                foreach (var item in tmp.Items)
                                    success &= items.RemoveItems(item.DefinitionId, item.Amount);
                            }
                        }

                        break;
                    }

                    default:
                        throw new Exception("Bad mode");
                }

                if (!success)
                {
                    errorReporter?.Invoke(action);
                    if (!continueOnFailure)
                        return false;
                }
            }

            return success;
        }

        private static void ApplyDurability(MyDurableItem item, ref int remaining)
        {
            if (remaining > 0)
            {
                var max = item.GetDefinition().MaxDurability;
                var add = Math.Min(max - item.Durability, remaining);
                remaining -= add;
                item.Durability += add;
            }
            else
            {
                var remove = Math.Min(-remaining, item.Durability);
                remaining += remove;
                item.Durability -= remove;
            }
        }

        public static readonly Action<IMyPlayer, ImmutableInventoryAction> NotifyUserIncapableAction = (player, action) =>
        {
            switch (action.Mode)
            {
                case ImmutableInventoryAction.InventoryActionMode.GiveTakeItem:
                {
                    var itemName = MyDefinitionManager.Get<MyInventoryItemDefinition>(action.TargetId)?.DisplayNameText ??
                                   MyDefinitionManager.Get<MyItemTagDefinition>(action.TargetId)?.DisplayNameText ??
                                   action.TargetId.SubtypeName;
                    if (action.Amount > 0)
                        player.ShowNotification("You need space for " + action.Amount + " more " + itemName);
                    else
                        player.ShowNotification("You need " + -action.Amount + " " + itemName);
                    break;
                }

                case ImmutableInventoryAction.InventoryActionMode.RepairDamageItem:
                {
                    var itemName = MyDefinitionManager.Get<MyInventoryItemDefinition>(action.TargetId)?.DisplayNameText ??
                                   MyDefinitionManager.Get<MyItemTagDefinition>(action.TargetId)?.DisplayNameText;
                    if (action.Amount > 0)
                        player.ShowNotification("You need " + itemName + " with at least " + action.Amount + " missing durability");
                    else
                        player.ShowNotification("You need " + -action.Amount + " durability of " + itemName);
                    break;
                }

                case ImmutableInventoryAction.InventoryActionMode.GiveTakeLootTable:
                {
                    var tableName = action.TargetId.SubtypeName;
                    if (action.Amount > 0)
                        player.ShowNotification("You need space for " + action.Amount + " more " + tableName);
                    else
                        player.ShowNotification("You need " + -action.Amount + " " + tableName);
                    break;
                }

                default:
                    throw new Exception("Invalid action mode " + action.Mode);
            }
        };
    }
}