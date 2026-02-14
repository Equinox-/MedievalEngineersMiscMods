using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiRegionMarketHostComponent))]
    public class EquiRegionMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public long RegionId { get; private set; }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiRegionMarketHostComponent)base.Serialize(copy);
            ob.RegionId = RegionId;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiRegionMarketHostComponent)builder;
            RegionId = ob.RegionId;
        }

        public override string ToString()
        {
            if (TryGetPlanet(out var planet) && planet.Components.TryGet(out MyPlanetAreasComponent areas))
            {
                areas.UnpackRegionIdToNames(RegionId, out var kingdom, out var region);
                return $"Region: {kingdom}, {region}, {PlanetId}";
            }
            MyPlanetAreasComponent.UnpackAreaId(RegionId, out int rawKingdom, out var x, out var y);
            return $"Region: {rawKingdom}, {x}, {y}, {PlanetId}";
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiRegionMarketHostComponent : MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent
    {
        [XmlAttribute("RegionId")]
        public long RegionId;

        internal override string ContainerSubtype => "EquiRegionMarketStorage";
    }
}