using System;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Library.Utils;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.Serialization;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Market
{
    public enum MarketOrderType : byte
    {
        Buy,
        Sell,
        CancelledBuy,
        CancelledSell,
    }

    public readonly struct MarketOrderLocalId : IEquatable<MarketOrderLocalId>
    {
        private readonly ulong _id;

        public static MarketOrderLocalId Allocate() => 0x8000000000000000UL | (ulong)MyRandom.Instance.NextLong();

        private MarketOrderLocalId(ulong id) => _id = id;

        public bool IsNull => _id == 0;

        public override string ToString() => _id.ToString("X16");
        public bool Equals(MarketOrderLocalId other) => _id == other._id;

        public override bool Equals(object obj) => obj is MarketOrderLocalId other && Equals(other);

        public override int GetHashCode() => _id.GetHashCode();

        public static implicit operator ulong(MarketOrderLocalId id) => id._id;
        public static implicit operator MarketOrderLocalId(ulong id) => new MarketOrderLocalId(id);
    }

    public enum MarketOrderOperation
    {
        Created,
        Edited,
        Collected,
        Cancelled,
        Solve,
        BeforeRemoved,
    }

    /// <summary>
    /// Callback when orders get changed for any reason.
    /// </summary>
    /// <param name="storage">market the order exists within</param>
    /// <param name="op">operation that caused the change</param>
    /// <param name="order">state of the order after the operation</param>
    public delegate void DelMarketOrderChanged(EquiMarketStorageComponent storage, MarketOrderOperation op, in LocalMarketOrder order);

    /// <summary>
    /// Callback when orders get solved against each other.
    /// </summary>
    /// <param name="seller">market the sell order exists within</param>
    /// <param name="sellOrder">state of the sell order after exchanging items</param>
    /// <param name="sellPricePerItem">amount of money the seller received per item</param>
    /// <param name="buyer">market the buy order exists within</param>
    /// <param name="buyOrder">state of the buy order after exchanging items</param>
    /// <param name="buyPricePerItem">amount of money the buyer paid per item</param>
    /// <param name="count">number of items that were exchanged</param>
    public delegate void DelMarketOrderSolved(
        EquiMarketStorageComponent seller, in LocalMarketOrder sellOrder, uint sellPricePerItem,
        EquiMarketStorageComponent buyer, in LocalMarketOrder buyOrder, uint buyPricePerItem,
        uint count);

    [RpcSerializable]
    public struct LocalMarketOrder
    {
        /// <summary>
        /// Local identifier of the order, only valid within the containing <see cref="EquiMarketStorageComponent"/>.
        /// </summary>
        [XmlIgnore]
        [NoSerialize]
        public MarketOrderLocalId LocalId;

        /// <summary>
        /// Identity ID of the creator of this market order.
        /// </summary>
        [XmlAttribute("Creator")]
        [Serialize]
        public long CreatorId;

        /// <summary>
        /// When the order was created.
        /// Note this is a timestamp from real life, not an in-game timestamp. 
        /// </summary>
        [XmlIgnore]
        [NoSerialize]
        public DateTime CreatedAt;

        /// <summary>
        /// Type of order.
        /// </summary>
        [XmlAttribute("Type")]
        [Serialize]
        public MarketOrderType Type;

        /// <summary>
        /// Type of item that is being bought or sold.
        /// </summary>
        [XmlIgnore]
        [NoSerialize]
        public MyDefinitionId Item;

        /// <summary>
        /// Requested price per item.
        /// <list type="table">
        ///     <listheader>
        ///         <term>Type</term>
        ///         <description>Behavior</description>
        ///     </listheader>
        ///     <item>
        ///         <term>Buy</term>
        ///         <description>The maximum amount of money to pay per item.</description>
        ///     </item>
        ///     <item>
        ///         <term>Sell</term>
        ///         <description>The minimum amount of money to receive per item.</description>
        ///     </item>
        /// </list>
        /// </summary>
        [XmlAttribute("DPrice")]
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint DesiredPricePerItem;

        /// <summary>
        /// The number of items in the initial order. 
        /// </summary>
        [XmlAttribute("OItems")]
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint DesiredItemAmount;

        /// <summary>
        /// The number of items in the order that have not yet been fulfilled.
        /// </summary>
        [XmlAttribute("RItems")]
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint RemainingItemAmount;

        /// <summary>
        /// How many <see cref="Item"/>-s are currently stored by the market order.
        /// <list type="table">
        ///     <listheader>
        ///         <term>Type</term>
        ///         <description>Behavior</description>
        ///     </listheader>
        ///     <item>
        ///         <term>Buy</term>
        ///         <description>Increases from zero up towards <see cref="DesiredItemAmount"/>.</description>
        ///     </item>
        ///     <item>
        ///         <term>Sell</term>
        ///         <description>Decreases from <see cref="DesiredItemAmount"/> down towards zero.</description>
        ///     </item>
        /// </list>
        /// </summary>
        [XmlAttribute("SItems")]
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint StoredItemAmount;

        /// <summary>
        /// How much money is currently stored by the market order.
        /// <list type="table">
        ///     <listheader>
        ///         <term>Type</term>
        ///         <description>Behavior</description>
        ///     </listheader>
        ///     <item>
        ///         <term>Buy</term>
        ///         <description>Decreases from <see cref="DesiredPricePerItem"/> * <see cref="DesiredItemAmount"/> down towards zero.</description>
        ///     </item>
        ///     <item>
        ///         <term>Sell</term>
        ///         <description>Increases from zero up towards <see cref="DesiredPricePerItem"/> * <see cref="DesiredItemAmount"/>.</description>
        ///     </item>
        /// </list>
        /// </summary>
        [XmlAttribute("SMoney")]
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint StoredMoneyAmount;

        public override string ToString() => $"{Type} {Item} @ {DesiredPricePerItem} each";

        #region Serialization

        [XmlAttribute("Id")]
        [Serialize]
        public ulong LocalIdForSerialization
        {
            get => LocalId;
            set => LocalId = value;
        }

        private const long CreatedAtSerializationOffset = 639028224000000000L;
        private const long CreatedAtSerializationPrecision = TimeSpan.TicksPerMinute;

        [XmlAttribute("CreatedAt")]
        [Serialize(MyPrimitiveFlags.VariantSigned)]
        public long CreatedAtForSerialization
        {
            get => (CreatedAt.ToUniversalTime().Ticks - CreatedAtSerializationOffset) / CreatedAtSerializationPrecision;
            set => CreatedAt = new DateTime((value * CreatedAtSerializationPrecision) + CreatedAtSerializationOffset, DateTimeKind.Utc);
        }

        [XmlAttribute("ItemType")]
        [NoSerialize]
        public string ItemTypeForXml
        {
            get => Item.TypeId.ToString();
            set => Item = new MyDefinitionId(value == null ? MyObjectBuilderType.Invalid : MyObjectBuilderType.Parse(value), Item.SubtypeId);
        }

        [XmlAttribute("ItemSubtype")]
        [NoSerialize]
        public string ItemSubtypeForXml
        {
            get => Item.SubtypeName;
            set => Item = new MyDefinitionId(Item.TypeId, value);
        }

        [Serialize]
        [XmlIgnore]
        private TypeId ItemTypeForRpc
        {
            get => Item.TypeId;
            set => Item = new MyDefinitionId(value, Item.SubtypeId);
        }

        [Serialize]
        [XmlIgnore]
        private MyStringHash ItemSubtypeForRpc
        {
            get => Item.SubtypeId;
            set => Item = new MyDefinitionId(Item.TypeId, value);
        }

        #endregion
    }

    public struct RemoteMarketOrder
    {
        public EquiMarketStorageComponent Storage;

        public LocalMarketOrder Order;
    }

    public static class MarketOrdersExt
    {
        /// <summary>
        /// Gets the amount of resources that can be collected from an order.
        /// </summary>
        /// <param name="order">market order instance</param>
        /// <param name="itemsToCollect">number of items that can be collected from StoredItemAmount</param>
        /// <param name="moneyToCollect">amount of money that can be collected from StoredMoneyAmount</param>
        /// <returns>true if itemsToCollect or moneyToCollect is non-zero</returns>
        public static bool HasCollectableResources(this in LocalMarketOrder order, out uint itemsToCollect, out uint moneyToCollect)
        {
            switch (order.Type)
            {
                case MarketOrderType.Buy:
                    itemsToCollect = order.StoredItemAmount;
                    moneyToCollect = AmountToCollect(order.StoredMoneyAmount, order.RemainingItemAmount * order.DesiredPricePerItem);
                    break;
                case MarketOrderType.Sell:
                    itemsToCollect = AmountToCollect(order.StoredItemAmount, order.RemainingItemAmount);
                    moneyToCollect = order.StoredMoneyAmount;
                    break;
                case MarketOrderType.CancelledBuy:
                case MarketOrderType.CancelledSell:
                    itemsToCollect = order.StoredItemAmount;
                    moneyToCollect = order.StoredMoneyAmount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return itemsToCollect != 0 || moneyToCollect != 0;

            uint AmountToCollect(uint available, uint downTo) => available >= downTo ? checked(available - downTo) : 0u;
        }
    }
}