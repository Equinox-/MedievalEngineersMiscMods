using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Collections;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Library.Collections;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Misc
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiCurrencySystemDefinition))]
    public class EquiCurrencySystemDefinition : MyVisualDefinitionBase
    {
        private readonly Dictionary<MyDefinitionId, CurrencyItem> _currencyItems = new Dictionary<MyDefinitionId, CurrencyItem>(MyDefinitionId.Comparer);
        private EqReadOnlySpan<DynamicLabel> _dynamicLabels;

        /// <summary>
        /// All the items involved in this currency system.
        /// </summary>
        public DictionaryReader<MyDefinitionId, CurrencyItem> CurrencyItems => _currencyItems;

        /// <summary>
        /// List of all currency items, ordered by ascending value.
        /// </summary>
        public ListReader<CurrencyItem> CurrencyItemsAscendingValue { get; private set; }

        /// <summary>
        /// List of all currency items, ordered by descending value.
        /// </summary>
        public ListReader<CurrencyItem> CurrencyItemsDescendingValue { get; private set; }

        /// <summary>
        /// Priority of this currency system, the highest priority currency system will be used.
        /// </summary>
        public int Priority { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiCurrencySystemDefinition)def;
            Priority = ob.Priority ?? 0;

            if (ob.Items != null)
                foreach (var item in ob.Items)
                {
                    var parsed = new CurrencyItem(item);
                    if (parsed.Item == null)
                    {
                        Log.Warning($"Currency system {def.Id} references unknown item {item.Type}/{item.Subtype}.");
                        continue;
                    }

                    if (parsed.Value == 0)
                    {
                        Log.Warning($"Currency system {def.Id}, item {item.Type}/{item.Subtype}, must have a non-zero value.");
                        continue;
                    }

                    // Replace existing item if it exists.
                    _currencyItems[parsed.Item.Id] = parsed;
                }

            CurrencyItemsAscendingValue = CurrencyItems.Values.OrderBy(x => x.Value).ToList();
            CurrencyItemsDescendingValue = CurrencyItems.Values.OrderByDescending(x => x.Value).ToList();
            _dynamicLabels = DynamicLabel.Of(ob.DynamicLabels);
        }

        /// <summary>
        /// Formats an amount of this currency as a string.
        /// </summary>
        /// <returns>the amount of currency, formatted as a string</returns>
        public string Format(ulong amount)
        {
            ref readonly var labelDef = ref DynamicLabel.Access(in _dynamicLabels, amount, out var okay);
            return okay ? labelDef.Format(amount) : amount.ToString();
        }

        #region Items to currency

        /// <summary>
        /// Takes items equivalent to the amount of currency from the given inventory.
        /// </summary>
        /// <param name="inventory">inventory to take items from</param>
        /// <param name="currencyAmount">amount of currency to take</param>
        /// <param name="onlyIfEnough">only take items if the given amount of currency can be taken</param>
        /// <param name="giveChange">give whatever change is possible back to the player</param>
        /// <returns>the amount of currency taken from the inventory</returns>
        public ulong TakeCurrency(MyInventoryBase inventory, ulong currencyAmount, bool onlyIfEnough, bool giveChange = false)
        {
            var info = new InventoryToCurrencyInfo(this, inventory);
            using (PoolManager.GetArray<ulong>(info.Count, out var takeCount, true))
            {
                var availableAmount = CurrencyToItemAmounts(ref info, currencyAmount, true, takeCount);
                if (availableAmount < currencyAmount && onlyIfEnough)
                    return 0;

                var takenAmount = 0ul;
                var ix = 0;
                for (; ix < info.Count; ix++)
                {
                    var take = takeCount[ix];
                    if (take == 0) continue;
                    if (take >= int.MaxValue) break;
                    if (!inventory.RemoveItems(info.Items[ix].Item.Id, (int)take)) break;
                    takenAmount += info.Value(ix) * take;
                }

                if (takenAmount >= currencyAmount || !onlyIfEnough)
                {
                    var changeDesired = takenAmount - currencyAmount;
                    if (!giveChange || changeDesired == 0)
                        return takenAmount;
                    var changeGiven = GiveCurrency(inventory, changeDesired, onlyIfFits: false, roundUp: false);
                    takenAmount -= changeGiven;
                    return takenAmount;
                }

                // Refund the items, not enough was taken.
                while (ix > 0)
                {
                    --ix;
                    var taken = takeCount[ix];
                    if (taken == 0) continue;
                    inventory.AddItems(info.Items[ix].Item.Id, (int)taken);
                }

                return 0;
            }
        }

        private readonly struct InventoryToCurrencyInfo : ICurrencyInfo
        {
            internal readonly ListReader<CurrencyItem> Items;
            private readonly MyInventoryBase _inventory;

            internal InventoryToCurrencyInfo(EquiCurrencySystemDefinition def, MyInventoryBase inventory)
            {
                Items = def.CurrencyItemsDescendingValue;
                _inventory = inventory;
            }

            public int Count => Items.Count;
            public ulong Value(int ix) => Items[ix].Value;
            public ulong Limit(int ix) => (ulong)_inventory.GetItemAmount(Items[ix].Item.Id);
        }

        #endregion

        #region Currency to items

        /// <summary>
        /// Gives items equivalent to the amount of currency to the given inventory.
        /// </summary>
        /// <param name="inventory">inventory to give items to</param>
        /// <param name="currencyAmount">amount of currency to give</param>
        /// <param name="onlyIfFits">only give items if the rounded amount of currency can fit in the inventory</param>
        /// <param name="roundUp">round the given currency amount up if true, round down if false</param>
        /// <returns>the amount of currency give to the inventory</returns>
        public ulong GiveCurrency(MyInventoryBase inventory, ulong currencyAmount, bool onlyIfFits, bool roundUp = false)
        {
            var info = new CurrencyToInventoryInfo(this, inventory);
            using (PoolManager.GetArray<ulong>(info.Count, out var giveCount, true))
            {
                var availableAmount = CurrencyToItemAmounts(ref info, currencyAmount, roundUp, giveCount);
                var givenAmount = 0ul;
                var ix = 0;
                for (; ix < info.Count; ix++)
                {
                    var give = giveCount[ix];
                    if (give == 0) continue;
                    if (give >= int.MaxValue) break;
                    if (!inventory.AddItems(info.Items[ix].Item.Id, (int)give)) break;
                    givenAmount += info.Value(ix) * give;
                }

                if (givenAmount >= availableAmount || !onlyIfFits)
                    return givenAmount;

                // Re-take the items, not enough was taken.
                while (ix > 0)
                {
                    --ix;
                    var given = giveCount[ix];
                    if (given == 0) continue;
                    inventory.RemoveItems(info.Items[ix].Item.Id, (int)given);
                }

                return 0;
            }
        }

        private readonly struct CurrencyToInventoryInfo : ICurrencyInfo
        {
            public readonly ListReader<CurrencyItem> Items;
            private readonly MyInventoryBase _inventory;

            internal CurrencyToInventoryInfo(EquiCurrencySystemDefinition def, MyInventoryBase inventory)
            {
                Items = def.CurrencyItemsDescendingValue;
                _inventory = inventory;
            }

            public int Count => Items.Count;
            public ulong Value(int ix) => Items[ix].Value;

            public ulong Limit(int ix)
            {
                // When every slot is full limit based on the existing stacks.
                if (_inventory.ItemCount >= _inventory.MaxItemCount)
                    return (ulong)_inventory.ComputeAmountThatFits(Items[ix].Item.Id);
                // Otherwise just allow giving all items.
                return ulong.MaxValue;
            }
        }

        #endregion

        #region Currency Factoring

        internal static ulong CurrencyToItemAmounts<TCurrencyInfo>(ref TCurrencyInfo info, ulong targetAmount, bool roundUp, ulong[] counts)
            where TCurrencyInfo : ICurrencyInfo
        {
            var itemCount = info.Count;
            if (itemCount == 0 || targetAmount == 0) return 0;
            Array.Clear(counts, 0, itemCount);

            var actualAmount = 0ul;
            // First compute item counts to exceed the goal currency amount.
            var excessIx = 0;
            for (var i = 0; i < itemCount && actualAmount < targetAmount; i++)
            {
                var limit = info.Limit(i);
                if (limit <= 0) continue;
                var value = info.Value(i);
                var desiredCount = (targetAmount + (roundUp ? value - 1 : 0) - actualAmount) / value;
                var count = Math.Min(limit, desiredCount);
                if (count <= 0) continue;
                excessIx = i;
                actualAmount += count * value;
                counts[i] += count;
            }

            // Push excess value down into lower value items.
            for (var i = excessIx + 1; i < itemCount && actualAmount > targetAmount; i++)
            {
                var limit = info.Limit(i);
                if (limit <= 0) continue;
                var value = info.Value(i);
                var excessValue = info.Value(excessIx);
                if (excessValue > actualAmount) break;
                var neededAmount = targetAmount - (actualAmount - excessValue);
                var desiredCount = (neededAmount + value - 1) / value;
                if (desiredCount > limit) continue;
                counts[excessIx]--;
                actualAmount += desiredCount * value;
                actualAmount -= excessValue;
                counts[i] += desiredCount;
                excessIx = i;
            }

            return actualAmount;
        }

        /// <summary>
        /// Accessor for currency items and limits, in descending value.
        /// </summary>
        internal interface ICurrencyInfo
        {
            int Count { get; }
            ulong Value(int ix);
            ulong Limit(int ix);
        }

        #endregion

        #region Value-for-items

        /// <summary>
        /// Gets the total value of this currency within the inventory.
        /// </summary>
        public ulong TotalValue(MyInventoryBase inventory) => TotalValue(inventory.Items);

        /// <summary>
        /// Gets the total value of this currency within the given list of items.
        /// </summary>
        /// <param name="items">list of items</param>
        /// <returns></returns>
        public ulong TotalValue(ListReader<MyInventoryItem> items) =>
            TotalValueInternal<List<MyInventoryItem>.Enumerator, MyInventoryItem, ReadInventoryItem>(items.GetEnumerator());

        /// <summary>
        /// Gets the total value of this currency within the given list of items.
        /// </summary>
        /// <param name="items">mapping from item ID to item count</param>
        /// <returns></returns>
        public ulong TotalValue(DictionaryReader<MyDefinitionId, ulong> items) =>
            TotalValueInternal<FallbackEnumerator<KeyValuePair<MyDefinitionId, ulong>, Dictionary<MyDefinitionId, ulong>.Enumerator>,
                KeyValuePair<MyDefinitionId, ulong>, ReadItemKeyValueULong>(items.GetEnumerator());

        /// <summary>
        /// Gets the total value of this currency within the given list of items.
        /// </summary>
        /// <param name="items">mapping from item ID to item count</param>
        /// <returns></returns>
        public ulong TotalValue(DictionaryReader<MyDefinitionId, int> items) =>
            TotalValueInternal<FallbackEnumerator<KeyValuePair<MyDefinitionId, int>, Dictionary<MyDefinitionId, int>.Enumerator>,
                KeyValuePair<MyDefinitionId, int>, ReadItemKeyValueInt>(items.GetEnumerator());

        private ulong TotalValueInternal<TEnumerator, TItem, TReadItem>(TEnumerator enumerator)
            where TEnumerator : IEnumerator<TItem> where TReadItem : struct, IReadItem<TItem>
        {
            using (enumerator)
            {
                var reader = default(TReadItem);
                var value = 0UL;
                while (enumerator.MoveNext())
                {
                    reader.Read(enumerator.Current, out var id, out var count);
                    if (!_currencyItems.TryGetValue(id, out var item)) continue;
                    value += count * item.Value;
                }

                return value;
            }
        }

        private interface IReadItem<in TItem>
        {
            void Read(TItem item, out MyDefinitionId id, out ulong amount);
        }

        private struct ReadInventoryItem : IReadItem<MyInventoryItem>
        {
            public void Read(MyInventoryItem item, out MyDefinitionId id, out ulong amount)
            {
                id = item.DefinitionId;
                amount = item.Amount > 0 ? (ulong)item.Amount : 0ul;
            }
        }

        private struct ReadItemKeyValueInt : IReadItem<KeyValuePair<MyDefinitionId, int>>
        {
            public void Read(KeyValuePair<MyDefinitionId, int> item, out MyDefinitionId id, out ulong amount)
            {
                id = item.Key;
                amount = item.Value > 0 ? (ulong)item.Value : 0ul;
            }
        }

        private struct ReadItemKeyValueULong : IReadItem<KeyValuePair<MyDefinitionId, ulong>>
        {
            public void Read(KeyValuePair<MyDefinitionId, ulong> item, out MyDefinitionId id, out ulong amount)
            {
                id = item.Key;
                amount = item.Value;
            }
        }

        #endregion

        public sealed class CurrencyItem
        {
            public readonly MyInventoryItemDefinition Item;
            public readonly ulong Value;

            internal CurrencyItem(MyObjectBuilder_EquiCurrencySystemDefinition.CurrencyItem ob)
            {
                Item = MyDefinitionManager.Get<MyInventoryItemDefinition>(new MyDefinitionId(MyObjectBuilderType.Parse(ob.Type), ob.Subtype));
                Value = ob.Value;
            }
        }

        internal EquiDynamicIconDefinition GenerateDynamicIconDefinition(MyDefinitionId id)
        {
            var def = new EquiDynamicIconDefinition();
            def.Init(new MyObjectBuilder_EquiDynamicIconDefinition { Id = id }, MyModContext.BaseGame);
            var itemValue = _currencyItems[id].Value;
            def.OverrideForCurrency(itemValue, _dynamicLabels);
            return def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCurrencySystemDefinition : MyObjectBuilder_VisualDefinitionBase
    {
        /// <summary>
        /// Labeling system for the currency.
        /// This will automatically be applied to all items involved in the currency.
        /// </summary>
        [XmlElement("DynamicLabel")]
        public List<MyObjectBuilder_DynamicLabel> DynamicLabels;

        /// <summary>
        /// Priority of this currency system. The highest priority currency system in the definition manager will be used by default.
        /// </summary>
        [XmlElement]
        public int? Priority;

        /// <summary>
        /// Items that make up this currency system.
        /// </summary>
        [XmlElement("Item")]
        public List<CurrencyItem> Items;

        public class CurrencyItem
        {
            /// <summary>
            /// Item definition type.
            /// </summary>
            [XmlAttribute("Type")]
            public string Type;

            /// <summary>
            /// Item definition subtype.
            /// </summary>
            [XmlAttribute("Subtype")]
            public string Subtype;

            /// <summary>
            /// Value of a single item, in currency units.
            /// </summary>
            [XmlAttribute("Value")]
            public ulong Value;
        }
    }
}