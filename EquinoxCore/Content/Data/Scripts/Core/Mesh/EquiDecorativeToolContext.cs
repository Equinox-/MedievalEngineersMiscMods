using System;
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
        public static readonly MyStringId SnapDivisions = MyStringId.GetOrCompute("SnapDivisions");
        public static readonly MyStringId MeshSnapping = MyStringId.GetOrCompute("MeshSnapping");

        public static readonly MyStringId LineCatenaryFactor = MyStringId.GetOrCompute("LineCatenaryFactor");
        public static readonly MyStringId LineWidthA = MyStringId.GetOrCompute("LineWidthA");
        public static readonly MyStringId LineWidthB = MyStringId.GetOrCompute("LineWidthB");

        public static readonly MyStringId SurfaceUvBias = MyStringId.GetOrCompute("SurfaceUvBias");
        public static readonly MyStringId SurfaceUvProjection = MyStringId.GetOrCompute("SurfaceUvProjection");
        public static readonly MyStringId SurfaceUvScale = MyStringId.GetOrCompute("SurfaceUvScale");

        public static readonly MyStringId DecalRotationDeg = MyStringId.GetOrCompute("DecalRotationDeg");
        public static readonly MyStringId DecalHeight = MyStringId.GetOrCompute("DecalHeight");

        public static readonly MyStringId ModelScale = MyStringId.GetOrCompute("ModelScale");

        public static readonly MyStringId MaterialDef = MyStringId.GetOrCompute("MaterialDef");

        private EquiDecorativeToolBaseDefinition _definition;

        private static readonly Vector3 HsvUiScale = new Vector3(360, 100, 100);

        public override void Init(object[] contextParams)
        {
            _definition = (EquiDecorativeToolBaseDefinition)contextParams[0];
            m_dataSources.Clear();
            if (_definition.AllowRecoloring)
                m_dataSources.Add(Color, new Vector3BoundedArrayDataSource(
                    new Vector3(0, -100, -100),
                    Vector3.Zero,
                    new Vector3(360, 100, 100),
                    () => (DecorativeToolSettings.HsvShift ?? default) * HsvUiScale,
                    val => DecorativeToolSettings.HsvShift = val == default ? null : (Vector3?)(val / HsvUiScale)));

            m_dataSources.Add(SnapDivisions, new SimpleBoundedDataSource<float>(
                1,
                16,
                16,
                () => DecorativeToolSettings.SnapDivisions,
                val => DecorativeToolSettings.SnapDivisions = (int)Math.Round(val)
            ));

            m_dataSources.Add(MeshSnapping, new EnumDataSource<DecorativeToolSettings.MeshSnappingType>(
                () => DecorativeToolSettings.MeshSnapping,
                val => DecorativeToolSettings.MeshSnapping = val
            ));

            switch (_definition)
            {
                case EquiDecorativeLineToolDefinition line:
                    m_dataSources.Add(LineCatenaryFactor, new SimpleBoundedDataSource<float>(
                        0,
                        0,
                        100,
                        () => DecorativeToolSettings.LineCatenaryFactor * 100,
                        val => DecorativeToolSettings.LineCatenaryFactor = val / 100));
                    m_dataSources.Add(LineWidthA, new SimpleBoundedDataSource<float>(
                        line.WidthRange.Min,
                        line.DefaultWidth,
                        line.WidthRange.Max,
                        () => DecorativeToolSettings.LineWidthA,
                        val => DecorativeToolSettings.LineWidthA = val));
                    m_dataSources.Add(LineWidthB, new SimpleBoundedDataSource<float>(
                        line.WidthRange.Min,
                        line.DefaultWidth,
                        line.WidthRange.Max,
                        () => DecorativeToolSettings.LineWidthB,
                        val => DecorativeToolSettings.LineWidthB = val));

                    if (line.SortedMaterials.Count > 1)
                        m_dataSources.Add(MaterialDef, new DecorativeMaterialsDataSource<EquiDecorativeLineToolDefinition.LineMaterialDef>(
                            line.SortedMaterials,
                            () => DecorativeToolSettings.LineMaterialIndex,
                            val => DecorativeToolSettings.LineMaterialIndex = val));
                    break;
                case EquiDecorativeSurfaceToolDefinition surf:
                    m_dataSources.Add(SurfaceUvBias, new EnumDataSource<UvBiasMode>(
                        () => DecorativeToolSettings.UvBias,
                        val => DecorativeToolSettings.UvBias = val,
                        true));
                    m_dataSources.Add(SurfaceUvProjection, new EnumDataSource<UvProjectionMode>(
                        () => DecorativeToolSettings.UvProjection,
                        val => DecorativeToolSettings.UvProjection = val,
                        true));
                    m_dataSources.Add(SurfaceUvScale, new SimpleBoundedDataSource<float>(
                        surf.TextureScale.Min,
                        surf.TextureScale.Clamp(1),
                        surf.TextureScale.Max,
                        () => DecorativeToolSettings.UvScale,
                        val => DecorativeToolSettings.UvScale = val));
                    if (surf.SortedMaterials.Count > 1)
                        m_dataSources.Add(MaterialDef, new DecorativeMaterialsDataSource<EquiDecorativeSurfaceToolDefinition.SurfaceMaterialDef>(
                            surf.SortedMaterials,
                            () => DecorativeToolSettings.SurfaceMaterialIndex,
                            val => DecorativeToolSettings.SurfaceMaterialIndex = val));
                    break;
                case EquiDecorativeDecalToolDefinition decal:
                    m_dataSources.Add(DecalRotationDeg, new SimpleBoundedDataSource<float>(
                        0,
                        0,
                        360,
                        () => DecorativeToolSettings.DecalRotationDeg,
                        val => DecorativeToolSettings.DecalRotationDeg = val));
                    m_dataSources.Add(DecalHeight, new SimpleBoundedDataSource<float>(
                        EquiDecorativeDecalTool.MinDecalHeight,
                        0.125f,
                        EquiDecorativeDecalTool.MaxDecalHeight,
                        () => DecorativeToolSettings.DecalHeight,
                        val => DecorativeToolSettings.DecalHeight = val));
                    if (decal.SortedMaterials.Count > 1)
                        m_dataSources.Add(MaterialDef, new DecorativeMaterialsDataSource<EquiDecorativeDecalToolDefinition.DecalDef>(
                            decal.SortedMaterials,
                            () => DecorativeToolSettings.DecalIndex,
                            val => DecorativeToolSettings.DecalIndex = val));
                    break;
                case EquiDecorativeModelToolDefinition model:
                    var modelScale = model.ScaleRange;
                    m_dataSources.Add(ModelScale, new SimpleBoundedDataSource<float>(
                        modelScale.Min,
                        1,
                        modelScale.Max,
                        () => DecorativeToolSettings.ModelScale,
                        val => DecorativeToolSettings.ModelScale = val));
                    if (model.SortedMaterials.Count > 1)
                        m_dataSources.Add(MaterialDef, new DecorativeMaterialsDataSource<EquiDecorativeModelToolDefinition.ModelDef>(
                            model.SortedMaterials,
                            () => DecorativeToolSettings.ModelIndex,
                            val => DecorativeToolSettings.ModelIndex = val));
                    break;
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