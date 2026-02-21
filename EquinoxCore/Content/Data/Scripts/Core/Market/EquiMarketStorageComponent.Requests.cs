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

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketStorageComponent
    {
        [Automatic]
        private readonly EquiMarketPermissionsComponent _permissions = null;

        /// <summary>
        /// Determines if buy orders can be created by the local player.
        /// </summary>
        public bool CanCreateBuyOrders(bool local = true) => TryGetLocalIdentity(out var identity)
                                                             && _permissions.PermissionsFor(identity, identity.Id, local)
                                                                 .Has(EquiMarketPermission.CreateBuyOrderPaired);

        /// <summary>
        /// Determines if a new buy order can be created by the local player.
        /// </summary>
        public bool CanCreateBuyOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
            => TryGetLocalIdentity(out var identity) && CanCreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local);

        /// <summary>
        /// Called by the local player to create a new buy order. If they don't have enough currency, based on the
        /// <see cref="EquiMarketManager.CurrencySystem"/>, in the provided inventory the order won't be created.
        /// </summary>
        public void RequestCreateBuyOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity) || !CanCreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local)) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CreateBuyOrderImpl(identity, inventory, item, pricePerItem, itemAmount);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCreateBuyOrder_Sync, inventory.Id,
                    (SerializableDefinitionId)item.Id, pricePerItem, itemAmount);
        }

        /// <summary>
        /// Determines if sell orders can be created by the local player.
        /// </summary>
        public bool CanCreateSellOrders(bool local = true) => TryGetLocalIdentity(out var identity)
                                                              && _permissions.PermissionsFor(identity, identity.Id, local)
                                                                  .Has(EquiMarketPermission.CreateSellOrderPaired);

        /// <summary>
        /// Determines if a new sell order can be created by the local player.
        /// </summary>
        public bool CanCreateSellOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
            => TryGetLocalIdentity(out var identity) && CanCreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local);

        /// <summary>
        /// Called by the local player to create a new sell order. If they don't have enough of the item in the provided inventory the order won't be created.
        /// </summary>
        public void RequestCreateSellOrder(MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, bool local = true)
        {
            if (!TryGetLocalIdentity(out var identity) || !CanCreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount, local)) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CreateSellOrderImpl(identity, inventory, item, pricePerItem, itemAmount);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCreateSellOrder_Sync, inventory.Id,
                    (SerializableDefinitionId)item.Id, pricePerItem, itemAmount);
        }

        /// <summary>
        /// Determines if the local player can cancel the given order.
        /// </summary>
        public bool CanCancelOrder(MarketOrderLocalId id, bool local = true) =>
            TryGetLocalIdentity(out var identity) && CanCancelOrderImpl(identity, id, local);

        /// <summary>
        /// Called by a local player to cancel an order they created.
        /// </summary>
        public void RequestCancelOrder(MarketOrderLocalId id)
        {
            if (!CanCancelOrder(id)) return;
            if (MyMultiplayerModApi.Static.IsServer)
                CancelOrder(id);
            else
                MyAPIGateway.Multiplayer.RaiseEvent(this, m => m.RequestCancelOrder_Sync, (ulong)id);
        }

        /// <summary>
        /// Determines if the local player can collect the given order.
        /// </summary>
        public bool CanCollectOrder(MyInventoryBase inventory, MarketOrderLocalId id) =>
            TryGetLocalIdentity(out var identity) && CanCollectOrderImpl(identity, inventory, id, true);

        /// <summary>
        /// Called by a local player to collect items and currency for an order they created into the provided inventory.
        /// </summary>
        public void RequestCollectOrder(MyInventoryBase inventory, MarketOrderLocalId id)
        {
            if (!CanCollectOrder(inventory, id)) return;
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
                || !CanCreateBuyOrderImpl(sender, inventory, item, pricePerItem, itemAmount, true))
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
                || !CanCreateSellOrderImpl(sender, inventory, item, pricePerItem, itemAmount, true))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CreateSellOrderImpl(sender, inventory, item, pricePerItem, itemAmount);
        }

        [Event, Reliable, Server]
        private void RequestCancelOrder_Sync(ulong orderIdRaw)
        {
            MarketOrderLocalId id = orderIdRaw;
            if (!TryGetLocalOrderHandle(id, out var order) || order.Value.CreatorId != NetworkTrust.SenderIdentity?.Id)
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
            if (!TryGetTrustedInventory(inventoryId, out var inventory) || !CanCollectOrderImpl(sender, inventory, id, true))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            CollectOrderImpl(inventory, id);
        }

        #endregion

        #region Implementations

        private bool CanCreateBuyOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount,
            bool local)
        {
            return identity != null
                   && _manager.CurrencySystem.TotalValue(inventory) >= pricePerItem * (ulong)itemAmount
                   && _permissions.PermissionsFor(identity, identity.Id, local)
                       .Has(IsPaired() ? EquiMarketPermission.CreateBuyOrderPaired : EquiMarketPermission.CreateBuyOrder);

            bool IsPaired()
            {
                var buyable = 0u;
                foreach (var handle in _orders)
                {
                    ref var order = ref handle.Value;
                    if (order.Type != MarketOrderType.Sell || order.Item != item.Id || order.DesiredPricePerItem > pricePerItem)
                        continue;
                    buyable += order.RemainingItemAmount;
                    if (buyable >= itemAmount)
                        return true;
                }

                return false;
            }
        }

        private bool CanCreateSellOrderImpl(MyIdentity identity, MyInventoryBase inventory, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount,
            bool local)
        {
            return identity != null
                   && inventory.CanRemoveItems(item.Id, (int)itemAmount)
                   && _permissions.PermissionsFor(identity, identity.Id, local)
                       .Has(IsPaired() ? EquiMarketPermission.CreateSellOrderPaired : EquiMarketPermission.CreateSellOrder);

            bool IsPaired()
            {
                var sellable = 0u;
                foreach (var handle in _orders)
                {
                    ref var order = ref handle.Value;
                    if (order.Type != MarketOrderType.Buy || order.Item != item.Id || order.DesiredPricePerItem < pricePerItem)
                        continue;
                    sellable += order.RemainingItemAmount;
                    if (sellable >= itemAmount)
                        return true;
                }

                return false;
            }
        }

        private bool CanCancelOrderImpl(MyIdentity identity, MarketOrderLocalId id, bool local)
        {
            if (identity == null || !TryGetLocalOrderHandle(id, out var orderHandle)) return false;
            ref readonly var order = ref orderHandle.Value;
            return _permissions.PermissionsFor(identity, order.CreatorId, local).Has(EquiMarketPermission.CancelOrder);
        }

        private bool CanCollectOrderImpl(MyIdentity identity, MyInventoryBase inventory, MarketOrderLocalId id, bool local)
        {
            if (identity == null || !TryGetLocalOrderHandle(id, out var orderHandle)) return false;
            ref readonly var order = ref orderHandle.Value;
            return order.HasCollectableResources(out _, out _)
                   && _permissions.PermissionsFor(identity, order.CreatorId, local).Has(EquiMarketPermission.CollectOrder);
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