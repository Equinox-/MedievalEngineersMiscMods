using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Medieval.GUI.Ingame.Map;
using Medieval.GUI.Ingame.Map.RenderLayers;
using ObjectBuilders.Definitions.GUI;
using ObjectBuilders.GUI.Map;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Utility;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public class EquiSymbolsMapLayer : MyPlanetMapRenderLayerBase, ICustomMapLayer
    {
        private readonly EquiSymbolsMapLayerDefinition _definition;
        private readonly MyTooltip _tooltip;

        private EquiSymbolsMapLayerDefinition.Symbol _currentTooltipFor;

        public EquiSymbolsMapLayer(EquiSymbolsMapLayerDefinition definition)
        {
            _definition = definition;
            _tooltip = new MyTooltip();
        }

        public MyMapGridView BoundTo => View;

        public override void Draw(float transitionAlpha)
        {
            var environmentViewport = Map.GetEnvironmentMapViewport(View, out var face);
            var screenViewport = new RectangleF(Map.GetPositionAbsoluteTopLeft() + Map.MapOffset, Map.MapSize);
            var mouseScreenPos = MyGuiManager.MouseCursorPosition;

            using (PoolManager.Get(out List<EquiSymbolsMapLayerDefinition.Symbol> symbols))
            {
                var envToScreenScale = screenViewport.Size / environmentViewport.Size;
                var envToScreenTranslate = screenViewport.Position - envToScreenScale * environmentViewport.Position;

                _definition.QuerySymbols(View.Zoom, face, environmentViewport, symbols);

                EquiSymbolsMapLayerDefinition.Symbol bestTooltip = null;
                var bestTooltipAnchorDist2 = float.PositiveInfinity;

                void MaybeTooltip(EquiSymbolsMapLayerDefinition.Symbol symbol, RectangleF region)
                {
                    if (!region.Contains(mouseScreenPos) || symbol.Tooltip.Count == 0)
                        return;
                    var dist2 = Vector2.Distance(region.Position + region.Size / 2, mouseScreenPos);
                    if (dist2 < bestTooltipAnchorDist2)
                    {
                        bestTooltip = symbol;
                        bestTooltipAnchorDist2 = dist2;
                    }
                }

                foreach (var symbol in symbols)
                {
                    ref readonly var placement = ref symbol.Placement(View.Zoom);
                    var anchor = symbol.Center * envToScreenScale + envToScreenTranslate;
                    if (placement.IconGroupsSize.X > 0 && placement.IconGroupsSize.Y > 0)
                    {
                        var leftCenter = new Vector2(anchor.X - placement.IconGroupsSize.X / 2, anchor.Y);
                        foreach (var group in symbol.IconGroups)
                        {
                            var groupSize = placement.IconGuideSize * group.Scale;
                            var region = new RectangleF(leftCenter - new Vector2(0, groupSize.Y / 2), groupSize);
                            MaybeTooltip(symbol, region);
                            var screenRegion = new RectangleF(
                                MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(region.Position),
                                MyGuiManager.GetScreenSizeFromNormalizedSize(region.Size));

                            Rectangle? source = null;
                            foreach (var icon in group.Layers)
                                MyRenderProxy.DrawSprite(icon,
                                    ref screenRegion, false,
                                    ref source, symbol.IconColor, 0, Vector2.UnitX,
                                    ref Vector2.Zero, SpriteEffects.None, 0);

                            leftCenter.X += groupSize.X;
                        }

                        anchor += new Vector2(0, (placement.IconGroupsSize.Y + placement.TextSize.Y) / 2);
                    }

                    if (placement.TextSize.X > 0 && placement.TextSize.Y > 0)
                    {
                        var textRegion = new RectangleF(anchor - placement.TextSize / 2, placement.TextSize);
                        if (screenViewport.Contains(textRegion.Position) && screenViewport.Contains(textRegion.Position + textRegion.Size))
                        {
                            MaybeTooltip(symbol, textRegion);
                            MyRenderProxy.DrawString(
                                symbol.Font,
                                MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(textRegion.Position),
                                symbol.TextColor, symbol.Text, placement.TextScale,
                                float.PositiveInfinity);
                        }
                    }
                }

                if (_currentTooltipFor != bestTooltip)
                {
                    _currentTooltipFor = bestTooltip;
                    if (bestTooltip != null)
                    {
                        ((ICollection<MyColoredText>)_tooltip.ToolTips).Clear();
                        foreach (var line in bestTooltip.Tooltip)
                        {
                            _tooltip.AddLine(line.Content,
                                line.Scale ?? (line.Title ? _tooltip.TitleFont.Size : _tooltip.BodyFont.Size),
                                line.Font != null ? MyStringHash.GetOrCompute(line.Font) : (line.Title ? _tooltip.TitleFont.Font : _tooltip.BodyFont.Font),
                                line.Color ?? (line.Title ? _tooltip.TitleFont.Color : _tooltip.BodyFont.Color));
                        }
                    }

                    EquiCustomMapLayersControl.CustomLayers(Map)?.BindTooltip(this, bestTooltip != null ? _tooltip : null);
                }
            }
        }

        public EquiCustomMapLayerDefinition Source => _definition;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiSymbolsMapLayerDefinition))]
    public class EquiSymbolsMapLayerDefinition : EquiCustomMapLayerDefinition
    {
        private static readonly Vector2 ScreenPerEnvironmentKingdom = new Vector2(.55125f, .7636f) / 2;
        private static readonly Vector2 ScreenPerEnvironmentRegion = ScreenPerEnvironmentKingdom * 10;

        private readonly MyDynamicAABBTree[] _kingdomShapeTrees = new MyDynamicAABBTree[6];
        private readonly MyDynamicAABBTree[] _regionShapeTrees = new MyDynamicAABBTree[6];

        public readonly struct SymbolPlacement
        {
            public readonly Vector2 IconGuideSize;
            public readonly Vector2 IconGroupsSize;
            public readonly Vector2 TextSize;
            public readonly float TextScale;

            public SymbolPlacement(
                Vector2 iconGuideSize,
                Vector2 iconGroupsSize,
                Vector2 textSize,
                float textScale)
            {
                IconGuideSize = iconGuideSize;
                IconGroupsSize = iconGroupsSize;
                TextSize = textSize;
                TextScale = textScale;
            }

            public bool Visible => (IconGroupsSize.X > 0 && IconGroupsSize.Y > 0) || (TextSize.X > 0 && TextSize.Y > 0);
        }

        public readonly struct IconGroup
        {
            public readonly ListReader<string> Layers;
            public readonly float Scale;

            public IconGroup(ListReader<string> layers, float scale)
            {
                Layers = layers;
                Scale = scale;
            }
        }

        public sealed class Symbol
        {
            public readonly Vector2 Center;

            public readonly MyStringHash Font;
            public readonly string Text;
            public readonly Color TextColor;

            public readonly ListReader<IconGroup> IconGroups;
            public readonly Color IconColor;

            public readonly SymbolPlacement KingdomPlacement;
            public readonly SymbolPlacement RegionPlacement;

            public readonly ListReader<MyObjectBuilder_EquiSymbolsMapLayerDefinition.TooltipLine> Tooltip;

            public Symbol(MyObjectBuilder_EquiSymbolsMapLayerDefinition.MyObjectBuilder_Symbol ob)
            {
                Center = ob.Position;
                Font = ob.Font != null ? MyStringHash.GetOrCompute(ob.Font) : MyGuiConstants.DEFAULT_FONT;
                Text = ob.Text;
                TextColor = ob.TextColor ?? Color.White;
                IconGroups = ob.IconGroups
                    .Select(x => new IconGroup(x.Icons, x.Scale > 0 ? x.Scale : 1))
                    .Where(x => x.Layers.Count > 0)
                    .ToList();
                IconColor = ob.IconColor ?? Color.White;
                Tooltip = ob.Tooltip;

                SymbolPlacement CreatePlacement(float textScale, float iconSize)
                {
                    var iconSizeGuide = new Vector2(iconSize);
                    var iconGroupsSize = Vector2.Zero;
                    foreach (var group in IconGroups)
                    {
                        var groupSize = group.Scale * iconSizeGuide;
                        iconGroupsSize.X += groupSize.X;
                        iconGroupsSize.Y = Math.Max(iconGroupsSize.Y, groupSize.Y);
                    }

                    var textSize = textScale > 0 && !string.IsNullOrEmpty(Text) ? MyFontHelper.MeasureString(Font, Text, textScale) : Vector2.Zero;
                    return new SymbolPlacement(iconSizeGuide, iconGroupsSize, textSize, textScale);
                }

                KingdomPlacement = CreatePlacement(ob.TextKingdomScale ?? 0, ob.IconKingdomSize ?? .015f);
                RegionPlacement = CreatePlacement(ob.TextRegionScale ?? .5f, ob.IconRegionSize ?? .03f);
            }

            public ref readonly SymbolPlacement Placement(MyPlanetMapZoomLevel zoom)
            {
                switch (zoom)
                {
                    case MyPlanetMapZoomLevel.Kingdom:
                        return ref KingdomPlacement;
                    case MyPlanetMapZoomLevel.Region:
                        return ref RegionPlacement;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(zoom), zoom, null);
                }
            }
        }

        private static BoundingBox To3D(BoundingBox2 box) => new BoundingBox(new Vector3(box.Min, -1), new Vector3(box.Max, 1));

        public void QuerySymbols(MyPlanetMapZoomLevel zoomLevel, int face, in RectangleF query, List<Symbol> icons)
        {
            var queryBox = To3D(new BoundingBox2(query.Position, query.Position + query.Size));
            MyDynamicAABBTree[] trees;
            switch (zoomLevel)
            {
                case MyPlanetMapZoomLevel.Kingdom:
                    trees = _kingdomShapeTrees;
                    break;
                case MyPlanetMapZoomLevel.Region:
                    trees = _regionShapeTrees;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zoomLevel), zoomLevel, null);
            }

            trees[face].OverlapAllBoundingBox(ref queryBox, icons);
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiSymbolsMapLayerDefinition)def;
            for (var i = 0; i < _kingdomShapeTrees.Length; i++)
            {
                _kingdomShapeTrees[i] = new MyDynamicAABBTree(Vector3.Zero);
                _regionShapeTrees[i] = new MyDynamicAABBTree(Vector3.Zero);
            }

            foreach (var icon in ob.Symbols)
            {
                var built = new Symbol(icon);

                void Collect(MyDynamicAABBTree tree, MyPlanetMapZoomLevel zoom)
                {
                    ref readonly var placement = ref built.Placement(zoom);
                    if (!placement.Visible)
                        return;
                    var scalingForBounds = zoom == MyPlanetMapZoomLevel.Kingdom ? ScreenPerEnvironmentKingdom : ScreenPerEnvironmentRegion;
                    var area = BoundingBox2.CreateInvalid();
                    var scaledIconSize = placement.IconGroupsSize / scalingForBounds;
                    area.Include(built.Center - scaledIconSize / 2);
                    area.Include(built.Center + scaledIconSize / 2);
                    var scaledTextSize = placement.TextSize / scalingForBounds;
                    var textCenter = built.Center + new Vector2(0, (scaledIconSize.Y + scaledTextSize.Y) / 2);
                    area.Include(textCenter - scaledTextSize / 2);
                    area.Include(textCenter + scaledTextSize / 2);
                    var bounds = To3D(area);
                    tree.AddProxy(ref bounds, built, 0);
                }

                Collect(_kingdomShapeTrees[icon.Face], MyPlanetMapZoomLevel.Kingdom);
                Collect(_regionShapeTrees[icon.Face], MyPlanetMapZoomLevel.Region);
            }
        }

        public override ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view)
        {
            var layer = new EquiSymbolsMapLayer(this);
            layer.Init(control, view, new MyObjectBuilder_PlanetMapRenderLayer());
            return layer;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSymbolsMapLayerDefinition : MyObjectBuilder_EquiCustomMapLayerDefinition
    {
        [XmlElement("Symbol")]
        public List<MyObjectBuilder_Symbol> Symbols;

        public struct TooltipLine
        {
            [XmlAttribute]
            public bool Title;

            [XmlAttribute]
            public string Font;

            [XmlIgnore]
            public float? Scale;

            [XmlAttribute("Scale")]
            public float ScaleAttr
            {
                get => Scale ?? .7f;
                set => Scale = value;
            }

            [XmlAttribute]
            public string Content;

            [XmlElement]
            public ColorDefinitionRGBA? Color;
        }

        public class MyObjectBuilder_Symbol
        {
            [XmlAttribute]
            public int Face;

            [XmlElement]
            public SerializableVector2 Position;

            [XmlElement]
            public string Font;

            [XmlElement]
            public string Text;

            [XmlElement]
            public ColorDefinitionRGBA? TextColor;

            [XmlElement]
            public float? TextKingdomScale;

            [XmlElement]
            public float? TextRegionScale;

            public struct IconGroup
            {
                [XmlElement("Icon")]
                public List<string> Icons;

                [XmlAttribute]
                public float Scale;
            }

            [XmlElement("IconGroup")]
            public List<IconGroup> IconGroups;

            [XmlElement]
            public ColorDefinitionRGBA? IconColor;

            [XmlElement]
            public float? IconKingdomSize;

            [XmlElement]
            public float? IconRegionSize;

            [XmlElement]
            public string TooltipTitle;

            [XmlElement("Tooltip")]
            public List<TooltipLine> Tooltip;
        }
    }
}