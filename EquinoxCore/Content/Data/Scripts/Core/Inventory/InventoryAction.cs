
using System;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Inventory
{
    public struct ImmutableInventoryAction
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
        [XmlAttribute("Type")]
        [Nullable]
        public string Type;

        [XmlAttribute("Subtype")]
        [Nullable]
        public string Subtype;

        [XmlAttribute("Tag")]
        [Nullable]
        public string Tag;

        [XmlAttribute]
        public int Amount;

        [XmlAttribute]
        public MutableInventoryActionMode Mode;

        public enum MutableInventoryActionMode
        {
            GiveItem,
            TakeItem,
            DamageItem,
            RepairItem,
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
}