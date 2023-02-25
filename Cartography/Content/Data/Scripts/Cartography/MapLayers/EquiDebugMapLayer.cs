using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Cartography.Derived.Contours;
using Medieval.GameSystems;
using Medieval.GUI.Ingame.Map;
using Medieval.GUI.Ingame.Map.RenderLayers;
using ObjectBuilders.GUI.Map;
using Sandbox.Game.Entities;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Utility;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Input.Devices.Mouse;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public class EquiDebugMapLayer : MyPlanetMapRenderLayerBase, ICustomMapLayer
    {
        private readonly EquiDebugMapLayerDefinition _definition;

        public EquiDebugMapLayer(EquiDebugMapLayerDefinition definition) => _definition = definition;
        public MyMapGridView BoundTo => View;
        public EquiCustomMapLayerDefinition Source => _definition;

        public override void Draw(float transitionAlpha)
        {
            if (!Map.IsMouseOver)
                return;
            var environmentViewport = Map.GetEnvironmentMapViewport(View, out var face);
            var screenViewport = new RectangleF(Map.GetPositionAbsoluteTopLeft() + Map.MapOffset, Map.MapSize);
            var mouseNormPos = (MyGuiManager.MouseCursorPosition - screenViewport.Position) / screenViewport.Size;
            if (mouseNormPos.X < 0 || mouseNormPos.Y < 0 || mouseNormPos.X > 1 || mouseNormPos.Y > 1)
                return;
            var mouseEnvPos = (mouseNormPos * environmentViewport.Size) + environmentViewport.Position;
            
            MyFontHelper.DrawString(
                MyGuiConstants.DEFAULT_FONT, 
                $"F: {face}\nX: {mouseEnvPos.X:F08}\nY: {mouseEnvPos.Y:F08}", 
                screenViewport.Position, 1f);
            if (MyAPIGateway.Input.IsMousePressed(MyMouseButtons.Left))
            {
                MyLog.Default.Log(LogSeverity.Verbatim, $"<Pt x=\"{mouseEnvPos.X}\" y=\"{mouseEnvPos.Y}\" />");
                MyLog.Default.Flush();
            }
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiDebugMapLayerDefinition))]
    public class EquiDebugMapLayerDefinition : EquiCustomMapLayerDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiDebugMapLayerDefinition)def;
        }

        public override ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view)
        {
            var layer = new EquiDebugMapLayer(this);
            layer.Init(control, view, new MyObjectBuilder_PlanetMapRenderLayer());
            return layer;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDebugMapLayerDefinition : MyObjectBuilder_EquiCustomMapLayerDefinition
    {
    }
}