using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Cartography.Derived;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.Definitions.GUI;
using ObjectBuilders.GUI.Map;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public class EquiShapesMapLayer : EquiRasterizedMapLayer<ShapeRenderArgs>, ICustomMapLayer
    {
        private readonly MyTooltip _tooltip = new MyTooltip();
        private EquiShapesMapLayerDefinition.Shape _boundTooltip;

        MyPlanetMapControl ICustomMapLayer.Map => Map;
        MyMapGridView ICustomMapLayer.View => View;

        private readonly EquiShapesMapLayerDefinition _definition;

        public EquiShapesMapLayer(EquiShapesMapLayerDefinition definition) => _definition = definition;

        protected override ShapeRenderArgs GetArgs(out bool shouldRender)
        {
            var area = Map.GetEnvironmentMapViewport(out var face);
            ElevationData elevation = null;
            if (_definition.HasStencil)
            {
                elevation = MySession.Static.Components.Get<EquiCartography>().GetOrComputeElevationAsync(new ElevationArgs(Map.Planet, face, area));
                if (elevation == null)
                {
                    shouldRender = false;
                    return default;
                }
            }

            shouldRender = true;
            return new ShapeRenderArgs(face, area, elevation);
        }

        protected override void Render(in ShapeRenderArgs args, ref RenderContext ctx)
        {
            var shapesCtx = new ShapesRenderContext(in args.Area, ctx.Size);
            using (PoolManager.Get(out List<EquiShapesMapLayerDefinition.Shape> shapes))
            {
                _definition.QueryShapes(args.Face, args.Area, shapes);
                if (_definition.HasStencil)
                {
                    // Prepare stencil.
                    var rowStart = 0;
                    for (var y = 0; y < ctx.Size.Y; y++)
                    {
                        var pos = rowStart;
                        for (var x = 0; x < ctx.Size.X; x++)
                        {
                            pos += ctx.PixelStride;
                            var elevationNorm = args.ElevationData.SampleNorm(x / (float)ctx.Size.X, y / (float)ctx.Size.Y);
                            ctx.Stencil[pos >> 2] = _definition.StencilValue(elevationNorm);
                        }

                        rowStart += ctx.RowStride;
                    }
                }

                foreach (var shape in shapes)
                {
                    if (!shape.Visibility.IsVisible(View.Zoom))
                        continue;
                    ctx.StencilRule = shape.StencilRule;
                    shape.DrawTo(in shapesCtx, ref ctx);
                }
            }
        }

        private const int TooltipInterval = 5;
        private int _recalculateTooltips;

        protected override void AfterDrawn(in ShapeRenderArgs args, in StencilAccessor stencil)
        {
            base.AfterDrawn(in args, in stencil);
            if (_recalculateTooltips++ < TooltipInterval)
                return;
            _recalculateTooltips = 0;

            var environmentViewport = Map.GetEnvironmentMapViewport(out var face);
            var screenViewport = Map.GetScreenViewport();
            var mouseNormPos = (MyGuiManager.MouseCursorPosition - screenViewport.Position) / screenViewport.Size;
            if (mouseNormPos.X < 0 || mouseNormPos.Y < 0 || mouseNormPos.X >= 1 || mouseNormPos.Y >= 1 || face != args.Face)
            {
                _boundTooltip = null;
                this.BindTooltip(null);
                return;
            }

            var mouseEnv = (mouseNormPos * environmentViewport.Size) + environmentViewport.Position;
            var envQuerySize = .01f * environmentViewport.Size;
            var envQueryRadius = envQuerySize.Length();

            byte mouseStencilValue = default;
            if (_definition.HasStencil)
            {
                var mouseStencil = Vector2I.Floor(mouseNormPos * stencil.Size);
                mouseStencilValue = stencil.ReadStencil(mouseStencil.X, mouseStencil.Y);
            }

            bool TestShape(EquiShapesMapLayerDefinition.Shape shape)
            {
                if (shape.Tooltip.Count == 0
                    || shape.Face != face
                    || !shape.TooltipVisibility.IsVisible(View.Zoom)
                    || !shape.StencilRule.Test(mouseStencilValue))
                    return false;

                return shape.Intersects(mouseEnv, envQueryRadius);
            }

            if (_boundTooltip != null && TestShape(_boundTooltip))
                return;

            var envQuery = new RectangleF(mouseEnv - envQuerySize / 2, envQuerySize);

            using (PoolManager.Get(out List<EquiShapesMapLayerDefinition.Shape> shapes))
            {
                _definition.QueryShapes(args.Face, envQuery, shapes);
                EquiShapesMapLayerDefinition.Shape bestTooltip = null;
                for (var i = shapes.Count - 1; i >= 0; i--)
                {
                    var shape = shapes[i];
                    if (TestShape(shape))
                    {
                        bestTooltip = shape;
                        break;
                    }
                }

                if (_boundTooltip != bestTooltip)
                {
                    _boundTooltip = bestTooltip;
                    bestTooltip?.Tooltip.AddAllTo(_tooltip);
                    this.BindTooltip(bestTooltip != null ? _tooltip : null);
                }
            }
        }

        public readonly struct ShapesRenderContext
        {
            private readonly Vector2I _size;
            private readonly RectangleF _area;

            public ShapesRenderContext(in RectangleF area, Vector2I size)
            {
                _area = area;
                _size = size;
            }

            public Vector2I Project(Vector2 pt) => Vector2I.Round(_size * (pt - _area.Position) / _area.Size);
        }

        public EquiCustomMapLayerDefinition Source => _definition;
    }

    public readonly struct ShapeRenderArgs : IEquatable<ShapeRenderArgs>
    {
        public readonly int Face;
        public readonly RectangleF Area;
        public readonly ElevationData ElevationData;

        public ShapeRenderArgs(int face, RectangleF area, ElevationData elevationData)
        {
            Face = face;
            Area = area;
            ElevationData = elevationData;
        }

        public bool Equals(ShapeRenderArgs other) => Face == other.Face && Area.Equals(other.Area) && Equals(ElevationData, other.ElevationData);

        public override bool Equals(object obj) => obj is ShapeRenderArgs other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Face;
                hashCode = (hashCode * 397) ^ Area.GetHashCode();
                hashCode = (hashCode * 397) ^ (ElevationData != null ? ElevationData.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiShapesMapLayerDefinition))]
    public class EquiShapesMapLayerDefinition : EquiCustomMapLayerDefinition
    {
        private readonly MyDynamicAABBTree[] _shapeTrees = new MyDynamicAABBTree[6];
        private readonly List<float> _stencilSteps = new List<float>();
        public bool HasStencil => _stencilSteps.Count > 2;

        public abstract class Shape
        {
            protected Shape(EquiShapesMapLayerDefinition owner, MyObjectBuilder_ShapesLayerShape shape, int face)
            {
                var minElevation = shape.MinElevation ?? 0;
                var maxElevation = shape.MaxElevation ?? 1;
                StencilRule = new StencilRule(owner.StencilValue(minElevation), owner.StencilValue(maxElevation));
                Tooltip = shape.Tooltip;
                Face = face;
                Visibility = shape.Visibility ?? CustomMapLayerVisibility.Both;
                TooltipVisibility = shape.TooltipVisibility ?? CustomMapLayerVisibility.Both;
            }

            public abstract bool Intersects(Vector2 pt, float radius);

            public abstract void DrawTo(in EquiShapesMapLayer.ShapesRenderContext shapes, ref RenderContext ctx);

            public int Face { get; private set; }

            public CustomMapLayerVisibility Visibility { get; }

            public BoundingBox2 Bounds { get; protected set; }

            public StencilRule StencilRule { get; }

            public CustomMapLayerVisibility TooltipVisibility { get; }

            public ListReader<TooltipLine> Tooltip { get; }
        }

        private sealed class Polygon : Shape
        {
            private readonly Vector2[] _points;
            private readonly Color? _strokeColor;
            private readonly RenderContext.LineWidthPattern _strokeWidth;
            private readonly Color? _fillColor;
            private readonly FillStyle _fillStyle;

            public Polygon(EquiShapesMapLayerDefinition owner,
                MyObjectBuilder_ShapesLayerPolygon polygon,
                int face,
                List<SerializableVector2> points
            ) : base(owner, polygon, face)
            {
                _points = new Vector2[points.Count];
                _strokeColor = polygon.StrokeColor;
                _strokeWidth = new RenderContext.LineWidthPattern(polygon.StrokeWidth ?? "1", Log);
                _fillColor = polygon.FillColor;
                _fillStyle = polygon.FillStyle ?? FillStyle.Solid;
                var bounds = BoundingBox2.CreateInvalid();
                for (var i = 0; i < points.Count; i++)
                {
                    _points[i] = points[i];
                    bounds.Include(_points[i]);
                }

                Bounds = bounds;
            }

            public override void DrawTo(in EquiShapesMapLayer.ShapesRenderContext shapes, ref RenderContext ctx)
            {
                using (PoolManager.Get(out List<Vector2I> loop))
                {
                    if (loop.Capacity < _points.Length)
                        loop.Capacity = _points.Length;
                    foreach (var pt in _points)
                        loop.Add(shapes.Project(pt));
                    var pattern = _strokeWidth;
                    ctx.DrawPolygon(loop, _fillColor, _fillStyle, _strokeColor, ref pattern);
                }
            }

            public override bool Intersects(Vector2 pt, float radius)
            {
                // Not query radius aware.
                var crossings = 0;
                var prev = _points[_points.Length - 1];
                foreach (var curr in _points)
                {
                    if (
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        prev.Y != curr.Y
                        && Math.Min(prev.Y, curr.Y) <= pt.Y
                        && pt.Y < Math.Max(prev.Y, curr.Y)
                        && pt.X < Math.Max(prev.X, curr.X))
                    {
                        var dxPerDy = (curr.X - prev.X) / (curr.Y - prev.Y);
                        var crossesAtX = prev.X + dxPerDy * (pt.Y - prev.Y);
                        if (crossesAtX > pt.X)
                            crossings++;
                    }

                    prev = curr;
                }

                return (crossings & 1) == 1;
            }
        }

        private sealed class Line : Shape
        {
            private readonly Vector2[] _points;
            private readonly float[] _len2;
            private readonly Color? _strokeColor;
            private readonly RenderContext.LineWidthPattern _strokeWidth;

            public Line(EquiShapesMapLayerDefinition owner, MyObjectBuilder_ShapesLayerLine line,
                int face, List<SerializableVector2> points) : base(owner, line, face)
            {
                _len2 = new float[points.Count - 1];
                _points = new Vector2[points.Count];
                _strokeColor = line.StrokeColor;
                _strokeWidth = new RenderContext.LineWidthPattern(line.StrokeWidth ?? "1", Log);
                var bounds = BoundingBox2.CreateInvalid();
                for (var i = 0; i < points.Count; i++)
                {
                    _points[i] = points[i];
                    if (i > 0)
                        _len2[i - 1] = Vector2.DistanceSquared(_points[i - 1], _points[i]);
                    bounds.Include(_points[i]);
                }

                Bounds = bounds;
            }

            public override void DrawTo(in EquiShapesMapLayer.ShapesRenderContext shapes, ref RenderContext ctx)
            {
                if (_points.Length < 2 || !_strokeColor.HasValue)
                    return;
                var pattern = _strokeWidth;
                var prev = shapes.Project(_points[0]);
                for (var i = 1; i < _points.Length; i++)
                {
                    var curr = shapes.Project(_points[i]);
                    ctx.DrawLine(prev, curr, _strokeColor.Value, ref pattern);
                    prev = curr;
                }
            }

            public override bool Intersects(Vector2 pt, float radius)
            {
                var radiusSquared = radius * radius;
                for (var i = 0; i < _points.Length - 1; i++)
                {
                    var from = _points[i];
                    var to = _points[i + 1];
                    var len2 = _len2[i];
                    var delta = to - from;
                    var t = MathHelper.Clamp(Vector2.Dot(delta, pt - from) / len2, 0, 1);
                    var projection = from + t * delta;
                    if (Vector2.DistanceSquared(projection, pt) <= radiusSquared)
                        return true;
                }

                return false;
            }
        }

        private static BoundingBox To3D(BoundingBox2 box) => new BoundingBox(new Vector3(box.Min, -1), new Vector3(box.Max, 1));

        public void QueryShapes(int face, in RectangleF query, List<Shape> shapes)
        {
            var queryBox = To3D(new BoundingBox2(query.Position, query.Position + query.Size));
            _shapeTrees[face].OverlapAllBoundingBox(ref queryBox, shapes);
        }

        private void BuildStencilSteps(MyObjectBuilder_EquiShapesMapLayerDefinition ob)
        {
            _stencilSteps.Add(0);
            _stencilSteps.Add(1);

            void Collect(MyObjectBuilder_ShapesLayerShape shape)
            {
                if (shape.Disabled)
                    return;
                _stencilSteps.Add(shape.MinElevation ?? 0);
                _stencilSteps.Add(shape.MaxElevation ?? 1);
            }

            foreach (var polygon in ob.Polygons)
                Collect(polygon);

            _stencilSteps.Sort();
            var removed = 0;
            for (var i = 1; i < _stencilSteps.Count; i++)
            {
                var curr = _stencilSteps[i];
                if (Math.Abs(_stencilSteps[i - 1] - curr) < 1e-6f)
                    removed++;
                else if (removed > 0)
                    _stencilSteps[i - removed] = curr;
            }

            _stencilSteps.RemoveRange(_stencilSteps.Count - removed, removed);
            if (_stencilSteps.Count >= 256)
                throw new ArgumentException("Shape layer must have fewer than 256 elevation stops for stenciling");
        }

        public byte StencilValue(float normElevation)
        {
            var val = _stencilSteps.BinarySearch(normElevation);
            if (val < 0)
                val = ~val - 1;
            return (byte)MathHelper.Clamp(val, 0, byte.MaxValue);
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiShapesMapLayerDefinition)def;
            BuildStencilSteps(ob);

            for (var i = 0; i < _shapeTrees.Length; i++)
                _shapeTrees[i] = new MyDynamicAABBTree(Vector3.Zero);

            void Register(Shape built)
            {
                var box = To3D(built.Bounds);
                _shapeTrees[built.Face].AddProxy(ref box, built, 1);
            }

            foreach (var polygon in ob.Polygons)
                if (!polygon.Disabled)
                {
                    if (polygon.TopLevelPoints.Count >= 3)
                        Register(new Polygon(this, polygon, polygon.TopLevelFace, polygon.TopLevelPoints));
                    foreach (var child in polygon.Polygons)
                        if (child.Points.Count >= 3)
                            Register(new Polygon(this, polygon, child.Face, child.Points));
                }

            foreach (var line in ob.Lines)
                if (!line.Disabled)
                {
                    if (line.TopLevelPoints.Count >= 2)
                        Register(new Line(this, line, line.TopLevelFace, line.TopLevelPoints));
                    foreach (var child in line.Lines)
                        if (child.Points.Count >= 2)
                            Register(new Line(this, line, child.Face, child.Points));
                }
        }

        public override ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view)
        {
            var layer = new EquiShapesMapLayer(this);
            layer.Init(control, view, new MyObjectBuilder_PlanetMapRenderLayer());
            return layer;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiShapesMapLayerDefinition : MyObjectBuilder_EquiCustomMapLayerDefinition
    {
        [XmlElement("Polygon")]
        public List<MyObjectBuilder_ShapesLayerPolygon> Polygons;

        [XmlElement("Line")]
        public List<MyObjectBuilder_ShapesLayerLine> Lines;
    }

    public class MyObjectBuilder_ShapesLayerShape
    {
        [XmlAttribute]
        public bool Disabled;

        [XmlElement]
        public float? MinElevation;

        [XmlElement]
        public float? MaxElevation;

        [XmlElement]
        public CustomMapLayerVisibility? Visibility;

        [XmlElement]
        public CustomMapLayerVisibility? TooltipVisibility;

        [XmlElement("Tooltip")]
        public List<TooltipLine> Tooltip;
    }

    public class MyObjectBuilder_ShapesLayerPolygon : MyObjectBuilder_ShapesLayerShape
    {
        [XmlAttribute("Face")]
        public int TopLevelFace;

        [XmlElement("Pt")]
        public List<SerializableVector2> TopLevelPoints;

        [XmlElement("Polygon")]
        public List<MyObjectBuilder_ShapesLayerCoordinates> Polygons;

        public ColorDefinitionRGBA? StrokeColor;
        public string StrokeWidth;
        public ColorDefinitionRGBA? FillColor;
        public FillStyle? FillStyle;
    }

    public class MyObjectBuilder_ShapesLayerLine : MyObjectBuilder_ShapesLayerShape
    {
        [XmlAttribute("Face")]
        public int TopLevelFace;

        [XmlElement("Pt")]
        public List<SerializableVector2> TopLevelPoints;

        [XmlElement("Line")]
        public List<MyObjectBuilder_ShapesLayerCoordinates> Lines;

        public ColorDefinitionRGBA? StrokeColor;
        public string StrokeWidth;
    }

    public class MyObjectBuilder_ShapesLayerCoordinates
    {
        [XmlAttribute]
        public int Face;

        [XmlElement("Pt")]
        public List<SerializableVector2> Points;
    }
}