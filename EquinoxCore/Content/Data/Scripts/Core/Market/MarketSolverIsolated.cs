using System;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Market
{
    public static class MarketSolverIsolated
    {
        public static void SolveIsolatedMarket(EquiMarketStorageComponent market, HashSetReader<MyDefinitionId> touchedItems)
        {
            using (PoolManager.Get(out Dictionary<MyDefinitionId, SingleMarketItemState> readItemStates))
            using (PoolManager.Get(out Dictionary<MyDefinitionId, SingleMarketItemState> writeItemStates))
            {
                // Initialize with empty states for the changed items.
                foreach (var touchedItem in touchedItems)
                    writeItemStates[touchedItem] = default;

                while (writeItemStates.Count > 0)
                {
                    // Find the highest buy offer and the lowest sell offer for each changed item.
                    FindPairedOffers(market, writeItemStates);

                    // Swap read and write, then clear write.
                    MyUtils.Swap(ref readItemStates, ref writeItemStates);
                    writeItemStates.Clear();

                    // Execute the paired orders if possible. If changes were made for that item then run it again.
                    foreach (var kv in readItemStates)
                    {
                        var info = kv.Value;
                        switch (market.SolveOrder(info.Buy.Id, info.Sell.Id))
                        {
                            case EquiMarketStorageComponent.SolveOrderResult.NoSuchOrder:
                            case EquiMarketStorageComponent.SolveOrderResult.WrongOrderType:
                            case EquiMarketStorageComponent.SolveOrderResult.NoAcceptablePrice:
                                // Item has run to completion, so don't use it in the next iteration.
                                break;
                            case EquiMarketStorageComponent.SolveOrderResult.PartiallySolved:
                            case EquiMarketStorageComponent.SolveOrderResult.FullySolved:
                                // Item made changes and there may be more orders for it, so use it in the next iteration.
                                writeItemStates[kv.Key] = default;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Populates paired offers from the market for each item that is present in the provided item states.
        /// </summary>
        private static void FindPairedOffers(EquiMarketStorageComponent market, Dictionary<MyDefinitionId, SingleMarketItemState> itemStates)
        {
            using (var enumerator = market.Orders.GetEnumerator())
                while (enumerator.MoveNext())
                {
                    ref readonly var order = ref enumerator.Current;
                    // If nothing is remaining there's no reason to consider the order.
                    if (order.RemainingItemAmount == 0) continue;

                    // If the item wasn't touched then nothing will have changed.
                    if (!itemStates.TryGetValue(order.Item, out var itemState)) continue;

                    switch (order.Type)
                    {
                        case MarketOrderType.Buy:
                            ref var buy = ref itemState.Buy;
                            if (buy.Id.IsNull
                                // Pick the buyer offering the highest price.
                                || order.DesiredPricePerItem > buy.Price
                                // Or in case of a tie, pick the older order.
                                || (order.DesiredPricePerItem == buy.Price && order.CreatedAt > buy.Time))
                            {
                                buy.Set(in order);
                                itemStates[order.Item] = itemState;
                            }

                            break;
                        case MarketOrderType.Sell:
                            ref var sell = ref itemState.Sell;
                            if (sell.Id.IsNull
                                // Pick the seller offering the lowest price.
                                || order.DesiredPricePerItem < sell.Price
                                // Or in case of a tie, pick the older order.
                                || (order.DesiredPricePerItem == sell.Price && order.CreatedAt > sell.Time))
                            {
                                sell.Set(in order);
                                itemStates[order.Item] = itemState;
                            }

                            break;
                        case MarketOrderType.CancelledBuy:
                        case MarketOrderType.CancelledSell:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
        }

        private struct SingleMarketItemState
        {
            public SingleMarketOrderInfo Buy;
            public SingleMarketOrderInfo Sell;
        }

        private struct SingleMarketOrderInfo
        {
            public MarketOrderLocalId Id;
            public uint Price;
            public DateTime Time;

            internal void Set(in LocalMarketOrder order)
            {
                Id = order.LocalId;
                Price = order.DesiredPricePerItem;
                Time = order.CreatedAt;
            }
        }
    }
}