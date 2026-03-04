using System;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRageMath;

// ReSharper disable ConvertIfStatementToReturnStatement

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketStorageComponent
    {
        [Automatic]
        private readonly EquiMarketPermissionsComponent _permissions = null;

        public enum CanCreateBuyOrderResult
        {
            Okay,
            NoIdentity,
            InvalidItem,
            NoPermission,
            NotEnoughPairedOrders,
            NotEnoughMoney,
        }

        /// <summary>
        /// Determines if buy orders can be created by the local player.
        /// </summary>
        public CanCreateBuyOrderResult CanCreateBuyOrders(bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCreateBuyOrderResult.NoIdentity;
            if (!_permissions.PermissionsFor(identity, identity.Id, local).Has(EquiMarketPermission.CreateBuyOrderPaired))
                return CanCreateBuyOrderResult.NoPermission;
            return CanCreateBuyOrderResult.Okay;
        }

        /// <summary>
        /// Determines if any buy order for the given item can be created by the local player.
        /// </summary>
        public CanCreateBuyOrderResult CanCreateBuyOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, bool local = true)
            => CanCreateBuyOrder(inventory, item, uint.MaxValue, 0, local);

        /// <summary>
        /// Determines if a new buy order can be created by the local player.
        /// </summary>
        public CanCreateBuyOrderResult CanCreateBuyOrder(MyInventoryBase inventory, MyInventoryItemDefinition item,
            uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCreateBuyOrderResult.NoIdentity;
            return CanCreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local);
        }

        /// <summary>
        /// Called by the local player to create a new buy order. If they don't have enough currency, based on the
        /// <see cref="EquiMarketManager.CurrencySystem"/>, in the provided inventory the order won't be created.
        /// </summary>
        public void RequestCreateBuyOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity)
                || CanCreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local) != CanCreateBuyOrderResult.Okay) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCreateBuyOrder_Sync, inventory.Id,
                    (SerializableDefinitionId)item.Id, pricePerItem, itemAmount);
        }

        public enum CanCreateSellOrderResult
        {
            Okay,
            NoIdentity,
            InvalidItem,
            NoPermission,
            NotEnoughPairedOrders,
            NotEnoughItems,
        }

        /// <summary>
        /// Determines if sell orders can be created by the local player.
        /// </summary>
        public CanCreateSellOrderResult CanCreateSellOrders(bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCreateSellOrderResult.NoIdentity;
            if (!_permissions.PermissionsFor(identity, identity.Id, local).Has(EquiMarketPermission.CreateSellOrderPaired))
                return CanCreateSellOrderResult.NoPermission;
            return CanCreateSellOrderResult.Okay;
        }

        /// <summary>
        /// Determines if any sell order for the given item can be created by the local player.
        /// </summary>
        public CanCreateSellOrderResult CanCreateSellOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, bool local = true)
            => CanCreateSellOrder(inventory, item, 0, 0, local);

        /// <summary>
        /// Determines if a new sell order can be created by the local player.
        /// </summary>
        public CanCreateSellOrderResult CanCreateSellOrder(MyInventoryBase inventory, MyInventoryItemDefinition item,
            uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCreateSellOrderResult.NoIdentity;
            return CanCreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local);
        }

        /// <summary>
        /// Called by the local player to create a new sell order. If they don't have enough of the item in the provided inventory the order won't be created.
        /// </summary>
        public void RequestCreateSellOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity) ||
                CanCreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local) != CanCreateSellOrderResult.Okay) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCreateSellOrder_Sync, inventory.Id,
                    (SerializableDefinitionId)item.Id, pricePerItem, itemAmount);
        }

        public enum CanCancelOrderResult
        {
            Okay,
            NoIdentity,
            NoOrder,
            NoPermission,
        }

        /// <summary>
        /// Determines if the local player can cancel the given order.
        /// </summary>
        public CanCancelOrderResult CanCancelOrder(MarketOrderLocalId id, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCancelOrderResult.NoIdentity;
            return CanCancelOrderImpl(identity, id, local);
        }

        /// <summary>
        /// Called by a local player to cancel an order they created.
        /// </summary>
        public void RequestCancelOrder(MarketOrderLocalId id)
        {
            if (CanCancelOrder(id) != CanCancelOrderResult.Okay) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CancelOrder(id);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCancelOrder_Sync, (ulong)id);
        }

        public enum CanCollectOrderResult
        {
            Okay,
            NoIdentity,
            NoOrder,
            NoPermission,
            NothingToCollect,
        }

        /// <summary>
        /// Determines if the local player can collect the given order.
        /// </summary>
        public CanCollectOrderResult CanCollectOrder(MyInventoryBase inventory, MarketOrderLocalId id)
        {
            if (!TryGetLocalIdentity(out var identity))
                return CanCollectOrderResult.NoIdentity;
            return CanCollectOrderImpl(identity, inventory, id, true);
        }

        /// <summary>
        /// Called by a local player to collect items and currency for an order they created into the provided inventory.
        /// </summary>
        public void RequestCollectOrder(MyInventoryBase inventory, MarketOrderLocalId id)
        {
            if (CanCollectOrder(inventory, id) != CanCollectOrderResult.Okay) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CollectOrderImpl(inventory, id);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCollectOrder_Sync, inventory.Id, (ulong)id);
        }

        #region Sync

        [Event, Reliable, Server]
        private void RequestCreateBuyOrder_Sync(EntityComponentId inventoryId, SerializableDefinitionId itemId, uint pricePerItem, uint itemAmount)
        {
            var sender = NetworkTrust.SenderIdentity;
            if (!TryGetTrustedInventory(inventoryId, out var inventory)
                || sender == null || !MyDefinitionManager.TryGet(itemId, out MyInventoryItemDefinition item)
                || CanCreateBuyOrderImpl(sender, inventory, item, pricePerItem, itemAmount, true) != CanCreateBuyOrderResult.Okay)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CreateBuyOrderImpl(sender, inventory, item, pricePerItem, itemAmount);
        }

        [Event, Reliable, Server]
        private void RequestCreateSellOrder_Sync(EntityComponentId inventoryId, SerializableDefinitionId itemId, uint pricePerItem, uint itemAmount)
        {
            var sender = NetworkTrust.SenderIdentity;
            if (!TryGetTrustedInventory(inventoryId, out var inventory)
                || sender == null || !MyDefinitionManager.TryGet(itemId, out MyInventoryItemDefinition item)
                || CanCreateSellOrderImpl(sender, inventory, item, pricePerItem, itemAmount, true) != CanCreateSellOrderResult.Okay)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CreateSellOrderImpl(sender, inventory, item, pricePerItem, itemAmount);
        }

        [Event, Reliable, Server]
        private void RequestCancelOrder_Sync(ulong orderIdRaw)
        {
            var sender = NetworkTrust.SenderIdentity;
            MarketOrderLocalId id = orderIdRaw;
            if (sender == null || CanCancelOrderImpl(sender, id, true) != CanCancelOrderResult.Okay)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CancelOrder(id);
        }

        [Event, Reliable, Server]
        private void RequestCollectOrder_Sync(EntityComponentId inventoryId, ulong orderIdRaw)
        {
            MarketOrderLocalId id = orderIdRaw;
            var sender = NetworkTrust.SenderIdentity;
            if (!TryGetTrustedInventory(inventoryId, out var inventory) || CanCollectOrderImpl(sender, inventory, id, true) != CanCollectOrderResult.Okay)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CollectOrderImpl(inventory, id);
        }

        #endregion

        #region Implementations

        private CanCreateBuyOrderResult CanCreateBuyOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item,
            uint pricePerItem, uint itemAmount, bool local)
        {
            if (identity == null)
                return CanCreateBuyOrderResult.NoIdentity;
            if (!_permissions.CheckItem(item))
                return CanCreateBuyOrderResult.InvalidItem;
            var perms = _permissions.PermissionsFor(identity, identity.Id, local);
            if (!perms.Has(EquiMarketPermission.CreateBuyOrder))
            {
                if (!perms.Has(EquiMarketPermission.CreateBuyOrderPaired))
                    return CanCreateBuyOrderResult.NoPermission;
                if (!IsPaired())
                    return CanCreateBuyOrderResult.NotEnoughPairedOrders;
            }

            if (itemAmount > 0 && _manager.CurrencySystem.TotalValue(inventory) < pricePerItem * (ulong)itemAmount)
                return CanCreateBuyOrderResult.NotEnoughMoney;
            return CanCreateBuyOrderResult.Okay;

            bool IsPaired()
            {
                var buyable = 0u;
                using (var e = Orders
                           .Filter(new MarketOrderFilter { Type = MarketOrderType.Sell, ItemFilter = item, MaxPricePerItem = pricePerItem })
                           .GetEnumerator())
                    while (e.MoveNext())
                    {
                        ref readonly var order = ref e.Current;
                        buyable += order.RemainingItemAmount;
                        if (buyable >= itemAmount)
                            return true;
                    }

                return false;
            }
        }

        private CanCreateSellOrderResult CanCreateSellOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item,
            uint pricePerItem, uint itemAmount, bool local)
        {
            if (identity == null)
                return CanCreateSellOrderResult.NoIdentity;
            if (!_permissions.CheckItem(item))
                return CanCreateSellOrderResult.InvalidItem;
            var perms = _permissions.PermissionsFor(identity, identity.Id, local);
            if (!perms.Has(EquiMarketPermission.CreateSellOrder))
            {
                if (!perms.Has(EquiMarketPermission.CreateSellOrderPaired))
                    return CanCreateSellOrderResult.NoPermission;
                if (!IsPaired())
                    return CanCreateSellOrderResult.NotEnoughPairedOrders;
            }

            if (itemAmount > 0 && inventory.GetItemAmount(item.Id) < itemAmount) // CanRemoveItems is broken, MEC~555
                return CanCreateSellOrderResult.NotEnoughItems;
            return CanCreateSellOrderResult.Okay;

            bool IsPaired()
            {
                var sellable = 0u;
                using (var e = Orders
                           .Filter(new MarketOrderFilter { Type = MarketOrderType.Buy, ItemFilter = item, MinPricePerItem = pricePerItem })
                           .GetEnumerator())
                    while (e.MoveNext())
                    {
                        ref readonly var order = ref e.Current;
                        sellable += order.RemainingItemAmount;
                        if (sellable >= itemAmount)
                            return true;
                    }

                return false;
            }
        }

        private CanCancelOrderResult CanCancelOrderImpl(MyIdentity identity, MarketOrderLocalId id, bool local)
        {
            if (identity == null)
                return CanCancelOrderResult.NoIdentity;
            if (!TryGetLocalOrderHandle(id, out var orderHandle))
                return CanCancelOrderResult.NoOrder;
            ref readonly var order = ref orderHandle.Value;
            if (!_permissions.PermissionsFor(identity, order.CreatorId, local).Has(EquiMarketPermission.CancelOrder))
                return CanCancelOrderResult.NoPermission;
            return CanCancelOrderResult.Okay;
        }

        private CanCollectOrderResult CanCollectOrderImpl(MyIdentity identity, MyInventoryBase inventory, MarketOrderLocalId id, bool local)
        {
            if (identity == null)
                return CanCollectOrderResult.NoIdentity;
            if (!TryGetLocalOrderHandle(id, out var orderHandle))
                return CanCollectOrderResult.NoOrder;
            ref readonly var order = ref orderHandle.Value;
            if (!_permissions.PermissionsFor(identity, order.CreatorId, local).Has(EquiMarketPermission.CollectOrder))
                return CanCollectOrderResult.NoPermission;
            if (!order.HasCollectableResources(out _, out _))
                return CanCollectOrderResult.NothingToCollect;
            return CanCollectOrderResult.Okay;
        }

        private void CreateBuyOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount)
        {
            var usedCurrency = _manager.CurrencySystem.TakeCurrency(inventory, pricePerItem * (ulong)itemAmount, true, true);
            if (usedCurrency > 0)
                CreateBuyOrder(identity, item, pricePerItem, itemAmount, (uint)usedCurrency);
        }

        private void CreateSellOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount)
        {
            if (inventory.RemoveItems(item.Id, (int)itemAmount))
                CreateSellOrder(identity, item, pricePerItem, itemAmount);
        }

        private void CollectOrderImpl(MyInventoryBase inventory, MarketOrderLocalId id)
        {
            var outerState = new CallbackState { Market = this, Inventory = inventory };
            CollectOrder(id, ref outerState, (ref CallbackState state, in MyDefinitionId item, int amount) =>
            {
                var fits = Math.Min(amount, state.Inventory.ComputeAmountThatFits(item));
                return fits > 0 && state.Inventory.AddItems(item, fits) ? fits : 0;
            }, (ref CallbackState state, int amount) => (int)state.Market._manager.CurrencySystem.GiveCurrency(state.Inventory, (ulong)amount, false));
        }

        private struct CallbackState
        {
            public EquiMarketStorageComponent Market;
            public MyInventoryBase Inventory;
        }

        private static bool TryGetLocalIdentity(out MyIdentity identity)
        {
            identity = NetworkTrust.LocalIdentity;
            return identity != null;
        }

        private bool TryGetTrustedInventory(EntityComponentId id, out MyInventoryBase inventory)
        {
            if (id.TryGetObject(Scene, out var comp) && comp is MyInventoryBase inv && NetworkTrust.IsTrusted(inv, (BoundingBoxD?)null))
            {
                inventory = inv;
                return true;
            }

            inventory = null;
            return false;
        }

        #endregion
    }
}