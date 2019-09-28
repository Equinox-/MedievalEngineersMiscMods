using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Definitions
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzItemArmorComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public struct MrzDamageReductionEntry
        {
            [XmlAttribute("Type")]
            public string DamageType;

            [XmlAttribute]
            public float Percentage;
        }

        [XmlElement("DamageReduction")]
        public List<MrzDamageReductionEntry> Entries;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_MrzItemArmorComponentDefinition))]
    public class MrzItemArmorComponentDefinition : MyEntityComponentDefinition
    {
        public Dictionary<MyStringHash, float> DamageReduction = new Dictionary<MyStringHash, float>();

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);

            var ob = (MyObjectBuilder_MrzItemArmorComponentDefinition)builder;

            if (ob.Entries != null && ob.Entries.Count > 0)
            {
                foreach (var entry in ob.Entries)
                    DamageReduction[MyStringHash.GetOrCompute(entry.DamageType)] = entry.Percentage;
            }
        }
    }
}
