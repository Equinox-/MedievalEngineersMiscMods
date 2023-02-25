using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Cartography.Derived;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.Definitions.GUI;
using ObjectBuilders.GUI.Map;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Utility;
using VRage;
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
        public MyMapGridView BoundTo => View;

        private readonly bool _debugPolygonVertices = false;

        private readonly EquiShapesMapLayerDefinition _definition;

        public EquiShapesMapLayer(EquiShapesMapLayerDefinition definition) => _definition = definition;

        protected override ShapeRenderArgs GetArgs(out bool shouldRender)
        {
            var area = Map.GetEnvironmentMapViewport(View, out var face);
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

        public override void Draw(float transitionAlpha)
        {
            base.Draw(transitionAlpha);
            if (!_debugPolygonVertices) return;
            using (PoolManager.Get(out List<EquiShapesMapLayerDefinition.Shape> shapes))
            {
                var args = GetArgs(out _);
                var environmentViewport = Map.GetEnvironmentMapViewport(View, out var face);
                var screenViewport = new RectangleF(Map.GetPositionAbsoluteTopLeft() + Map.MapOffset, Map.MapSize);

                var envToScreenScale = screenViewport.Size / environmentViewport.Size;
                var envToScreenTranslate = screenViewport.Position - envToScreenScale * environmentViewport.Position;

                _definition.QueryShapes(args.Face, args.Area, shapes);
                foreach (var shape in shapes)
                    if (shape is EquiShapesMapLayerDefinition.Polygon poly)
                    {
                        for (var i = 0; i < poly._points.Length; i++)
                        {
                            var screenPos = envToScreenScale * poly._points[i] + envToScreenTranslate;
                            MyFontHelper.DrawString(
                                MyGuiConstants.DEFAULT_FONT,
                                $"{i}",
                                screenPos, 0.5f,
                                colorMask: poly._fillColor.Value);
                        }
                    }
            }
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
                    ctx.StencilRule = shape.StencilRule;
                    shape.DrawTo(in shapesCtx, ref ctx);
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
            protected Shape(EquiShapesMapLayerDefinition owner, MyObjectBuilder_ShapesLayerShape shape)
            {
                var minElevation = shape.MinElevation ?? 0;
                var maxElevation = shape.MaxElevation ?? 1;
                StencilRule = new StencilRule(owner.StencilValue(minElevation), owner.StencilValue(maxElevation));
            }

            public abstract void DrawTo(in EquiShapesMapLayer.ShapesRenderContext shapes, ref RenderContext ctx);

            public BoundingBox2 Bounds { get; protected set; }

            public StencilRule StencilRule { get; }
        }

        public sealed class Polygon : Shape
        {
            public readonly Vector2[] _points;
            private readonly Color? _strokeColor;
            public readonly Color? _fillColor;
            private readonly FillStyle _fillStyle;

            public Polygon(EquiShapesMapLayerDefinition owner, MyObjectBuilder_ShapesLayerPolygon polygon) : base(owner, polygon)
            {
                _points = new Vector2[polygon.Points.Count];
                _strokeColor = polygon.StrokeColor;
                _fillColor = polygon.FillColor;
                _fillStyle = polygon.FillStyle ?? FillStyle.Solid;
                var bounds = BoundingBox2.CreateInvalid();
                for (var i = 0; i < polygon.Points.Count; i++)
                {
                    _points[i] = polygon.Points[i];
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
                    ctx.DrawPolygon(loop, _fillColor, _fillStyle, _strokeColor);
                }
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
            foreach (var polygon in ob.Polygons)
                if (!polygon.Disabled)
                {
                    var built = new Polygon(this, polygon);
                    var box = To3D(built.Bounds);
                    _shapeTrees[polygon.Face].AddProxy(ref box, built, 1);
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
    }

    public class MyObjectBuilder_ShapesLayerShape
    {
        [XmlAttribute]
        public int Face;

        [XmlAttribute]
        public bool Disabled;

        [XmlElement]
        public float? MinElevation;

        [XmlElement]
        public float? MaxElevation;
    }

    public class MyObjectBuilder_ShapesLayerPolygon : MyObjectBuilder_ShapesLayerShape
    {
        [XmlElement("Pt")]
        public List<SerializableVector2> Points;

        public ColorDefinitionRGBA? StrokeColor;
        public ColorDefinitionRGBA? FillColor;
        public FillStyle? FillStyle;
    }
}