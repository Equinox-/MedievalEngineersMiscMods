using System.Xml.Serialization;
using Sandbox.Game.Entities;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiPlanetMarketHostComponent))]
    public class EquiPlanetMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public override bool IsLocal(in Vector3D position) =>
            TryGetPlanet(out var planet) && MyGamePruningStructureSandbox.GetClosestPlanet(position) == planet;

        public override string ToString() => $"Planet: {PlanetId}";
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlanetMarketHostComponent : MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent
    {
        internal override string ContainerSubtype => "EquiPlanetMarketStorage";
    }
}