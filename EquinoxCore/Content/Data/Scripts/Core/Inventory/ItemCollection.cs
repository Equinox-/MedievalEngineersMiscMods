using System;
using System.Collections.Generic;
using VRage;
using VRage.Collections;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Collections.Concurrent;

namespace Equinox76561198048419394.Core.Inventory
{
    /// <summary>
    /// No synchronization inventory with no limits.
    /// </summary>
    public class ItemCollection : MyInventoryBase
    {
        private static readonly MyConcurrentPool<ItemCollection> Pool = new MyConcurrentPool<ItemCollection>(0,
            (x) => { x._items.Clear(); });

        private readonly List<MyInventoryItem> _items = new List<MyInventoryItem>();

        public static ReturnHandle Borrow(out ItemCollection collection)
        {
            collection = Pool.Get();
            return new ReturnHandle(collection);
        }

        public struct ReturnHandle : IDisposable
        {
            public ItemCollection Handle;

            public ReturnHandle(ItemCollection handle)
            {
                Handle = handle;
            }

            public void Dispose()
            {
                Pool.Return(Handle);
                Handle = null;
            }
        }

        public override int ComputeAmountThatFits(MyDefinitionId id)
        {
            return int.MaxValue;
        }

        public override int GetItemAmount(MyDefinitionId contentId)
        {
            var i = 0;
            foreach (var item in _items)
                if (item.DefinitionId == contentId)
                    i += item.Amount;
            return i;
        }

        public override int GetItemStackAmount(MyDefinitionId contentId)
        {
            var i = 0;
            foreach (var item in _items)
                if (item.DefinitionId == contentId)
                    i++;
            return i;
        }

        public override bool CanAddItems(MyDefinitionId item, int amount)
        {
            return true;
        }

        public override bool CanRemoveItems(MyDefinitionId item, int amount)
        {
            return GetItemAmount(item) >= amount;
        }

        public override MyInventoryItem FindItem(uint itemId)
        {
            foreach (var item in _items)
                if (item.ItemId == itemId)
                    return item;
            return null;
        }

        public override MyInventoryItem FindItem(MyDefinitionId id)
        {
            foreach (var item in _items)
                if (item.DefinitionId == id)
                    return item;
            return null;
        }

        public override bool Add(MyInventoryItem item, NewItemParams parameters = NewItemParams.None)
        {
            if ((parameters & NewItemParams.AsNewStack) != 0)
            {
                _items.Add(item);
                return true;
            }

            var stackSize = item.GetDefinition().MaxStackAmount;
            for (var i = 0; i < _items.Count; i++)
            {
                var other = _items[i];
                if (!other.CanStack(item))
                    continue;
                var add = Math.Min(item.Amount, stackSize - other.Amount);
                _items[i] = other.Clone(other.Amount + add);
                item = item.Clone(item.Amount - add);
                if (item.Amount == 0)
                    break;
            }

            if (item.Amount > 0)
                _items.Add(item);
            return true;
        }

        public override bool Remove(MyInventoryItem item, int? amount = null)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].ItemId != item.ItemId)
                    continue;

                if (amount == null)
                {
                    _items.RemoveAtFast(i);
                    return true;
                }

                if (amount.Value > _items[i].Amount)
                    return false;
                _items[i].Amount -= amount.Value;
                return true;
            }

            return false;
        }

        public override void CountItems(Dictionary<MyDefinitionId, int> results)
        {
            foreach (var item in _items)
                results[item.DefinitionId] = item.Amount;
        }

        public override bool AddItems(MyDefinitionId id, int amount, NewItemParams parameters = NewItemParams.None)
        {
            if (amount == 0)
                return false;
            var item = MyInventoryItem.Create(id, amount);
            return item != null && Add(item, parameters);
        }

        public override bool RemoveItems(MyDefinitionId id, int amount)
        {
            if (GetItemAmount(id) < amount)
                return false;
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].DefinitionId != id)
                    continue;

                if (_items[i].Amount <= amount)
                {
                    amount -= _items[i].Amount;
                    _items.RemoveAtFast(i);
                }
                else
                {
                    _items[i] = _items[i].Clone(_items[i].Amount - amount);
                    amount = 0;
                }

                if (amount == 0)
                    break;
            }

            return amount == 0;
        }

        public override bool TransferItemsFrom(MyInventoryBase sourceInventory, MyInventoryItem item, int amount)
        {
            if (sourceInventory == null || item == null || amount > item.Amount)
                return false;
            if (amount == 0)
                return true;
            var limitedItem = amount < item.Amount ? item.Clone(amount) : item;
            if (!CanAddItems(item.DefinitionId, amount) && this != sourceInventory)
                return false;
            if (this == sourceInventory)
                return sourceInventory.Remove(item, amount) && Add(limitedItem);
            if (!Add(limitedItem))
                return false;
            if (sourceInventory.Remove(item, amount))
                return true;
            Remove(item, amount);
            return false;
        }

        public override void OnContentsChanged()
        {
        }

        public void OverwriteFrom(MyInventoryBase other)
        {
            _items.Clear();
            foreach (var item in other.Items)
                _items.Add(item.Clone(item.Amount));
        }

        /// <summary>
        /// Transfers all items in
        /// </summary>
        /// <param name="other"></param>
        public void TransferAllTo(MyInventoryBase other, NewItemParams newItemParams = NewItemParams.None)
        {
            var force = (newItemParams & NewItemParams.ForcedInsertion) != 0;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item == null || item.Amount == 0) continue;
                var fits = force ? item.Amount : Math.Min(item.Amount, other.ComputeAmountThatFits(item.DefinitionId));
                if (fits == 0) continue;

                if (fits == item.Amount)
                {
                    if (!other.Add(item, newItemParams)) continue;
                    _items.RemoveAtFast(i);
                    --i;
                    continue;
                }

                var cloned = item.Clone(fits);
                if (!other.Add(cloned, newItemParams)) continue;
                item.Amount -= fits;
            }
        }

        public override int MaxItemCount => int.MaxValue;

        public override MyFixedPoint CurrentMass
        {
            get
            {
                var mass = 0f;
                foreach (var item in _items)
                    mass += item.GetDefinition().Mass * item.Amount;
                return (MyFixedPoint) mass;
            }
        }

        public override int ItemCount => _items.Count;
        public override ListReader<MyInventoryItem> Items => _items;
    }
}