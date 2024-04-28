using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Inventory
{
    public readonly struct ImmutableInventoryAction
    {
        public enum InventoryActionMode
        {
            GiveTakeItem,
            RepairDamageItem,
            GiveTakeLootTable
        }

        public readonly MyDefinitionId TargetId;

        public readonly int Amount;

        public readonly InventoryActionMode Mode;

        public ImmutableInventoryAction(MyDefinitionId targetId, int amount, InventoryActionMode mode)
        {
            TargetId = targetId;
            Amount = amount;
            Mode = mode;
        }

        public ImmutableInventoryAction Inverse()
        {
            return new ImmutableInventoryAction(TargetId, -Amount, Mode);
        }

        public InventoryActionBuilder ToBuilder()
        {
            var ob = new InventoryActionBuilder
            {
                Type = TargetId.TypeId.ToString(),
                Subtype = TargetId.SubtypeName,
                Amount = Math.Abs(Amount),
            };
            switch (Mode)
            {
                case InventoryActionMode.GiveTakeItem:
                    ob.Mode = Amount < 0
                        ? InventoryActionBuilder.MutableInventoryActionMode.TakeItem
                        : InventoryActionBuilder.MutableInventoryActionMode.GiveItem;
                    break;
                case InventoryActionMode.RepairDamageItem:
                    ob.Mode = Amount < 0
                        ? InventoryActionBuilder.MutableInventoryActionMode.DamageItem
                        : InventoryActionBuilder.MutableInventoryActionMode.RepairItem;
                    break;
                case InventoryActionMode.GiveTakeLootTable:
                    if (Amount < 0)
                        throw new ArgumentOutOfRangeException();
                    ob.Mode = InventoryActionBuilder.MutableInventoryActionMode.GiveLootTable;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return ob;
        }

        public override string ToString()
        {
            return $"InventoryAction[Mode={Mode}, Type={TargetId.TypeId}, Subtype={TargetId.SubtypeId}, Amount={Amount}]";
        }
    }

    public struct InventoryActionBuilder
    {
        /// <summary>
        /// When tag is not specified will control the matched inventory item type, or the loot table type for the loot table mode.
        /// </summary>
        [XmlAttribute("Type")]
        [Nullable]
        public string Type;

        /// <summary>
        /// When tag is not specified will control the matched inventory item subtype, or the loot table subtype for the loot table mode.
        /// </summary>
        [XmlAttribute("Subtype")]
        [Nullable]
        public string Subtype;

        /// <summary>
        /// When specified the targeted items will be any items matching the provided tag.
        /// </summary>
        [XmlAttribute("Tag")]
        [Nullable]
        public string Tag;

        [XmlAttribute]
        public int Amount;

        [XmlAttribute]
        public MutableInventoryActionMode Mode;

        public enum MutableInventoryActionMode
        {
            /// <summary>
            /// Give the specified amount of the matching items.
            /// </summary>
            GiveItem,

            /// <summary>
            /// Take the specified amount of the matching items.
            /// </summary>
            TakeItem,

            /// <summary>
            /// Cumulatively damages matching items by the specified amount.
            /// </summary>
            DamageItem,

            /// <summary>
            /// Cumulatively repairs matching items by the specified amount.
            /// </summary>
            RepairItem,

            /// <summary>
            /// Gives the specified amount of rolls on a loot table definition.
            /// </summary>
            GiveLootTable
        }

        public ImmutableInventoryAction ToImmutable()
        {
            var targetId = Tag != null
                ? new MyDefinitionId(typeof(MyObjectBuilder_ItemTagDefinition), Tag)
                : new MyDefinitionId(MyObjectBuilderType.Parse(Type), Subtype);
            if (Amount <= 0)
                throw new Exception($"Invalid inventory action with amount less than or equal to zero");
            switch (Mode)
            {
                case MutableInventoryActionMode.GiveItem:
                    return new ImmutableInventoryAction(targetId, Amount, ImmutableInventoryAction.InventoryActionMode.GiveTakeItem);
                case MutableInventoryActionMode.TakeItem:
                    return new ImmutableInventoryAction(targetId, -Amount, ImmutableInventoryAction.InventoryActionMode.GiveTakeItem);
                case MutableInventoryActionMode.RepairItem:
                    return new ImmutableInventoryAction(targetId, Amount, ImmutableInventoryAction.InventoryActionMode.RepairDamageItem);
                case MutableInventoryActionMode.DamageItem:
                    return new ImmutableInventoryAction(targetId, -Amount, ImmutableInventoryAction.InventoryActionMode.RepairDamageItem);
                case MutableInventoryActionMode.GiveLootTable:
                    return new ImmutableInventoryAction(targetId, Amount, ImmutableInventoryAction.InventoryActionMode.GiveTakeLootTable);
                default:
                    throw new Exception($"Failed to parse inventory action with mode {Mode}");
            }
        }
    }

    public static class InventoryActionExt
    {
        public static ListReader<ImmutableInventoryAction> ToImmutable(this InventoryActionBuilder[] items)
        {
            if (items == null || items.Length == 0)
                return ListReader<ImmutableInventoryAction>.Empty;
            var dest = new List<ImmutableInventoryAction>(items.Length);
            foreach (var item in items)
                if (item.Amount > 0)
                    dest.Add(item.ToImmutable());
            if (dest.Count == 0)
                return ListReader<ImmutableInventoryAction>.Empty;
            dest.TrimExcess();
            return dest;
        }

        public static ListReader<ImmutableInventoryAction> ToImmutable(this List<InventoryActionBuilder> items)
        {
            if (items == null || items.Count == 0)
                return ListReader<ImmutableInventoryAction>.Empty;
            var dest = new List<ImmutableInventoryAction>(items.Count);
            foreach (var item in items)
                if (item.Amount > 0)
                    dest.Add(item.ToImmutable());
            if (dest.Count == 0)
                return ListReader<ImmutableInventoryAction>.Empty;
            dest.TrimExcess();
            return dest;
        }
    }
}