using System;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketStorageComponent
    {
        private static void AssertOnlyServer()
        {
            if (!MyMultiplayerModApi.Static.IsServer) throw new ArgumentException("Only the server is allowed to make arbitrary orders");
        }

        private static void ValidateOrder(in LocalMarketOrder order)
        {
            if (order.LocalId.IsNull) throw new ArgumentException("Order ID must not be null");
            if (order.RemainingItemAmount > order.DesiredItemAmount)
                throw new ArgumentException("Orders should never have more items remaining than were ordered");
            // Validate the order.
            switch (order.Type)
            {
                case MarketOrderType.Buy:
                    if (order.RemainingItemAmount * order.DesiredPricePerItem > order.StoredMoneyAmount)
                        throw new ArgumentException(
                            $"Buy orders should never have more remaining items ({order.RemainingItemAmount}) than can be afforded at the desired price ({order.DesiredPricePerItem}) using the stored money {order.StoredItemAmount}");
                    break;
                case MarketOrderType.Sell:
                    if (order.RemainingItemAmount > order.StoredItemAmount)
                        throw new ArgumentException(
                            $"Sell orders should never have more remaining items ({order.RemainingItemAmount}) than stored items ({order.StoredItemAmount})");
                    break;
                case MarketOrderType.CancelledBuy:
                case MarketOrderType.CancelledSell:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Creates a new buy order on the server. The caller should have already verified (and withdrawn) the necessary money, moneyAmount, from the player.
        /// The money amount must be greater or equal to (pricePerItem * itemAmount).
        /// This function is only usable on the server.
        /// </summary>
        public MarketOrderLocalId CreateBuyOrder(MyIdentity identity, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount, uint moneyAmount)
        {
            AssertOnlyServer();
            var order = new LocalMarketOrder
            {
                LocalId = MarketOrderLocalId.Allocate(),
                CreatorId = identity.Id,
                CreatedAt = DateTime.UtcNow,
                Type = MarketOrderType.Buy,
                Item = item.Id,
                DesiredPricePerItem = pricePerItem,
                DesiredItemAmount = itemAmount,
                RemainingItemAmount = itemAmount,
                StoredItemAmount = 0,
                StoredMoneyAmount = moneyAmount,
            };
            return CreateOrder(in order);
        }

        /// <summary>
        /// Creates a new buy order on the server. The caller should have already verified (and withdrawn) the necessary items from the player.
        /// This function is only usable on the server.
        /// </summary>
        public MarketOrderLocalId CreateSellOrder(MyIdentity identity, MyInventoryItemDefinition item, uint pricePerItem, uint itemAmount)
        {
            AssertOnlyServer();
            var order = new LocalMarketOrder
            {
                LocalId = MarketOrderLocalId.Allocate(),
                CreatorId = identity.Id,
                CreatedAt = DateTime.UtcNow,
                Type = MarketOrderType.Sell,
                Item = item.Id,
                DesiredItemAmount = itemAmount,
                DesiredPricePerItem = pricePerItem,
                RemainingItemAmount = itemAmount,
                StoredItemAmount = itemAmount,
                StoredMoneyAmount = 0,
            };
            return CreateOrder(in order);
        }

        private MarketOrderLocalId CreateOrder(in LocalMarketOrder order)
        {
            ValidateOrder(in order);
            _orders.Add(in order.LocalId, in order);
            OrderChangedOnServer(MarketOrderOperation.Created, in order);
            return order.LocalId;
        }

        /// <summary>
        /// Cancels an existing order. This function is only usable on the server.
        /// </summary>
        /// <param name="id">order to cancel</param>
        /// <returns>true if the order was canceled, false if not</returns>
        public bool CancelOrder(MarketOrderLocalId id)
        {
            AssertOnlyServer();
            if (!_orders.TryGetValue(id, out var handle))
                return false;
            ref var storedOrder = ref handle.Value;
            var order = storedOrder;
            switch (order.Type)
            {
                case MarketOrderType.Buy:
                    order.RemainingItemAmount = 0;
                    order.Type = MarketOrderType.CancelledBuy;
                    break;
                case MarketOrderType.Sell:
                    order.RemainingItemAmount = 0;
                    order.Type = MarketOrderType.CancelledSell;
                    break;
                case MarketOrderType.CancelledBuy:
                case MarketOrderType.CancelledSell:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ValidateOrder(in order);
            storedOrder = order;
            OrderChangedOnServer(MarketOrderOperation.Cancelled, in storedOrder);
            return true;
        }

        /// <summary>
        /// Called when collecting items for an order.
        /// </summary>
        /// <param name="userData">user provided data</param>
        /// <param name="item">item that is being collected</param>
        /// <param name="amount">number of items available to collect</param>
        /// <returns>number of items actually collected</returns>
        public delegate int DelCollectItems<TUserData>(ref TUserData userData, in MyDefinitionId item, int amount);

        /// <summary>
        /// Called when collecting money for an order.
        /// </summary>
        /// <param name="userData">user provided data</param>
        /// <param name="amount">amount of money available to collect</param>
        /// <returns>amount of money actually collected</returns>
        public delegate int DelCollectMoney<TUserData>(ref TUserData userData, int amount);

        public enum CollectOrderResult
        {
            NoSuchOrder,
            NothingCollected,
            PartiallyCollected,
            FullyCollectedAndRemoved,
        }

        /// <summary>
        /// Collect the items and money for an order on the server.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="userData"></param>
        /// <param name="collectItems"></param>
        /// <param name="collectMoney"></param>
        /// <typeparam name="TUserData"></typeparam>
        public CollectOrderResult CollectOrder<TUserData>(MarketOrderLocalId id, ref TUserData userData, DelCollectItems<TUserData> collectItems,
            DelCollectMoney<TUserData> collectMoney)
        {
            if (!_orders.TryGetValue(in id, out var handle)) return CollectOrderResult.NoSuchOrder;
            var anythingCollected = false;
            ref var storedOrder = ref handle.Value;
            storedOrder.HasCollectableResources(out var itemsToCollect, out var moneyToCollect);
            if (itemsToCollect == 0 && moneyToCollect == 0) return CollectOrderResult.NothingCollected;

            var order = storedOrder;

            if (itemsToCollect > 0 && collectItems != null)
            {
                var collected = collectItems(ref userData, in order.Item, (int)itemsToCollect);
                if (collected != 0)
                {
                    anythingCollected = true;
                    order.StoredItemAmount = checked((uint)(order.StoredItemAmount - collected));
                }
            }

            if (moneyToCollect > 0 && collectMoney != null)
            {
                var collected = collectMoney(ref userData, (int)moneyToCollect);
                if (collected != 0)
                {
                    anythingCollected = true;
                    order.StoredMoneyAmount = checked((uint)(order.StoredMoneyAmount - collected));
                }
            }

            ValidateOrder(in order);
            storedOrder = order;

            if (storedOrder.StoredItemAmount == 0 && storedOrder.StoredMoneyAmount == 0 && storedOrder.RemainingItemAmount == 0)
            {
                OrderChangedOnServer(MarketOrderOperation.BeforeRemoved, in storedOrder);
                _orders.Remove(in id);
                return CollectOrderResult.FullyCollectedAndRemoved;
            }

            if (!anythingCollected) return CollectOrderResult.NothingCollected;

            OrderChangedOnServer(MarketOrderOperation.Collected, in storedOrder);
            return CollectOrderResult.PartiallyCollected;
        }

        public enum SolveOrderResult
        {
            NoSuchOrder,
            WrongOrderItems,
            WrongOrderType,
            NoAcceptablePrice,
            PartiallySolved,
            FullySolved,
        }

        /// <summary>
        /// Solves a pair of buy and sell orders on the server.
        /// </summary>
        /// <param name="buyOrder">the buy order ID</param>
        /// <param name="sellOrder">the sell order ID</param>
        /// <returns>solving result, an error or a success value</returns>
        public SolveOrderResult SolveOrder(MarketOrderLocalId buyOrder, MarketOrderLocalId sellOrder)
        {
            if (!_orders.TryGetValue(buyOrder, out var buyHandle) || !_orders.TryGetValue(sellOrder, out var sellHandle))
                return SolveOrderResult.NoSuchOrder;
            ref var storedBuy = ref buyHandle.Value;
            ref var storedSell = ref sellHandle.Value;
            if (storedBuy.Item != storedSell.Item)
                return SolveOrderResult.WrongOrderItems;
            if (storedBuy.Type != MarketOrderType.Buy || storedSell.Type != MarketOrderType.Sell)
                return SolveOrderResult.WrongOrderType;
            if (storedBuy.DesiredPricePerItem < storedSell.DesiredPricePerItem)
                return SolveOrderResult.NoAcceptablePrice;

            var buy = storedBuy;
            var sell = storedSell;

            // Executed price for the offer pair is the older offer. Picking the midpoint might seem fairer, but since this isn't a blind auction
            // house the creator of the later order would presumably always browse the existing orders to maximize their price.
            // Picking the older offer's price removes the incentive to manually browse through all the offers.
            var pricePerItem = buy.CreatedAt < sell.CreatedAt ? buy.DesiredPricePerItem : sell.DesiredPricePerItem;

            var items = Math.Min(buy.RemainingItemAmount, sell.RemainingItemAmount);
            var money = items * pricePerItem;

            checked
            {
                sell.RemainingItemAmount -= items;
                sell.StoredItemAmount -= items;
                sell.StoredMoneyAmount += money;

                buy.RemainingItemAmount -= items;
                buy.StoredMoneyAmount -= money;
                buy.StoredItemAmount += items;
            }

            ValidateOrder(in sell);
            ValidateOrder(in buy);
            storedSell = sell;
            storedBuy = buy;

            OrderChangedOnServer(MarketOrderOperation.Solve, in storedSell);
            OrderChangedOnServer(MarketOrderOperation.Solve, in storedBuy);
            OrderSolvedOnServer(this, in storedSell, pricePerItem, this, in storedBuy, pricePerItem, items);

            return storedSell.RemainingItemAmount == 0 && storedBuy.RemainingItemAmount == 0 ? SolveOrderResult.FullySolved : SolveOrderResult.PartiallySolved;
        }
    }
}