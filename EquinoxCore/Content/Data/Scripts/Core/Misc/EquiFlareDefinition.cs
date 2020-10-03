using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Misc
{
    // Matches SE's FlareDefinition
    [MyDefinitionType(typeof(MyObjectBuilder_EquiFlareDefinition))]
    public class EquiFlareDefinition : MyDefinitionBase
    {
        private static readonly MySubGlare.KeyPoint[] DefaultOcclusionCurve =
        {
            new MySubGlare.KeyPoint {Occlusion = 0, Intensity = 1},
            new MySubGlare.KeyPoint {Occlusion = .35f, Intensity = 0}
        };

        public float Intensity { get; private set; }

        public float MaxDistance { get; private set; }
        public Vector2 Size { get; private set; }
        public MySubGlare[] Glares { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiFlareDefinition) def;
            Intensity = ob.Intensity ?? 1;
            Size = ob.Size ?? Vector2.One;
            MaxDistance = ob.MaxDistance ?? 100f;
            Glares = new MySubGlare[ob.SubGlares?.Count ?? 0];
            if (ob.SubGlares == null) return;
            for (var i = 0; i < ob.SubGlares.Count; i++)
            {
                var builder = ob.SubGlares[i];
                var glare = new MySubGlare
                {
                    Material = MyStringId.GetOrCompute(builder.Material),
                    Type = builder.Type ?? SubGlareType.Oriented,
                    Color = builder.Color.HasValue ? ((Vector4) builder.Color.Value).ToLinearRGB() : Vector4.One,
                    FixedSize = builder.FixedSize ?? false,
                    Size = builder.Size ?? new Vector2(.25f, .25f),
                    ScreenIntensityMultiplierCenter = builder.ScreenIntensityMultiplierCenter ?? 1,
                    ScreenIntensityMultiplierEdge = builder.ScreenIntensityMultiplierEdge ?? 1,
                    ScreenCenterDistance = builder.ScreenCenterDistance ?? Vector2.Zero
                };
                if (builder.OcclusionToIntensityCurve != null && builder.OcclusionToIntensityCurve.Length > 0)
                    glare.OcclusionToIntensityCurve = builder.OcclusionToIntensityCurve
                        .Select(stop => new MySubGlare.KeyPoint {Intensity = stop.Intensity, Occlusion = stop.Occlusion}).ToArray();
                else
                    glare.OcclusionToIntensityCurve = DefaultOcclusionCurve;

                Glares[i] = glare;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiFlareDefinition : MyObjectBuilder_DefinitionBase
    {
        public float? Intensity;

        public SerializableVector2? Size;

        public float? MaxDistance;

        public struct GlareBuilder
        {
            public string Material;
            public SubGlareType? Type;
            public SerializableVector4? Color;
            public bool? FixedSize;
            public SerializableVector2? Size;
            public float? ScreenIntensityMultiplierCenter;
            public float? ScreenIntensityMultiplierEdge;
            public SerializableVector2? ScreenCenterDistance;

            [XmlArrayItem("Stop")]
            public GradientStop[] OcclusionToIntensityCurve;

            public struct GradientStop
            {
                [XmlAttribute]
                public float Occlusion;

                [XmlAttribute]
                public float Intensity;
            }
        }

        [XmlElement("SubGlare")]
        public List<GlareBuilder> SubGlares;
    }
}