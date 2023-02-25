using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Cartography.MapLayers;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Derived.Contours
{
    public sealed class EquiContoursMapLayer : EquiRasterizedMapLayer<ContourData>, ICustomMapLayer
    {
        private readonly EquiContourOptions _opts;

        public EquiContoursMapLayer(EquiContourOptions opts, EquiCustomMapLayerDefinition source = null)
        {
            _opts = opts;
            Source = source;
        }

        protected override ContourData GetArgs(out bool shouldRender)
        {
            var planet = Map.Planet;
            var areas = planet.Get<MyPlanetAreasComponent>();
            int scalingCount;
            var contourInterval = _opts.ContourInterval;
            switch (View.Zoom)
            {
                case MyPlanetMapZoomLevel.Kingdom:
                    scalingCount = areas.RegionCount;
                    break;
                case MyPlanetMapZoomLevel.Region:
                    scalingCount = areas.AreaCount;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (contourInterval <= 0)
            {
                shouldRender = false;
                return default;
            }

            var counts = View.Size;
            MyPlanetAreasComponent.UnpackAreaId(View[0, 0], out var face, out int minCellX, out var minCellY);
            MyPlanetAreasComponent.UnpackAreaId(View[counts.X - 1, counts.Y - 1], out _, out int maxCellX, out var maxCellY);

            var minTexCoord = (2 * new Vector2(minCellX, minCellY) - scalingCount) / scalingCount;
            var maxTexCoord = (2 * new Vector2(maxCellX + 1, maxCellY + 1) - scalingCount) / scalingCount;
            var rectangle = new RectangleF(minTexCoord, maxTexCoord - minTexCoord);
            var args = new ContourArgs(planet, face, rectangle, contourInterval);
            
            var contours = MySession.Static.Components.Get<EquiCartography>()?.GetOrComputeContoursAsync(args);
            shouldRender = contours != null;
            return contours;
        }

        protected override void Render(in ContourData contours, ref RenderContext ctx)
        {
            var contoursCopy = contours;
            var size = ctx.Size;
            Vector2I MapVertex(int v) => Vector2I.Round(contoursCopy.GetVertex(v) * size);
            foreach (var contour in contours.Lines)
            {
                var prev = MapVertex(contour.StartVertex);
                var major = _opts.MajorContourEvery != 0 && (contour.ContourId % _opts.MajorContourEvery) == 0;
                var color = major ? _opts.MajorContourColor : _opts.MinorContourColor;
                for (var v = contour.StartVertex + 1; v <= contour.EndVertex; v++)
                {
                    var curr = MapVertex(v);
                    ctx.DrawLine(prev, curr, color);
                    prev = curr;
                }
            }
        }

        public EquiCustomMapLayerDefinition Source { get; }
        public MyMapGridView BoundTo => View;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiContoursMapLayerDefinition))]
    public class EquiContoursMapLayerDefinition : EquiCustomMapLayerDefinition
    {
        private EquiContourOptions _kingdom;
        private EquiContourOptions _region;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiContoursMapLayerDefinition)def;

            EquiContourOptions Create(MyObjectBuilder_EquiContourOptions opts)
            {
                var result = new EquiContourOptions();
                result.Init(opts, def.Package);
                return result;
            }

            _kingdom = ob.Kingdom != null ? Create(ob.Kingdom) : null;
            _region = ob.Region != null ? Create(ob.Region) : null;
        }

        public override bool IsSupported(MyPlanet planet, MyPlanetMapZoomLevel zoom)
        {
            if (!base.IsSupported(planet, zoom))
                return false;
            switch (zoom)
            {
                case MyPlanetMapZoomLevel.Kingdom:
                    return _kingdom != null;
                case MyPlanetMapZoomLevel.Region:
                    return _region != null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zoom), zoom, null);
            }
        }

        public override ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view)
        {
            EquiContoursMapLayer layer;
            switch (view.Zoom)
            {
                case MyPlanetMapZoomLevel.Kingdom:
                    layer = new EquiContoursMapLayer(_kingdom, this);
                    break;
                case MyPlanetMapZoomLevel.Region:
                    layer = new EquiContoursMapLayer(_region, this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            layer.Init(control, view, new MyObjectBuilder_PlanetMapRenderLayer());
            return layer;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiContoursMapLayerDefinition : MyObjectBuilder_EquiCustomMapLayerDefinition
    {
        public MyObjectBuilder_EquiContourOptions Kingdom;
        public MyObjectBuilder_EquiContourOptions Region;
    }
}