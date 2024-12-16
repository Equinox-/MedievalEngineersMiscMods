using System;
using System.Xml.Serialization;
using Medieval.GUI.Ingame.Map;
using Medieval.GUI.Ingame.Map.RenderLayers;
using ObjectBuilders.GUI.Map;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Utility;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Input.Devices.Mouse;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public class EquiDebugMapLayer : MyPlanetMapRenderLayerBase, ICustomMapLayer
    {
        private readonly EquiDebugMapLayerDefinition _definition;

        public EquiDebugMapLayer(EquiDebugMapLayerDefinition definition) => _definition = definition;

        MyPlanetMapControl ICustomMapLayer.Map => Map;
        MyMapGridView ICustomMapLayer.View => View;
        public EquiCustomMapLayerDefinition Source => _definition;

        public override void Draw(float transitionAlpha)
        {
            if (!Map.IsMouseOver)
                return;
            if (!Map.TryGetMouseEnvironmentPosition(out var face, out var mouseEnvPos))
                return;
            var screenViewport = Map.GetScreenViewport();
            var player = MySession.Static?.PlayerEntity?.GetPosition() ?? Vector3D.Zero;
            
            var localPos = player - Map.Planet.GetPosition();

            var elevation = localPos.Normalize() - Map.Planet.AverageRadius;
            var lng = MathHelper.ToDegrees(Math.Atan2(-localPos.X, -localPos.Z));
            var lat = MathHelper.ToDegrees(Math.Atan2(localPos.Y, Math.Sqrt(localPos.X * localPos.X + localPos.Z * localPos.Z)));

            MyFontHelper.DrawString(
                MyGuiConstants.DEFAULT_FONT, 
                $"F: {face}\nX: {mouseEnvPos.X:F08}\nY: {mouseEnvPos.Y:F08}\nP: {lng:F8},{lat:F8},{elevation:F8}", 
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