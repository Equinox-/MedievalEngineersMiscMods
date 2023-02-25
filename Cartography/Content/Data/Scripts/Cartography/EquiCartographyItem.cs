using System.Xml.Serialization;
using Equinox76561198048419394.Cartography.Data;
using Equinox76561198048419394.Cartography.Data.Cartographic;
using Equinox76561198048419394.Cartography.Data.Framework;
using Sandbox.Definitions.Inventory;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.ObjectBuilders.Inventory;
using VRage.Scene;

namespace Equinox76561198048419394.Cartography
{
    [MyInventoryItemType(typeof(MyObjectBuilder_EquiCartographyItem))]
    public class EquiCartographyItem : MyHandItem, IEquiExternalDataItem
    {
        public EquiCartographicDataId? DataId;

        public override MyObjectBuilder_InventoryItem Serialize()
        {
            var ob = (MyObjectBuilder_EquiCartographyItem)base.Serialize();
            ob.DataId = DataId?.HostEntity ?? 0;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_InventoryItem item)
        {
            base.Deserialize(item);
            var ob = (MyObjectBuilder_EquiCartographyItem)item;
            DataId = ob.DataId != 0 ? new EquiCartographicDataId(ob.DataId) : default(EquiCartographicDataId?);
        }

        protected override void Serialize(BitStream stream)
        {
            base.Serialize(stream);
            stream.WriteVariant(DataId?.HostEntity ?? 0);
        }

        protected override void Deserialize(BitStream stream)
        {
            base.Deserialize(stream);
            DataId = new EquiCartographicDataId(stream.ReadUInt64Variant());
        }

        public EntityId? DataHost => DataId?.HostEntity;

        public override MyInventoryItem Clone(int newAmount = -1)
        {
            var item = (EquiCartographyItem) base.Clone(newAmount);
            item.DataId = DataId;
            return item;
        }

        public override bool CanStack(MyDefinitionId newItem) => false;
        public override bool CanStack(MyInventoryItem other) => false;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCartographyItem : MyObjectBuilder_HandItem
    {
        public ulong DataId;
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiCartographyItemDefinition))]
    public class EquiCartographyItemDefinition : MyHandItemDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCartographyItemDefinition : MyObjectBuilder_HandItemDefinition
    {
    }
}