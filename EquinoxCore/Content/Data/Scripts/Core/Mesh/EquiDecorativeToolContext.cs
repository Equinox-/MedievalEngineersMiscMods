using System.Xml.Serialization;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyContextMenuContextType(typeof(MyObjectBuilder_EquiDecorativeToolContext))]
    public class EquiDecorativeToolContext : MyContextMenuContext
    {
        public static readonly MyStringId Color = MyStringId.GetOrCompute("Color");

        public static readonly MyStringId LineCatenaryFactor = MyStringId.GetOrCompute("LineCatenaryFactor");

        public static readonly MyStringId SurfaceUvBias = MyStringId.GetOrCompute("SurfaceUvBias");
        public static readonly MyStringId SurfaceUvProjection = MyStringId.GetOrCompute("SurfaceUvProjection");

        public static readonly MyStringId DecalRotationDeg = MyStringId.GetOrCompute("DecalRotationDeg");
        public static readonly MyStringId DecalHeight = MyStringId.GetOrCompute("DecalHeight");
        public static readonly MyStringId DecalDef = MyStringId.GetOrCompute("DecalDef");

        private EquiDecorativeToolBaseDefinition _definition;

        private static readonly Vector3 HsvUiScale = new Vector3(360, 100, 100);

        public override void Init(object[] contextParams)
        {
            _definition = (EquiDecorativeToolBaseDefinition)contextParams[0];
            m_dataSources.Clear();
            if (_definition.AllowRecoloring)
                m_dataSources.Add(Color, new Vector3BoundedArrayDataSource(
                    new Vector3(0, -100, -100),
                    (DecorativeToolSettings.HsvShift ?? default) * HsvUiScale,
                    new Vector3(360, 100, 100),
                    () => (DecorativeToolSettings.HsvShift ?? default) * HsvUiScale,
                    val => DecorativeToolSettings.HsvShift = val == default ? null : (Vector3?)(val / HsvUiScale)));

            if (_definition is EquiDecorativeLineToolDefinition line)
            {
                m_dataSources.Add(LineCatenaryFactor, new SimpleBoundedDataSource<float>(
                    0,
                    DecorativeToolSettings.LineCatenaryFactor * 100,
                    100,
                    () => DecorativeToolSettings.LineCatenaryFactor * 100,
                    val => DecorativeToolSettings.LineCatenaryFactor = val / 100));
            }

            if (_definition is EquiDecorativeSurfaceToolDefinition surf)
            {
                m_dataSources.Add(SurfaceUvBias, new EnumDataSource<UvBiasMode>(
                    () => DecorativeToolSettings.UvBias,
                    val => DecorativeToolSettings.UvBias = val,
                    true));
                m_dataSources.Add(SurfaceUvProjection, new EnumDataSource<UvProjectionMode>(
                    () => DecorativeToolSettings.UvProjection,
                    val => DecorativeToolSettings.UvProjection = val,
                    true));
            }

            if (_definition is EquiDecorativeDecalToolDefinition decal)
            {
                m_dataSources.Add(DecalRotationDeg, new SimpleBoundedDataSource<int>(
                    0,
                    DecorativeToolSettings.DecalRotationDeg,
                    360,
                    () => DecorativeToolSettings.DecalRotationDeg,
                    val => DecorativeToolSettings.DecalRotationDeg = val));
                m_dataSources.Add(DecalHeight, new SimpleBoundedDataSource<float>(
                    EquiDecorativeDecalTool.MinDecalHeight,
                    DecorativeToolSettings.DecalHeight,
                    EquiDecorativeDecalTool.MaxDecalHeight,
                    () => DecorativeToolSettings.DecalHeight,
                    val => DecorativeToolSettings.DecalHeight = val));
                m_dataSources.Add(DecalDef, new DecorativeDecalsDataSource(decal));
            }
        }

        public void ResetColor() => DecorativeToolSettings.HsvShift = null;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeToolContext : MyObjectBuilder_Base
    {
    }
}