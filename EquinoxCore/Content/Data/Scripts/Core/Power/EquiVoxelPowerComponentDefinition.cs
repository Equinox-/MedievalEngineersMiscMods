using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using Medieval.ObjectBuilders.Definitions.Quests.Conditions;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Power
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiVoxelPowerComponentDefinition))]
    public class EquiVoxelPowerComponentDefinition : MyEntityComponentDefinition
    {
        private readonly List<MaterialRequirement> _materialsBacking;

        public EquiVoxelPowerComponentDefinition()
        {
            _materialsBacking = new List<MaterialRequirement>();
            Materials = new ListReader<MaterialRequirement>(_materialsBacking);
        }

        public ListReader<MaterialRequirement> Materials { get; }
        public float ScanRadius { get; private set; }
        public float ScanMargin { get; private set; }
        public bool PoweredWhenDisturbed { get; private set; }
        public int LevelOfDetail { get; private set; }
        public TimeSpan DisturbedTime { get; private set; }
        public VoxelPowerCountMode Mode { get; private set; }
        public bool DebugMode { get; private set; }
        
        public string Name { get; private set; }

        public QuestConditionCompositeOperator Operator { get; private set; }

        public struct MaterialRequirement
        {
            public readonly MyDefinitionId Material;
            public readonly float Amount;
            public readonly bool LessThan;

            public MaterialRequirement(MyObjectBuilder_EquiVoxelPowerComponentDefinition.MaterialRequirement mt)
            {
                Material = new SerializableDefinitionId {TypeIdString = mt.Type, SubtypeId = mt.Subtype};
                Amount = mt.Amount;
                LessThan = mt.LessThan;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiVoxelPowerComponentDefinition) def;

            _materialsBacking.Clear();
            if (ob.Materials != null)
            {
                _materialsBacking.Capacity = Math.Min(_materialsBacking.Capacity, ob.Materials.Count);
                _materialsBacking.AddRange(ob.Materials.Select(x => new MaterialRequirement(x)));
            }

            LevelOfDetail = ob.LevelOfDetail;
            ScanRadius = ob.ScanRadius;
            ScanMargin = ob.ScanMargin;
            Operator = ob.Operator;
            Mode = ob.Mode;
            DisturbedTime = ob.DisturbedTime;
            PoweredWhenDisturbed = ob.PoweredWhenDisturbed;
            DebugMode = ob.DebugMode;
            Name = ob.Name;
        }
    }

    public enum VoxelPowerCountMode
    {
        Volume,
        Surface
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelPowerComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [DefaultValue(QuestConditionCompositeOperator.OR)]
        public QuestConditionCompositeOperator Operator;

        public float ScanRadius;

        [DefaultValue(1)]
        public float ScanMargin;

        public VoxelPowerCountMode Mode;

        [DefaultValue(true)]
        public bool PoweredWhenDisturbed;

        [DefaultValue(1)]
        public int LevelOfDetail;

        [DefaultValue(false)]
        public bool DebugMode;

        public TimeDefinition DisturbedTime;

        public List<MaterialRequirement> Materials;

        public string Name;

        public struct MaterialRequirement
        {
            [XmlAttribute]
            public string Type;

            [XmlAttribute]
            public string Subtype;

            [XmlAttribute]
            public float Amount;

            [XmlAttribute]
            [DefaultValue(false)]
            public bool LessThan;
        }
    }
}