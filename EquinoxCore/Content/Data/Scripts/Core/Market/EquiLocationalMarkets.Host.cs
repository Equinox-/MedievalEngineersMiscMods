using System.Xml.Serialization;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Scene;

namespace Equinox76561198048419394.Core.Market
{
    public abstract class EquiLocationalMarketHostComponent : EquiMarketHostComponent
    {
        // Must always implement EquiLocationalMarketHostComponent
        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false) => (MyObjectBuilder_EquiLocationalMarketHostComponent) base.Serialize(copy);
    }

    public abstract class MyObjectBuilder_EquiLocationalMarketHostComponent : MyObjectBuilder_EquiMarketHostComponent
    {
        internal abstract string ContainerSubtype { get; }
    }

    public abstract class EquiPlanetAssociatedMarketHostComponent : EquiLocationalMarketHostComponent
    {
        private MyEntity _planetCached;

        /// <summary>
        /// Planet entity ID this market is associated with. 
        /// </summary>
        public EntityId PlanetId { get; private set; }

        /// <summary>
        /// Gets the planet entity this market is associated with.
        /// </summary>
        public bool TryGetPlanet(out MyEntity planet)
        {
            planet = _planetCached;
            if (planet != null && planet.Scene == Scene && planet.Id == PlanetId)
                return true;
            if (Scene == null || !Scene.TryGetEntity(PlanetId, out planet))
                planet = null;
            _planetCached = planet;
            return planet != null;
        }

        /// <summary>
        /// Gets the planet entity this market is associated with, or null if the planet is not loaded.
        /// </summary>
        public MyEntity Planet => TryGetPlanet(out var planet) ? planet : null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            TryGetPlanet(out _);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            _planetCached = null;
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent)base.Serialize(copy);
            ob.PlanetId = PlanetId.Value;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent)builder;
            PlanetId = ob.PlanetId;
        }
    }

    public abstract class MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent : MyObjectBuilder_EquiLocationalMarketHostComponent
    {
        [XmlAttribute("PlanetId")]
        public ulong PlanetId;
    }

}