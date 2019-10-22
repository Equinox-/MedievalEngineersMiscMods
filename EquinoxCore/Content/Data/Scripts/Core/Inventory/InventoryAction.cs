
using System;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders.Definitions.Inventory;

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

        public override string ToString()
        {
            return $"InventoryAction[Mode={Mode}, Type={TargetId.TypeId}, Subtype={TargetId.SubtypeId}, Amount={Amount}]";
        }
    }

    public struct InventoryActionBuilder
    {
        [XmlAttribute("Type")]
        public string Type;

        [XmlAttribute("Subtype")]
        public string Subtype;

        [XmlAttribute("Tag")]
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