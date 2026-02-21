using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    public abstract class EquiMarketHostComponent : MyEntityComponent
    {
        public override bool IsSerialized => true;

        /// <summary>
        /// Determines if the give position is considered "local" for this market,
        /// where local means the position is within the boundaries of the market.
        /// </summary>
        public abstract bool IsLocal(in Vector3D position);

        // Must always implement MarketHostComponent
        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false) => (MyObjectBuilder_EquiMarketHostComponent) base.Serialize(copy);
    }

    public abstract class MyObjectBuilder_EquiMarketHostComponent : MyObjectBuilder_EntityComponent
    {
    }
}