using System;
using Medieval.GUI.Ingame.Map;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    [MyDefinitionGroup]
    [MyDefinitionType(typeof(MyObjectBuilder_EquiCustomMapLayerDefinition))]
    public abstract class EquiCustomMapLayerDefinition : MyVisualDefinitionBase
    {
        public string Order { get; private set; }
        public CustomMapLayerVisibility Visibility { get; private set; }
        public CustomMapLayerVisibility VisibilityByDefault { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiCustomMapLayerDefinition)def;
            Order = ob.Order ?? Id.SubtypeName;
            Visibility = ob.Visible ?? CustomMapLayerVisibility.Both;
            VisibilityByDefault = (ob.VisibleByDefault ?? CustomMapLayerVisibility.None) & Visibility;
        }

        public virtual bool IsSupported(MyPlanet planet, MyPlanetMapZoomLevel zoom)
        {
            return Visibility.IsVisible(zoom);
        }

        public abstract ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view);
    }

    public abstract class MyObjectBuilder_EquiCustomMapLayerDefinition : MyObjectBuilder_VisualDefinitionBase
    {
        public string Order;
        public CustomMapLayerVisibility? Visible;
        public CustomMapLayerVisibility? VisibleByDefault;
    }
}