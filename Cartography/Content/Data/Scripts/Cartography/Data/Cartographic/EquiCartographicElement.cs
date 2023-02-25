using System.Xml.Serialization;
using VRage.Serialization;

namespace Equinox76561198048419394.Cartography.Data.Cartographic
{
    public class EquiCartographicElement
    {
        public readonly ulong Id;
        public string Name;

        public EquiCartographicElement(ulong id) => Id = id;

        protected void DeserializeFrom(MyObjectBuilder_CartographicElement ob)
        {
            Name = ob.Name;
        }

        protected void SerializeTo(MyObjectBuilder_CartographicElement ob)
        {
            ob.Id = Id;
            ob.Name = Name;
        }
    }

    public class MyObjectBuilder_CartographicElement
    {
        [XmlAttribute]
        public ulong Id;

        [XmlElement]
        [Nullable]
        public string Name;
    }
}