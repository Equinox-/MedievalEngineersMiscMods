using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;

namespace Equinox76561198048419394.Core.Market
{
    public abstract class EquiMarketHostComponent : MyEntityComponent
    {
        public override bool IsSerialized => true;

        // Must always implement MarketHostComponent
        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false) => (MyObjectBuilder_EquiMarketHostComponent) base.Serialize(copy);
    }

    public abstract class MyObjectBuilder_EquiMarketHostComponent : MyObjectBuilder_EntityComponent
    {
    }
}