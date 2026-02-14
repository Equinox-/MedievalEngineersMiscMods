using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using VRage.Collections;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiMarketHistoryComponent))]
    [MyDefinitionRequired(typeof(EquiMarketHistoryComponentDefinition))]
    [MyDependency(typeof(EquiMarketStorageComponent), Critical = true)]
    public class EquiMarketHistoryComponent : MyEntityComponent
    {
        private readonly Dictionary<MyDefinitionId, MarketItemHistory> _items = new Dictionary<MyDefinitionId, MarketItemHistory>(MyDefinitionId.Comparer);

        public DictionaryReader<MyDefinitionId, MarketItemHistory> Items => _items;

        [Automatic]
        private readonly EquiMarketStorageComponent _marketStorageComponent = null;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _marketStorageComponent.OnOrderSolved += OnOrderSolved;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _marketStorageComponent.OnOrderSolved -= OnOrderSolved;
            base.OnBeforeRemovedFromContainer();
        }

        private void OnOrderSolved(
            EquiMarketStorageComponent seller, in LocalMarketOrder order, uint sellPricePerItem,
            EquiMarketStorageComponent buyer, in LocalMarketOrder _2, uint buyPricePerItem,
            uint count)
        {
            uint price;
            if (seller == _marketStorageComponent)
                price = sellPricePerItem;
            else if (buyer == _marketStorageComponent)
                price = buyPricePerItem;
            else
                return;

            if (TryGetHistory(order.Item, out var history, true))
                history.RecordOrderSolved(count, price);
        }

        public bool TryGetHistory(MyDefinitionId item, out MarketItemHistory history, bool createIfMissing = false)
        {
            if (_items.TryGetValue(item, out history))
                return true;
            if (!createIfMissing || !MyDefinitionManager.TryGet(item, out MyInventoryItemDefinition itemDef))
                return false;
            history = new MarketItemHistory(Definition, itemDef);
            _items.Add(item, history);
            return true;
        }

        public EquiMarketHistoryComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiMarketHistoryComponentDefinition)def;
        }

        #region Persistence

        public override bool IsSerialized
        {
            get
            {
                foreach (var item in _items.Values)
                    if (item.IsSerialized)
                        return true;
                return false;
            }
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiMarketHistoryComponent)builder;
            if (ob.Items == null) return;
            foreach (var item in ob.Items)
                if (MyDefinitionManager.TryGet(item.Item, out MyInventoryItemDefinition itemDef))
                {
                    var history = new MarketItemHistory(Definition, itemDef);
                    history.Deserialize(item);
                    _items[item.Item] = history;
                }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiMarketHistoryComponent)base.Serialize(copy);
            ob.Items = new List<MyObjectBuilder_EquiMarketHistoryComponent.MyObjectBuilder_MarketItemHistory>(_items.Count);
            foreach (var item in _items.Values)
                if (item.IsSerialized)
                    ob.Items.Add(item.Serialize());
            return ob;
        }

        #endregion
    }

    public class MarketItemHistory
    {
        public readonly MyInventoryItemDefinition Item;
        public TemporalHistoryReader<MarketItemHistoryEntry> History => _history;
        private readonly TemporalHistory<MarketItemHistoryEntry> _history;

        internal MarketItemHistory(
            EquiMarketHistoryComponentDefinition definition,
            MyInventoryItemDefinition item)
        {
            Item = item;
            _history = new TemporalHistory<MarketItemHistoryEntry>(definition.BucketCount, definition.BucketSize);
        }

        internal void RecordOrderSolved(uint volume, uint pricePerItem) => _history.AddInstant(DateTime.UtcNow, new MarketItemHistoryEntry
        {
            Volume = volume,
            MinPricePerItem = pricePerItem,
            MeanPricePerItem = pricePerItem,
            MaxPricePerItem = pricePerItem,
        });

        internal void Deserialize(MyObjectBuilder_EquiMarketHistoryComponent.MyObjectBuilder_MarketItemHistory item)
        {
            if (item.History != null) _history.Add(item.History);
        }

        internal bool IsSerialized => !_history.IsEmpty;

        internal MyObjectBuilder_EquiMarketHistoryComponent.MyObjectBuilder_MarketItemHistory Serialize() =>
            new MyObjectBuilder_EquiMarketHistoryComponent.MyObjectBuilder_MarketItemHistory
            {
                Item = Item.Id,
                History = _history.Serialize(),
            };
    }

    public struct MarketItemHistoryEntry : ITemporalHistoryBucket<MarketItemHistoryEntry>
    {
        [Serialize(MyPrimitiveFlags.Variant)]
        public uint Volume;

        [Serialize(MyPrimitiveFlags.Variant)]
        public uint MinPricePerItem;

        [Serialize(MyPrimitiveFlags.Variant)]
        public uint MeanPricePerItem;

        [Serialize(MyPrimitiveFlags.Variant)]
        public uint MaxPricePerItem;

        private const char Separator = ',';

        public bool IsEmpty => Volume == 0;

        public void MergeWith(in MarketItemHistoryEntry other) => MergeWithInternal(in other, other.Volume);

        public void MergeWith(in MarketItemHistoryEntry other, float fraction) => MergeWithInternal(in other, (uint)Math.Round(fraction * other.Volume));

        private void MergeWithInternal(in MarketItemHistoryEntry other, uint otherEffectiveVolume)
        {
            var totalVolume = Volume + otherEffectiveVolume;
            if (Volume == 0 || other.MinPricePerItem < MinPricePerItem) MinPricePerItem = other.MinPricePerItem;
            if (Volume == 0 || other.MaxPricePerItem > MaxPricePerItem) MaxPricePerItem = other.MaxPricePerItem;
            MeanPricePerItem = (uint)Math.Round((Volume * (double)MeanPricePerItem + otherEffectiveVolume * (double)other.MeanPricePerItem) / totalVolume);
            Volume = totalVolume;
        }

        private const string EmptySerialized = "0";

        public bool DeserializeFrom(string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return false;
            if (part == EmptySerialized)
            {
                Volume = 0;
                MinPricePerItem = MeanPricePerItem = MaxPricePerItem = 0;
                return true;
            }

            var tokens = part.Split(Separator);
            if (tokens.Length != 4) return false;
            return uint.TryParse(tokens[0], out Volume)
                   && uint.TryParse(tokens[1], out MinPricePerItem)
                   && uint.TryParse(tokens[2], out MeanPricePerItem)
                   && uint.TryParse(tokens[3], out MaxPricePerItem);
        }

        public void SerializeTo(StringBuilder sb)
        {
            if (IsEmpty)
            {
                sb.Append(EmptySerialized);
                return;
            }

            sb.Append(Volume).Append(Separator)
                .Append(MinPricePerItem).Append(Separator)
                .Append(MeanPricePerItem).Append(Separator)
                .Append(MaxPricePerItem);
        }

        public override string ToString() => $"Volume={Volume}, Min={MinPricePerItem}, Mean={MeanPricePerItem}, Max={MaxPricePerItem}";
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketHistoryComponent : MyObjectBuilder_EntityComponent
    {
        [XmlElement("Item")]
        public List<MyObjectBuilder_MarketItemHistory> Items;

        public class MyObjectBuilder_MarketItemHistory
        {
            [XmlElement]
            [Serialize]
            public SerializableDefinitionId Item;

            [XmlElement]
            [Serialize]
            public MyObjectBuilder_TemporalHistory<MarketItemHistoryEntry> History;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiMarketHistoryComponentDefinition))]
    public class EquiMarketHistoryComponentDefinition : MyEntityComponentDefinition
    {
        public TimeSpan BucketSize { get; private set; }
        public int BucketCount { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiMarketHistoryComponentDefinition)def;

            BucketSize = ob.BucketSize ?? TimeSpan.FromHours(6);
            BucketCount = ob.BucketCount ?? 40;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiMarketHistoryComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement]
        public TimeDefinition? BucketSize;

        [XmlElement]
        public int? BucketCount;
    }
}