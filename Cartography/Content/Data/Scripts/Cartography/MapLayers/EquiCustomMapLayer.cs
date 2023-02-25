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
        public bool VisibleByDefaultInKingdoms { get; private set; }
        public bool VisibleByDefaultInRegions { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiCustomMapLayerDefinition)def;
            Order = ob.Order ?? Id.SubtypeName;
            VisibleByDefaultInKingdoms = ob.VisibleByDefaultInKingdoms ?? ob.VisibleByDefault ?? false;
            VisibleByDefaultInRegions = ob.VisibleByDefaultInRegions ?? ob.VisibleByDefault ?? false;
        }

        public virtual bool IsSupported(MyPlanet planet, MyPlanetMapZoomLevel zoom)
        {
            return true;
        }

        public abstract ICustomMapLayer CreateLayer(MyPlanetMapControl control, MyMapGridView view);
    }

    public abstract class MyObjectBuilder_EquiCustomMapLayerDefinition : MyObjectBuilder_VisualDefinitionBase
    {
        public string Order;
        public bool? VisibleByDefault;
        public bool? VisibleByDefaultInKingdoms;
        public bool? VisibleByDefaultInRegions;
    }
}