using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.Game.Players;
using VRage.Collections;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ObjectBuilder.Merging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Market
{
    public static class EquiMarketGenOrderSetExt
    {
        /// <summary>
        /// Retrieve the order state, or create one if necessary.
        /// </summary>
        private static EquiMarketGenOrderState GetState(
            in EquiMarketGenOrderSetDefinition.Order order,
            Dictionary<MyDefinitionId, EquiMarketGenOrderState> states)
        {
            if (!states.TryGetValue(order.Item.Id, out var state))
                states.Add(order.Item.Id, state = new EquiMarketGenOrderState());
            return state;
        }

        /// <summary>
        /// Tries to get a valid market order handle for the given id and order.
        /// </summary>
        private static EquiMarketStorageComponent.OrderHandle TryGetValidOrder(
            EquiMarketStorageComponent storage,
            in EquiMarketGenOrderSetDefinition.Order order,
            ref MarketOrderLocalId id)
        {
            if (!storage.TryGetLocalOrderHandle(id, out var handle))
                return default;
            if (handle.Value.Item == order.Item.Id && handle.Value.Type == (order.Buying ? MarketOrderType.Buy : MarketOrderType.Sell))
                return handle;
            storage.RemoveOrder(id);
            id = default;
            return default;
        }

        /// <summary>
        /// Collect all currency, and up to the limit of items for an order.
        /// Updates the order ID to nil if the order is removed.
        /// </summary>
        private static void CollectOrder(EquiMarketStorageComponent storage, ref MarketOrderLocalId id, uint itemLimit)
        {
            switch (storage.CollectOrder(id, ref itemLimit,
                        (ref uint data, in MyDefinitionId item, int amount) => Math.Min((int)data, amount),
                        (ref uint data, int amount) => amount))
            {
                case EquiMarketStorageComponent.CollectOrderResult.NothingCollected:
                case EquiMarketStorageComponent.CollectOrderResult.PartiallyCollected:
                    break;
                case EquiMarketStorageComponent.CollectOrderResult.NoSuchOrder:
                case EquiMarketStorageComponent.CollectOrderResult.FullyCollectedAndRemoved:
                    id = default;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Creates buy orders to build up the stockpiled items, crafts them into sellable items, and then creates sell orders.
        /// </summary>
        /// <param name="definition">order set</param>
        /// <param name="storage">market to apply to</param>
        /// <param name="states">state holding market orders for each involved item</param>
        /// <param name="actions">number of actions to take, usually <see cref="EquiMarketGenOrderSetDefinition.Amount"/> </param>
        /// <param name="stockpileActions">number of actions to stockpile inputs and outputs up to, usually <see cref="EquiMarketGenOrderSetDefinition.Stockpile"/></param>
        /// <param name="identity">identity to use</param>
        public static void Apply(
            this EquiMarketGenOrderSetDefinition definition,
            EquiMarketStorageComponent storage,
            Dictionary<MyDefinitionId, EquiMarketGenOrderState> states,
            uint actions,
            uint stockpileActions,
            MyIdentity identity)
        {
            // Prune all un-necessary states.
            definition.PruneStates(storage, states);
            var buyActions = actions;
            var sellActions = actions;
            // Run twice to ensure that more buy orders are created after selling items.
            for (var i = 0; i < 2; i++)
            {
                buyActions = MiscMath.SaturatedSubtract(buyActions, definition.BuyInputs(storage, states, buyActions, stockpileActions, identity));
                sellActions = MiscMath.SaturatedSubtract(sellActions, definition.CraftAndSell(storage, states, sellActions, stockpileActions, identity));
            }
        }

        /// <summary>
        /// Creates buy orders so that up to stockpile actions worth of inputs are available.
        /// Up to max actions worth of orders will be created in a single method call.
        /// </summary>
        /// <param name="definition">order set</param>
        /// <param name="storage">market to apply to</param>
        /// <param name="states">state holding market orders for each involved item</param>
        /// <param name="maxActions">maximum number of actions to take</param>
        /// <param name="stockpileActions">number of actions to stockpile inputs up to</param>
        /// <param name="identity">identity to use</param>
        /// <returns>the number of orders that were created</returns>
        public static uint BuyInputs(
            this EquiMarketGenOrderSetDefinition definition,
            EquiMarketStorageComponent storage,
            Dictionary<MyDefinitionId, EquiMarketGenOrderState> states,
            uint maxActions,
            uint stockpileActions,
            MyIdentity identity)
        {
            var used = 0u;
            foreach (var order in definition.Orders.Values)
            {
                // This only applies to buy orders.
                if (!order.Buying) continue;

                // Retrieve the order state, or create one if necessary.
                var state = GetState(in order, states);

                // Gets a valid order, removing the existing one if necessary.
                var handle = TryGetValidOrder(storage, in order, ref state.Order);

                // Determine the number of items to buy.
                var targetCount = stockpileActions * order.BuyAmount;
                var pendingAndStockpiledCount = handle.IsValid ? handle.Value.StoredItemAmount + handle.Value.RemainingItemAmount : 0u;
                var amountToBuy = MiscMath.SaturatedSubtract(targetCount, pendingAndStockpiledCount);
                amountToBuy = Math.Min(amountToBuy, maxActions * order.BuyAmount);
                if (amountToBuy == 0) continue;

                if (handle.IsValid)
                {
                    // Edit the existing order, buying more items and adjusting the price as needed.
                    var newOpenOrderCount = amountToBuy + handle.Value.RemainingItemAmount;
                    var requiredMoney = newOpenOrderCount * order.Price;
                    storage.EditBuyOrder(state.Order,
                        MiscMath.SaturatedSubtract(requiredMoney, handle.Value.StoredMoneyAmount),
                        amountToBuy, newPrice: order.Price);
                }
                else
                {
                    // Create a new order.
                    state.Order = storage.CreateBuyOrder(identity, order.Item, order.Price, amountToBuy, amountToBuy * order.Price);
                }

                // Update the number of actions performed.
                used = Math.Max(used, (amountToBuy + order.BuyAmount - 1) / order.BuyAmount);
            }

            return used;
        }

        /// <summary>
        /// Converts the bought items into sold items, collecting items from the buy orders and creating new sell orders.
        /// </summary>
        /// <param name="definition">order set</param>
        /// <param name="storage">market to apply to</param>
        /// <param name="states">state holding market orders for each involved item</param>
        /// <param name="identity">identity to use</param>
        /// <param name="maxActions">maximum number of actions to take</param>
        /// <param name="stockpileActions">number of actions to stockpile outputs up to</param>
        /// <returns>number of actions taken</returns>
        public static uint CraftAndSell(
            this EquiMarketGenOrderSetDefinition definition,
            EquiMarketStorageComponent storage,
            Dictionary<MyDefinitionId, EquiMarketGenOrderState> states,
            uint maxActions,
            uint stockpileActions,
            MyIdentity identity)
        {
            // Determine the number of times the action can be taken.
            var actionsToTake = Math.Min(maxActions, stockpileActions);
            foreach (var order in definition.Orders.Values)
            {
                // Retrieve the order state, or create one if necessary.
                var state = GetState(in order, states);

                // Gets a valid order, removing the existing one if necessary.
                var handle = TryGetValidOrder(storage, in order, ref state.Order);
                var storedItems = handle.IsValid ? handle.Value.StoredItemAmount : 0;

                uint actionLimit;
                if (order.Buying)
                    // Calculate the number of actions that can be taken before the stored items are exhausted.
                    actionLimit = storedItems / order.BuyAmount;
                else
                    // Calculate the number of actions that can be taken before the stockpile limit is reached.
                    actionLimit = MiscMath.SaturatedSubtract(stockpileActions * order.SellAmount, storedItems) / order.SellAmount;

                if (actionLimit == 0)
                    return 0;
                if (actionLimit < actionsToTake)
                    actionsToTake = actionLimit;
            }

            // Collect items from the buy orders, and increase the number of sell orders.
            foreach (var order in definition.Orders.Values)
            {
                // Retrieve the order state, or create one if necessary.
                var state = GetState(in order, states);

                if (order.Buying)
                {
                    CollectOrder(storage, ref state.Order, actionsToTake * order.BuyAmount);
                    continue;
                }

                if (TryGetValidOrder(storage, in order, ref state.Order).IsValid)
                    // Edit the existing sell order, adding more items to it.
                    storage.EditSellOrder(state.Order, actionsToTake * order.SellAmount, order.Price);
                else
                    // Creates a new sell order.
                    state.Order = storage.CreateSellOrder(identity, order.Item, order.Price, actionsToTake * order.SellAmount);

                // Collect money that is being held in the sell order.
                CollectOrder(storage, ref state.Order, 0);
            }

            return actionsToTake;
        }

        /// <summary>
        /// Prunes orders and state unnecessary for the order set.
        /// </summary>
        /// <param name="definition">order set</param>
        /// <param name="storage">market to apply to</param>
        /// <param name="states">state holding market orders for each involved item</param>
        public static void PruneStates(
            this EquiMarketGenOrderSetDefinition definition,
            EquiMarketStorageComponent storage,
            Dictionary<MyDefinitionId, EquiMarketGenOrderState> states)
        {
            List<MyDefinitionId> toRemove = null;
            try
            {
                foreach (var state in states)
                {
                    if (!definition.Orders.TryGetValue(state.Key, out var order))
                    {
                        if (toRemove == null)
                            toRemove = PoolManager.Get<List<MyDefinitionId>>();
                        toRemove.Add(state.Key);
                        state.Value.RemoveOrders(storage);
                        continue;
                    }
                }
            }
            finally
            {
                if (toRemove != null)
                    PoolManager.Return(ref toRemove);
            }
        }

        /// <summary>
        /// Removes all orders for the given generation order state.
        /// </summary>
        public static void RemoveOrders(this EquiMarketGenOrderState state, EquiMarketStorageComponent storage)
        {
            storage.RemoveOrder(state.Order);
        }
    }

    #region Order Set State

    public class EquiMarketGenOrderState
    {
        /// <summary>
        /// The market order created for this generation order.
        /// </summary>
        public MarketOrderLocalId Order;

        public static void Deserialize(Dictionary<MyDefinitionId, EquiMarketGenOrderState> target, List<MyObjectBuilder_EquiMarketGenOrderState> src)
        {
            target.Clear();
            if (src == null || src.Count == 0)
                return;
            foreach (var state in src)
                target[state.Item] = Deserialize(state);
        }

        public static EquiMarketGenOrderState Deserialize(MyObjectBuilder_EquiMarketGenOrderState state) => new EquiMarketGenOrderState
        {
            Order = state.OrderId,
        };

        public static List<MyObjectBuilder_EquiMarketGenOrderState> Serialize(Dictionary<MyDefinitionId, EquiMarketGenOrderState> states)
        {
            if (states == null || states.Count == 0)
                return null;
            return states
                .Select(state => new MyObjectBuilder_EquiMarketGenOrderState
                {
                    Item = state.Key,
                    OrderId = state.Value.Order,
                }).ToList();
        }
    }

    public class MyObjectBuilder_EquiMarketGenOrderState
    {
        [XmlIgnore]
        public SerializableDefinitionId Item;

        [XmlAttribute]
        public ulong OrderId;

        [XmlAttribute]
        public string Type
        {
            get => Item.TypeIdString;
            set => Item.TypeIdString = value;
        }

        [XmlAttribute]
        public string Subtype
        {
            get => Item.SubtypeName;
            set => Item.SubtypeName = value;
        }
    }

    #endregion

    #region Definition

    [MyDefinitionType(typeof(MyObjectBuilder_EquiMarketGenOrderSet))]
    [MyDependency(typeof(MyInventoryItemDefinition), Recursive = true)]
    public class EquiMarketGenOrderSetDefinition : MyDefinitionBase
    {
        public TimeSpan Interval { get; private set; }
        public uint Amount { get; private set; }
        public uint Stockpile { get; private set; }
        public DictionaryReader<MyDefinitionId, Order> Orders { get; private set; }

        public readonly struct Order
        {
            public readonly MyInventoryItemDefinition Item;
            public readonly int Amount;
            public readonly uint Price;

            public bool Buying => Amount < 0;
            public uint BuyAmount => checked((uint)-Amount);

            public bool Selling => Amount > 0;
            public uint SellAmount => checked((uint)Amount);

            public Order(MyInventoryItemDefinition item, int amount, uint price)
            {
                Item = item;
                Amount = amount;
                Price = price;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            InitInternal(((MyObjectBuilder_EquiMarketGenOrderSet)builder).Config);
        }

        internal void InitInternal(MyObjectBuilder_EquiMarketGenOrderSetConfig ob)
        {
            Interval = ob?.Interval != null ? (TimeSpan)ob.Interval.Value : TimeSpan.FromDays(1);
            Amount = ob?.Amount ?? 1;
            Stockpile = ob?.Stockpile ?? 1;
            if (ob?.Orders?.Count > 0)
            {
                var orders = new Dictionary<MyDefinitionId, Order>(MyDefinitionId.Comparer);
                foreach (var order in ob.Orders)
                {
                    if (order.Amount == 0 || order.Price == 0) continue;
                    var def = MyInventoryItemAdapter.GetDefinition(order.Id);
                    if (def == null)
                    {
                        Log.Warning($"Failed to find referenced item {order.Id}");
                        continue;
                    }

                    orders[def.Id] = new Order(def, order.Amount, order.Price);
                }

                Orders = orders;
            }
            else
                Orders = DictionaryReader<MyDefinitionId, Order>.Empty;
        }

        public MyObjectBuilder_EquiMarketGenOrderSetConfig SerializeConfig() => new MyObjectBuilder_EquiMarketGenOrderSetConfig
        {
            Interval = Interval,
            Amount = Amount,
            Stockpile = Stockpile,
            Orders = Orders.Values.Select(x => new MyObjectBuilder_EquiMarketGenOrderSetConfig.EquiMarketOrderGen
            {
                Id = x.Item.Id,
                Amount = x.Amount,
                Price = x.Price,
            }).ToList(),
        };
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketGenOrderSet : MyObjectBuilder_DefinitionBase
    {
        [XmlElement]
        [FieldMerger(typeof(MyObjectBuilderMerger<MyObjectBuilder_EquiMarketGenOrderSetConfig>))]
        public MyObjectBuilder_EquiMarketGenOrderSetConfig Config;
    }

    public class MyObjectBuilder_EquiMarketGenOrderSetConfig
    {
        /// <summary>
        /// How often this generator will produce this order set.
        /// </summary>
        [XmlElement]
        public TimeDefinition? Interval;

        /// <summary>
        /// Number of times this set will be produced per time interval.
        /// </summary>
        [XmlElement]
        public uint? Amount;

        /// <summary>
        /// Number of orders worth of items to stockpile.
        /// For buy orders, this multiplied by the per-item amount is the number of open buy orders plus unclaimed bought items that will exist.
        /// For sale orders, this multiplied by the per-item amount is the number of open sell orders that will exist.
        /// </summary>
        [XmlElement]
        public uint? Stockpile;

        /// <summary>
        /// Orders that are part of this set.
        /// All orders have to be executed at once, so this can be used to represent a crafting recipe.
        /// </summary>
        [XmlElement("Order")]
        public List<EquiMarketOrderGen> Orders;

        public struct EquiMarketOrderGen
        {
            [XmlIgnore]
            public SerializableDefinitionId Id;

            /// <summary>
            /// Amount of the item per order set.
            /// Positive if the item is produced by the set and sold.
            /// Negative if the item is consumed by the set and bought.
            /// </summary>
            [XmlAttribute]
            public int Amount;

            /// <summary>
            /// Price per item.
            /// </summary>
            [XmlAttribute]
            public uint Price;

            /// <summary>
            /// Inventory item type.
            /// </summary>
            [XmlAttribute]
            public string Type
            {
                get => Id.TypeIdString;
                set => Id.TypeIdString = value;
            }

            /// <summary>
            /// Inventory item subtype.
            /// </summary>
            [XmlAttribute]
            public string Subtype
            {
                get => Id.SubtypeName;
                set => Id.SubtypeName = value;
            }
        }
    }

    #endregion
}