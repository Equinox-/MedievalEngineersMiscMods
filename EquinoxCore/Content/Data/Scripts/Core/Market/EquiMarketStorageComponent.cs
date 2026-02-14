using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiMarketStorageComponent))]
    [ReplicatedComponent(typeof(EquiMarketStorageComponentReplicable))]
    public partial class EquiMarketStorageComponent : MyEntityComponent, IMyEventProxy
    {
        private EquiMarketManager _manager;
        private readonly OffloadedDictionary<MarketOrderLocalId, LocalMarketOrder> _orders = new OffloadedDictionary<MarketOrderLocalId, LocalMarketOrder>();

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _manager = MySession.Static?.Components.Get<EquiMarketManager>();
        }

        #region Events

        private void OrderChangedOnServer(MarketOrderOperation op, in LocalMarketOrder order)
        {
            AssertOnlyServer();
            RaiseOrderChanged(op, in order);
            MyAPIGateway.Multiplayer?.RaiseEvent(this, x => x.OrderChanged_Sync, op, order);
        }

        private void RaiseOrderChanged(MarketOrderOperation op, in LocalMarketOrder order)
        {
            OnOrderChanged?.Invoke(this, op, in order);
            _manager?.RaiseOrderChanged(this, op, in order);
        }

        private static void OrderSolvedOnServer(EquiMarketStorageComponent seller,
            in LocalMarketOrder sellOrder,
            uint sellPricePerItem,
            EquiMarketStorageComponent buyer,
            in LocalMarketOrder buyOrder,
            uint buyPricePerItem,
            uint count)
        {
            AssertOnlyServer();
            RaiseOrderSolved(seller, in sellOrder, sellPricePerItem, buyer, in buyOrder, buyPricePerItem, count);
            if (MyAPIGateway.Multiplayer == null) return;
            var rpc = new OrderSolvedRpc
            {
                SellOrderId = sellOrder.LocalId,
                SellPricePerItem = sellPricePerItem,
                BuyOrderId = buyOrder.LocalId,
                BuyPricePerItem = buyPricePerItem,
                Count = count
            };
            if (seller == buyer)
                MyAPIGateway.Multiplayer.RaiseEvent(seller, x => x.OrderSolved_Local, rpc);
            else
            {
                // Purposefully not using a blocking event here to avoid slow and expensive barriers causing hitches. This means delivery isn't guaranteed,
                // even for clients that see replicables for both markets as present.
                // This doesn't break synchronization because this event is only for order solving, and order sync is done separately.
                MyAPIGateway.Multiplayer.RaiseEvent(seller, x => x.OrderSolved_Remote, buyer.Entity.Id, rpc);
            }
        }

        private static void RaiseOrderSolved(EquiMarketStorageComponent seller, in LocalMarketOrder sellOrder, uint sellPricePerItem,
            EquiMarketStorageComponent buyer, in LocalMarketOrder buyOrder, uint buyPricePerItem, uint count)
        {
            seller.OnOrderSolved?.Invoke(seller, in sellOrder, sellPricePerItem, buyer, in buyOrder, buyPricePerItem, count);
            if (buyer != seller) buyer.OnOrderSolved?.Invoke(seller, in sellOrder, sellPricePerItem, buyer, in buyOrder, buyPricePerItem, count);
            seller._manager?.RaiseOrderSolved(seller, in sellOrder, sellPricePerItem, buyer, in buyOrder, buyPricePerItem, count);
        }

        #endregion

        #region Sync

        [Event, Reliable, Broadcast]
        private void OrderChanged_Sync(MarketOrderOperation op, LocalMarketOrder order)
        {
            switch (op)
            {
                case MarketOrderOperation.Created:
                {
                    ref var handle = ref _orders.Add(in order.LocalId);
                    handle = order;
                    RaiseOrderChanged(op, in handle);
                    break;
                }
                case MarketOrderOperation.Edited:
                case MarketOrderOperation.Collected:
                case MarketOrderOperation.Cancelled:
                {
                    if (!_orders.TryGetValue(in order.LocalId, out var handle))
                        return;
                    handle.Value = order;
                    RaiseOrderChanged(op, in handle.Value);
                    break;
                }
                case MarketOrderOperation.BeforeRemoved:
                {
                    if (!_orders.TryGetValue(in order.LocalId, out var handle))
                        return;
                    RaiseOrderChanged(op, in handle.Value);
                    _orders.Remove(in order.LocalId);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }
        }

        [RpcSerializable]
        private struct OrderSolvedRpc
        {
            public ulong SellOrderId;

            [Serialize(MyPrimitiveFlags.Variant)]
            public uint SellPricePerItem;

            public ulong BuyOrderId;

            [Serialize(MyPrimitiveFlags.Variant)]
            public uint BuyPricePerItem;

            [Serialize(MyPrimitiveFlags.Variant)]
            public uint Count;
        }

        [Event, Reliable, Broadcast]
        private void OrderSolved_Remote(EntityId buyerId, OrderSolvedRpc args)
        {
            if (_orders.TryGetValue(args.SellOrderId, out var sellHandle)
                && Scene.TryGetEntity(buyerId, out var buyerEntity)
                && buyerEntity.Components.TryGet(out EquiMarketStorageComponent buyer)
                && buyer._orders.TryGetValue(args.BuyOrderId, out var buyHandle))
            {
                RaiseOrderSolved(this, in sellHandle.Value, args.SellPricePerItem, buyer, in buyHandle.Value, args.BuyPricePerItem, args.Count);
            }
        }

        [Event, Reliable, Broadcast]
        private void OrderSolved_Local(OrderSolvedRpc args)
        {
            if (_orders.TryGetValue(args.SellOrderId, out var sellHandle)
                && _orders.TryGetValue(args.BuyOrderId, out var buyHandle))
            {
                RaiseOrderSolved(this, in sellHandle.Value, args.SellPricePerItem, this, in buyHandle.Value, args.BuyPricePerItem, args.Count);
            }
        }

        #endregion

        #region Accessor

        /// <summary>
        /// Called whenever an order changes state in this market.
        /// </summary>
        public event DelMarketOrderChanged OnOrderChanged;

        /// <summary>
        /// Called whenever two orders exchange items involving this market.
        /// </summary>
        public event DelMarketOrderSolved OnOrderSolved;

        /// <summary>
        /// Read-only view of all orders in this market.
        /// </summary>
        public LocalMarketOrderEnumerable Orders => new LocalMarketOrderEnumerable(this);

        /// <summary>
        /// Attempts to get a handle to an existing order in this market.
        /// The handle will be valid as long as modifications aren't made.
        /// Returns false if the order does not exist.
        /// </summary>
        public bool TryGetLocalOrderHandle(MarketOrderLocalId id, out OrderHandle handle)
        {
            if (_orders.TryGetValue(id, out var internalHandle))
            {
                handle = new OrderHandle(internalHandle);
                return true;
            }

            handle = default;
            return false;
        }

        /// <summary>
        /// Attempts to get the current state of an existing order in this market.
        /// Returns false if the order does not exist.
        /// </summary>
        public bool TryGetLocalOrder(MarketOrderLocalId id, out LocalMarketOrder order)
        {
            if (_orders.TryGetValue(id, out var handle))
            {
                order = handle.Value;
                return true;
            }

            order = default;
            return false;
        }

        internal int OrderCount => _orders.Count;

        public struct MarketOrderEnumerator : IRefReadonlyEnumerator<LocalMarketOrder>
        {
            private PagedFreeList<LocalMarketOrder>.Enumerator _backing;

            internal MarketOrderEnumerator(EquiMarketStorageComponent storage) => _backing = storage._orders.Values.GetEnumerator();

            public void Dispose() => _backing.Dispose();

            public bool MoveNext() => _backing.MoveNext();

            public ref readonly LocalMarketOrder Current => ref _backing.Current.Value;

            LocalMarketOrder IEnumerator<LocalMarketOrder>.Current => Current;

            void IEnumerator.Reset() => _backing.Reset();

            object IEnumerator.Current => Current;
        }

        public readonly struct OrderHandle
        {
            private readonly PagedFreeList<LocalMarketOrder>.Handle _handle;

            public OrderHandle(PagedFreeList<LocalMarketOrder>.Handle handle) => _handle = handle;

            public bool IsValid => _handle.IsValid;

            public ref readonly LocalMarketOrder Value => ref _handle.Value;
        }

        #endregion

        #region Persistence

        public override bool IsSerialized => _orders.Count > 0;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiMarketStorageComponent)base.Serialize(copy);
            ob.Orders = new List<LocalMarketOrder>(_orders.Count);
            foreach (var order in _orders.Values)
                ob.Orders.Add(order.Value);
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiMarketStorageComponent)builder;
            if (ob.Orders != null)
                foreach (var order in ob.Orders)
                {
                    if (!_orders.TryGetValue(in order.LocalId, out var handle))
                        handle = _orders.AddHandle(in order.LocalId);
                    handle.Value = order;
                }
        }

        #endregion
    }

    public readonly struct LocalMarketOrderEnumerable : IReadOnlyCollection<LocalMarketOrder>,
        IConcreteEnumerable<LocalMarketOrder, EquiMarketStorageComponent.MarketOrderEnumerator>
    {
        private readonly EquiMarketStorageComponent _storage;

        internal LocalMarketOrderEnumerable(EquiMarketStorageComponent markets) => _storage = markets;

        public EquiMarketStorageComponent.MarketOrderEnumerator GetEnumerator() => new EquiMarketStorageComponent.MarketOrderEnumerator(_storage);

        public int Count => _storage.OrderCount;

        IEnumerator<LocalMarketOrder> IEnumerable<LocalMarketOrder>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketStorageComponent : MyObjectBuilder_EntityComponent
    {
        [XmlElement("Order")]
        public List<LocalMarketOrder> Orders;
    }

    public class EquiMarketStorageComponentReplicable : MyComponentReplicable<EquiMarketStorageComponent>
    {
        // Market contents are always sent to all users.
        private readonly PriorityCalculator _priorityFunction = (_, client) => 1;

        protected override IMyReplicable GetParent()
        {
            var parent = (MyEntityReplicable<MyEntity>)base.GetParent();
            parent.PriorityFunction = _priorityFunction;
            return parent;
        }
    }
}