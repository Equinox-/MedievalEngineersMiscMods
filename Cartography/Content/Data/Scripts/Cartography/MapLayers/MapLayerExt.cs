using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using ObjectBuilders.Definitions.GUI;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Collections;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Cartography.MapLayers
{
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

        public void AddTo(MyTooltip tooltip)
        {
            tooltip.AddLine(Content,
                Scale ?? (Title ? tooltip.TitleFont.Size : tooltip.BodyFont.Size),
                Font != null ? MyStringHash.GetOrCompute(Font) : (Title ? tooltip.TitleFont.Font : tooltip.BodyFont.Font),
                Color ?? (Title ? tooltip.TitleFont.Color : tooltip.BodyFont.Color));
        }
    }

    public static class MapLayerExt
    {
        public static void AddAllTo(this ListReader<TooltipLine> lines, MyTooltip tooltip)
        {
            tooltip.RecalculateOnChange = false;
            ((ICollection<MyColoredText>)tooltip.ToolTips).Clear();
            foreach (var line in lines)
                line.AddTo(tooltip);
            tooltip.RecalculateSize();
            tooltip.RecalculateOnChange = true;
        }

        public static RectangleF GetEnvironmentMapViewport(this MyPlanetMapControl control, out int face)
        {
            var view = control.CurrentView;
            var planet = control.Planet;
            var areas = planet.Get<MyPlanetAreasComponent>();
            int scalingCount;
            switch (view.Zoom)
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

            var counts = view.Size;
            MyPlanetAreasComponent.UnpackAreaId(view[0, 0], out face, out var minCellX, out var minCellY);
            MyPlanetAreasComponent.UnpackAreaId(view[counts.X - 1, counts.Y - 1], out _, out int maxCellX, out var maxCellY);

            var minTexCoord = (2 * new Vector2(minCellX, minCellY) - scalingCount) / scalingCount;
            var maxTexCoord = (2 * new Vector2(maxCellX + 1, maxCellY + 1) - scalingCount) / scalingCount;
            return new RectangleF(minTexCoord, maxTexCoord - minTexCoord);
        }

        public static RectangleF GetScreenViewport(this MyPlanetMapControl control)
        {
            return new RectangleF(control.GetPositionAbsoluteTopLeft() + control.MapOffset, control.MapSize);
        }

        public static bool TryGetMouseEnvironmentPosition(
            in RectangleF environmentViewport, in RectangleF screenViewport,
            out Vector2 uv)
        {
            var mouseNormPos = (MyGuiManager.MouseCursorPosition - screenViewport.Position) / screenViewport.Size;
            uv = default;
            if (mouseNormPos.X < 0 || mouseNormPos.Y < 0 || mouseNormPos.X > 1 || mouseNormPos.Y > 1)
                return false;
            uv = (mouseNormPos * environmentViewport.Size) + environmentViewport.Position;
            return true;
        }

        public static bool TryGetMouseEnvironmentPosition(this MyPlanetMapControl control, out int face, out Vector2 uv)
        {
            var environmentViewport = control.GetEnvironmentMapViewport(out face);
            var screenViewport = control.GetScreenViewport();
            return TryGetMouseEnvironmentPosition(in environmentViewport, in screenViewport, out uv);
        }
    }
}