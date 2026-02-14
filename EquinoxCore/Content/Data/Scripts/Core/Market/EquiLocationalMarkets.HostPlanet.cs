using System.Xml.Serialization;
using VRage.Game.Components;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiPlanetMarketHostComponent))]
    public class EquiPlanetMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public override string ToString() => $"Planet: {PlanetId}";
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlanetMarketHostComponent : MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent
    {
        internal override string ContainerSubtype => "EquiPlanetMarketStorage";
    }
}