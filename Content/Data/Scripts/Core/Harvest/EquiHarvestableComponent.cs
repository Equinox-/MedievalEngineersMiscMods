using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Contexts;
using System.Xml.Serialization;
using Sandbox.Game.EntityComponents;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Harvest
{
    [MyComponent(typeof(MyObjectBuilder_EquiHarvestableComponent))]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    [MyDefinitionRequired(typeof(EquiHarvestableComponentDefinition))]
    public class EquiHarvestableComponent : MyEntityComponent
    {
        private MyEntityStateComponent _stateComponent;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _stateComponent = Container.Get<MyEntityStateComponent>();
        }

        public override void OnRemovedFromScene()
        {
            _stateComponent = null;
            base.OnRemovedFromScene();
        }

        private EquiHarvestableComponentDefinition Definition { get; set; }

        public bool CanHarvest(MyInventoryItemDefinition item, out EquiHarvestableComponentDefinition.Data info)
        {
            return Definition.TryGetData(_stateComponent.CurrentState, item, out info);
        }

        public bool TryHarvest(MyInventoryItemDefinition item, out EquiHarvestableComponentDefinition.Data info)
        {
            return Definition.TryGetData(_stateComponent.CurrentState, item, out info) &&
                   (info.DestinationState == MyStringHash.NullOrEmpty || _stateComponent.TransitionTo(info.DestinationState));
        }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiHarvestableComponentDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiHarvestableComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiHarvestableComponentDefinition))]
    [MyDependency(typeof(MyLootTableDefinition))]
    public class EquiHarvestableComponentDefinition : MyEntityComponentDefinition
    {
        #region Lookup Keys

        public struct Data
        {
            public readonly MyLootTableDefinition LootTable;
            public readonly MyStringHash DestinationState;
            public readonly string ActionHint;
            public readonly MyStringHash ActionIcon;
            public readonly bool RequiresPermission;

            public Data(MyLootTableDefinition lt, MyStringHash ds, bool permission, string actionHint, MyStringHash actionIcon)
            {
                LootTable = lt;
                DestinationState = ds;
                RequiresPermission = permission;
                ActionHint = actionHint;
                ActionIcon = actionIcon;
            }
        }

        private struct TagKey : IEquatable<TagKey>
        {
            public readonly MyStringHash State;
            public readonly MyStringHash Tag;

            public TagKey(MyStringHash state, MyStringHash tag)
            {
                State = state;
                Tag = tag;
            }

            public bool Equals(TagKey other)
            {
                return State.Equals(other.State) && Tag.Equals(other.Tag);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is TagKey && Equals((TagKey) obj);
            }

            public override int GetHashCode()
            {
                return (State.GetHashCode() * 397) ^ Tag.GetHashCode();
            }
        }

        private struct DefinitionKey : IEquatable<DefinitionKey>
        {
            public readonly MyStringHash State;
            public readonly MyDefinitionId Id;

            public DefinitionKey(MyStringHash state, MyDefinitionId id)
            {
                State = state;
                Id = id;
            }

            public bool Equals(DefinitionKey other)
            {
                return State.Equals(other.State) && Id.Equals(other.Id);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is DefinitionKey && Equals((DefinitionKey) obj);
            }

            public override int GetHashCode()
            {
                return (State.GetHashCode() * 397) ^ Id.GetHashCode();
            }
        }

        #endregion

        private readonly Dictionary<TagKey, Data> _lootTableByTag = new Dictionary<TagKey, Data>();
        private readonly Dictionary<DefinitionKey, Data> _lootTableByItem = new Dictionary<DefinitionKey, Data>();

        public bool TryGetData(MyStringHash state, MyInventoryItemDefinition itemDef, out Data res)
        {
            if (_lootTableByItem.TryGetValue(new DefinitionKey(state, itemDef.Id), out res))
                return true;
            foreach (var tag in itemDef.Tags)
                if (_lootTableByTag.TryGetValue(new TagKey(state, tag), out res))
                    return true;
            return false;
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiHarvestableComponentDefinition) def;
            _lootTableByTag.Clear();
            _lootTableByItem.Clear();
            if (ob.Entries == null)
                return;
            foreach (var k in ob.Entries)
            {
                if (string.IsNullOrWhiteSpace(k.From))
                {
                    MyDefinitionErrors.Add(Package, $"{Id} has an entry with no from state", LogSeverity.Warning);
                    continue;
                }

                if (k.Harvesters == null || k.Harvesters.Length == 0)
                {
                    MyDefinitionErrors.Add(Package, $"{Id} has an entry with no harvesters", LogSeverity.Warning);
                    continue;
                }

                MyLootTableDefinition lootTable = null;
                if (k.LootTable.HasValue)
                {
                    lootTable = MyDefinitionManager.Get<MyLootTableDefinition>(k.LootTable.Value);
                    if (lootTable == null)
                    {
                        MyDefinitionErrors.Add(Package, $"{Id} has an entry from {k.From} referring to missing loot table {k.LootTable}",
                            LogSeverity.Warning);
                        continue;
                    }
                }

                foreach (var item in k.Harvesters)
                    if (!item.IsValid())
                        MyDefinitionErrors.Add(Package, $"{Id} has an entry with an invalid harvester", LogSeverity.Warning);


                var sourceState = MyStringHash.GetOrCompute(k.From);
                var destState = MyStringHash.GetOrCompute(k.To);
                var data = new Data(lootTable, destState, k.RequiresPermission, k.ActionHint ?? "Harvest",
                    MyStringHash.GetOrCompute(k.ActionIcon ?? "Pickup_Item"));

                foreach (var item in k.Harvesters)
                {
                    if (!item.IsValid())
                        continue;

                    if (!string.IsNullOrWhiteSpace(item.Tag))
                        _lootTableByTag[new TagKey(sourceState, MyStringHash.GetOrCompute(item.Tag))] = data;
                    else
                        _lootTableByItem[new DefinitionKey(sourceState, item)] = data;
                }
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiHarvestableComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class Entry
        {
            [XmlAttribute]
            public string From;

            [XmlAttribute]
            public string To;

            [XmlElement]
            public SerializableDefinitionId? LootTable;

            [XmlElement("Harvester")]
            public DefinitionTagId[] Harvesters;

            [XmlElement]
            public string ActionHint;

            [XmlElement]
            public string ActionIcon;

            [XmlElement]
            public bool RequiresPermission;
        }

        [XmlElement("Entry")]
        public Entry[] Entries;
    }
}