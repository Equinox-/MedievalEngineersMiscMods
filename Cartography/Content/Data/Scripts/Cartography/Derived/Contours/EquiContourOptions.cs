using System.Xml.Serialization;
using ObjectBuilders.Definitions.GUI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Derived.Contours
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiContourOptions))]
    public class EquiContourOptions : MyDefinitionBase
    {
        public int MajorContourEvery { get; private set; }
        public float ContourInterval { get; private set; }
        public Color MajorContourColor { get; private set; }
        public Color MinorContourColor { get; private set; }
        public Color? HighlightContourColor { get; private set; }

        public float OverlayMajorRenderDistance { get; private set; }
        public float OverlayMinorRenderDistance { get; private set; }
        public float OverlayMajorDepthTestDistanceSq { get; private set; }
        public float OverlayMinorDepthTestDistanceSq { get; private set; }
        public bool IsOverlay => OverlayMajorRenderDistance > 0 || OverlayMinorRenderDistance > 0;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiContourOptions)builder;
            MajorContourEvery = ob.MajorContourEvery ?? 5;
            ContourInterval = ob.ContourInterval ?? 25;
            MajorContourColor = ob.MajorContourColor ?? new ColorDefinitionRGBA(0, 0, 0, 255);
            MinorContourColor = ob.MinorContourColor ?? new ColorDefinitionRGBA(0, 0, 0, 200);
            HighlightContourColor = ob.HighlightContourColor;

            OverlayMajorRenderDistance = ob.OverlayMajorRenderDistance ?? 0;
            OverlayMinorRenderDistance = ob.OverlayMinorRenderDistance ?? OverlayMajorRenderDistance;
            OverlayMajorDepthTestDistanceSq = ob.OverlayMajorDepthTestDistance ?? float.PositiveInfinity;
            OverlayMinorDepthTestDistanceSq = ob.OverlayMinorDepthTestDistance ?? OverlayMajorDepthTestDistanceSq;

            OverlayMajorDepthTestDistanceSq *= OverlayMajorDepthTestDistanceSq;
            OverlayMinorDepthTestDistanceSq *= OverlayMinorDepthTestDistanceSq;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiContourOptions : MyObjectBuilder_DefinitionBase
    {
        public int? MajorContourEvery;
        public float? ContourInterval;
        public ColorDefinitionRGBA? MajorContourColor;
        public ColorDefinitionRGBA? MinorContourColor;
        public ColorDefinitionRGBA? HighlightContourColor;

        public float? OverlayMajorRenderDistance;
        public float? OverlayMinorRenderDistance;
        public float? OverlayMajorDepthTestDistance;
        public float? OverlayMinorDepthTestDistance;
    }
}