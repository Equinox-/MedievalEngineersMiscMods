using System;
using System.Collections;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities.Inventory.Constraints;
using Sandbox.Game.Players;
using VRage.Definitions.Inventory;
using VRage.Game;

namespace Equinox76561198048419394.Core.Market
{
    public static class MarketQueries
    {
        /// <summary>
        /// Creates a filtered view over all markets.
        /// </summary>
        public static FilteredMarketEnumerable Filter(this in MarketEnumerable markets, in MarketFilter filter) =>
            new FilteredMarketEnumerable(markets, in filter);

        /// <summary>
        /// Creates a filtered view over a market's local orders.
        /// </summary>
        public static FilteredLocalOrderEnumerable Filter(this in LocalMarketOrderEnumerable orders, in MarketOrderFilter filter) =>
            new FilteredLocalOrderEnumerable(in orders, in filter);

        /// <summary>
        /// Creates a filtered view over the remote orders of multiple markets.
        /// </summary>
        public static FilteredRemoteOrderEnumerable Filter(this in RemoteOrderEnumerable orders, in MarketOrderFilter orderFilter) =>
            new FilteredRemoteOrderEnumerable(in orders.Markets, in orderFilter);

        /// <summary>
        /// Expands a collection of markets into a collection of all orders in those markets.
        /// </summary>
        public static RemoteOrderEnumerable Orders(this in MarketEnumerable markets) =>
            new RemoteOrderEnumerable(new FilteredMarketEnumerable(in markets, default));

        /// <summary>
        /// Expands a collection of markets into a collection of all orders in those markets. 
        /// </summary>
        public static RemoteOrderEnumerable Orders(this in FilteredMarketEnumerable markets) => new RemoteOrderEnumerable(in markets);

        public static bool Test(this in MarketFilter filter, EquiMarketStorageComponent host) => true;

        public static bool Test(this in MarketOrderFilter filter, in LocalMarketOrder order)
        {
            if (filter.CreatorId != null && order.CreatorId != filter.CreatorId.Value) return false;
            if (filter.Type != null && order.Type != filter.Type.Value) return false;
            if (filter.MinPricePerItem != null && order.DesiredPricePerItem < filter.MinPricePerItem.Value) return false;
            if (filter.MaxPricePerItem != null && order.DesiredPricePerItem > filter.MaxPricePerItem.Value) return false;

            switch (filter.RawItemFilter)
            {
                case MyInventoryItemDefinition item:
                    return item.Id == order.Item;
                case MyItemTagDefinition tag:
                {
                    foreach (var tagItem in tag.Items)
                        if (tagItem.Id == order.Item)
                            return true;
                    return false;
                }
                case MyInventoryConstraint constraint:
                    return constraint.Check(order.Item);
                default:
                    return true;
            }
        }

        public struct FilteredMarketEnumerator : IEnumerator<EquiMarketStorageComponent>
        {
            private EquiMarketManager.MarketEnumerator _backing;
            private readonly MarketFilter _filter;

            internal FilteredMarketEnumerator(in MarketEnumerable manager, in MarketFilter filter)
            {
                _backing = manager.GetEnumerator();
                _filter = filter;
            }

            public void Dispose() => _backing.Dispose();

            public bool MoveNext()
            {
                while (_backing.MoveNext())
                    if (_filter.Test(_backing.Current))
                        return true;
                return false;
            }

            public EquiMarketStorageComponent Current => _backing.Current;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => ((IEnumerator)_backing).Reset();
        }

        public struct FilteredLocalOrderEnumerator : IRefReadonlyEnumerator<LocalMarketOrder>
        {
            private EquiMarketStorageComponent.MarketOrderEnumerator _backing;
            private readonly MarketOrderFilter _filter;

            internal FilteredLocalOrderEnumerator(in LocalMarketOrderEnumerable orders, in MarketOrderFilter filter)
            {
                _backing = orders.GetEnumerator();
                _filter = filter;
            }

            public void Dispose() => _backing.Dispose();

            public bool MoveNext()
            {
                while (_backing.MoveNext())
                    if (_filter.Test(in _backing.Current))
                        return true;
                return false;
            }

            public ref readonly LocalMarketOrder Current => ref _backing.Current;

            LocalMarketOrder IEnumerator<LocalMarketOrder>.Current => Current;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => ((IEnumerator)_backing).Reset();
        }

        public struct RemoteOrderEnumerator : IEnumerator<RemoteMarketOrder>
        {
            private FilteredMarketEnumerator _markets;
            private EquiMarketStorageComponent.MarketOrderEnumerator _orders;
            private bool _ordersInit;

            internal RemoteOrderEnumerator(in FilteredMarketEnumerator markets)
            {
                _markets = markets;
                _orders = default;
                _ordersInit = false;
            }

            private void DisposeOrders()
            {
                if (!_ordersInit) return;
                _orders.Dispose();
                _ordersInit = false;
            }

            public void Dispose()
            {
                DisposeOrders();
                _markets.Dispose();
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_ordersInit)
                    {
                        if (_orders.MoveNext())
                            return true;
                        DisposeOrders();
                    }

                    if (!_markets.MoveNext()) return false;

                    var market = _markets.Current;
                    if (market == null) continue;
                    _orders = market.Orders.GetEnumerator();
                    _ordersInit = true;
                }
            }

            public RemoteMarketOrder Current => new RemoteMarketOrder { Storage = _markets.Current, Order = _orders.Current };

            public ref readonly LocalMarketOrder CurrentOrder => ref _orders.Current;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                DisposeOrders();
                ((IEnumerator)_markets).Reset();
            }
        }

        public struct FilteredRemoteOrderEnumerator : IEnumerator<RemoteMarketOrder>
        {
            private RemoteOrderEnumerator _backing;
            private readonly MarketOrderFilter _filter;

            internal FilteredRemoteOrderEnumerator(in RemoteOrderEnumerator backing, in MarketOrderFilter filter)
            {
                _backing = backing;
                _filter = filter;
            }

            public void Dispose() => _backing.Dispose();

            public bool MoveNext()
            {
                while (_backing.MoveNext())
                    if (_filter.Test(in _backing.CurrentOrder))
                        return true;
                return false;
            }

            public RemoteMarketOrder Current => _backing.Current;

            public ref readonly LocalMarketOrder CurrentOrder => ref _backing.CurrentOrder;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => ((IEnumerator)_backing).Reset();
        }
    }

    public readonly struct FilteredMarketEnumerable : IConcreteEnumerable<EquiMarketStorageComponent, MarketQueries.FilteredMarketEnumerator>
    {
        private readonly MarketEnumerable _markets;
        private readonly MarketFilter _filter;

        internal FilteredMarketEnumerable(in MarketEnumerable markets, in MarketFilter filter)
        {
            _markets = markets;
            _filter = filter;
        }

        public MarketQueries.FilteredMarketEnumerator GetEnumerator() => new MarketQueries.FilteredMarketEnumerator(in _markets, in _filter);

        IEnumerator<EquiMarketStorageComponent> IEnumerable<EquiMarketStorageComponent>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct FilteredLocalOrderEnumerable : IConcreteEnumerable<LocalMarketOrder, MarketQueries.FilteredLocalOrderEnumerator>
    {
        private readonly LocalMarketOrderEnumerable _orders;
        private readonly MarketOrderFilter _filter;

        internal FilteredLocalOrderEnumerable(in LocalMarketOrderEnumerable orders, in MarketOrderFilter filter)
        {
            _orders = orders;
            _filter = filter;
        }

        public MarketQueries.FilteredLocalOrderEnumerator GetEnumerator() => new MarketQueries.FilteredLocalOrderEnumerator(in _orders, in _filter);

        IEnumerator<LocalMarketOrder> IEnumerable<LocalMarketOrder>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct RemoteOrderEnumerable : IConcreteEnumerable<RemoteMarketOrder, MarketQueries.RemoteOrderEnumerator>
    {
        internal readonly FilteredMarketEnumerable Markets;

        internal RemoteOrderEnumerable(in FilteredMarketEnumerable markets) => Markets = markets;

        public MarketQueries.RemoteOrderEnumerator GetEnumerator() => new MarketQueries.RemoteOrderEnumerator(Markets.GetEnumerator());

        IEnumerator<RemoteMarketOrder> IEnumerable<RemoteMarketOrder>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct FilteredRemoteOrderEnumerable : IConcreteEnumerable<RemoteMarketOrder, MarketQueries.FilteredRemoteOrderEnumerator>
    {
        private readonly FilteredMarketEnumerable _markets;
        private readonly MarketOrderFilter _orderFilter;

        internal FilteredRemoteOrderEnumerable(in FilteredMarketEnumerable markets, in MarketOrderFilter orderFilter)
        {
            _markets = markets;
            _orderFilter = orderFilter;
        }

        public MarketQueries.FilteredRemoteOrderEnumerator GetEnumerator() => new MarketQueries.FilteredRemoteOrderEnumerator(
            new MarketQueries.RemoteOrderEnumerator(_markets.GetEnumerator()), _orderFilter);

        IEnumerator<RemoteMarketOrder> IEnumerable<RemoteMarketOrder>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct MarketFilter
    {
    }

    public struct MarketOrderFilter
    {
        /// <summary>
        /// Filter returned orders with the same <see cref="LocalMarketOrder.CreatorId"/>.
        /// </summary>
        public long? CreatorId;

        /// <summary>
        /// Filter returned orders with the same <see cref="LocalMarketOrder.Type"/>.
        /// </summary>
        public MarketOrderType? Type;

        internal MyDefinitionBase RawItemFilter;

        /// <summary>
        /// Filter returned orders with a <see cref="LocalMarketOrder.DesiredPricePerItem"/> greater than or equal to this value.
        /// </summary>
        public int? MinPricePerItem;

        /// <summary>
        /// Filter returned orders with a <see cref="LocalMarketOrder.DesiredPricePerItem"/> less than or equal to this value.
        /// </summary>
        public int? MaxPricePerItem;

        /// <summary>
        /// Filter returned orders with an <see cref="LocalMarketOrder.Item"/> contained within this tag.
        /// </summary>
        public MyItemTagDefinition TagFilter
        {
            get => RawItemFilter as MyItemTagDefinition;
            set => RawItemFilter = value;
        }

        /// <summary>
        /// Filter returned orders with an <see cref="LocalMarketOrder.Item"/> equal to this item.
        /// </summary>
        public MyInventoryItemDefinition ItemFilter
        {
            get => RawItemFilter as MyInventoryItemDefinition;
            set => RawItemFilter = value;
        }

        /// <summary>
        /// Filter returned orders with an <see cref="LocalMarketOrder.Item"/> that matches this constraint.
        /// </summary>
        public MyInventoryConstraint Constraint
        {
            get => RawItemFilter as MyInventoryConstraint;
            set => RawItemFilter = value;
        }

        /// <summary>
        /// Filter returned orders with an <see cref="LocalMarketOrder.CreatorId"/> equal to this identity.
        /// </summary>
        public MyIdentity Creator
        {
            get => CreatorId.HasValue ? MyIdentities.Static?.GetIdentity(CreatorId.Value) : null;
            set => CreatorId = value?.Id;
        }

        public MarketOrderFilter(MyIdentity creator = null, MarketOrderType? type = null, MyInventoryItemDefinition item = null,
            MyItemTagDefinition tag = null, MyInventoryConstraint constraint = null, int? minPricePerItem = null, int? maxPricePerItem = null)
        {
            CreatorId = creator?.Id;
            Type = type;
            if ((item != null ? 1 : 0) + (tag != null ? 1 : 0) + (constraint != null ? 1 : 0) > 1)
                throw new ArgumentException("Only one of item, tag, or constraint can be set");
            RawItemFilter = (MyDefinitionBase)item ?? (MyDefinitionBase)tag ?? constraint;
            MinPricePerItem = minPricePerItem;
            MaxPricePerItem = maxPricePerItem;
        }
    }
}