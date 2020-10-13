using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.Core.Debug
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiDebugDefinition))]
    public class EquiDebugDefinition : MyDefinitionBase
    {
        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiDebugDefinition) def;
            if (ob.Entries != null && ob.Entries.Count > 0)
                foreach (var e in ob.Entries)
                    DebugFlags.SetLevel(e.Prefix, e.Level);

            if (ob.FailFast.HasValue)
                DebugFlags.FailFast = ob.FailFast.Value;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDebugDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement("FailFast")]
        public bool? FailFast;
        
        [XmlElement("Entry")]
        public List<Entry> Entries;

        public struct Entry
        {
            [XmlAttribute]
            public string Prefix;

            [XmlAttribute]
            public DebugFlags.Level Level;
        }
    }
}