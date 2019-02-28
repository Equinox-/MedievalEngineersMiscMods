using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Logging;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Stats
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiVoxelEffectComponentDefinition), null)]
    public class EquiVoxelEffectComponentDefinition : MyEntityComponentDefinition
    {
        public string SourceBone { get; private set; }

        public IReadOnlyDictionary<MyDefinitionId, IReadOnlyCollection<MyDefinitionId>> Effects { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var b = (MyObjectBuilder_EquiVoxelEffectComponentDefinition) builder;

            SourceBone = b.SourceBone;
            var tmp = new Dictionary<MyDefinitionId, IReadOnlyCollection<MyDefinitionId>>();
            if (b.Effects != null)
                foreach (var e in b.Effects)
                {
                    IReadOnlyCollection<MyDefinitionId> eft;
                    if (!tmp.TryGetValue(e.Material, out eft))
                        tmp.Add(e.Material, eft = new HashSet<MyDefinitionId>());
                    ((ICollection<MyDefinitionId>) eft).Add(e.Effect);
                }

            if (tmp.Count == 0)
                MyDefinitionErrors.Add(Package, $"Voxel effect component {Id} has no effects", LogSeverity.Error);

            Effects = tmp;
        }
    }

    [XmlSerializerAssembly("VRage.Game.XmlSerializers")]
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_EquiVoxelEffectComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement]
        public string SourceBone;

        [XmlElement("Effect")]
        public ObEffect[] Effects;

        public class ObEffect
        {
            [DefaultValue("VoxelMaterialDefinition")]
            [XmlAttribute]
            public string MaterialType;

            [XmlAttribute]
            public string MaterialSubtype;

            [XmlAttribute]
            public string EffectType;

            [XmlAttribute]
            public string EffectSubtype;

            [XmlIgnore]
            public SerializableDefinitionId Material =>
                new SerializableDefinitionId {TypeIdString = MaterialType ?? "VoxelMaterialDefinition", SubtypeId = MaterialSubtype};

            [XmlIgnore]
            public SerializableDefinitionId Effect => new SerializableDefinitionId {TypeIdString = EffectType, SubtypeId = EffectSubtype};
        }
    }
}