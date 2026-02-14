using System.Xml.Serialization;
using Medieval.GameSystems;
using VRage;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiKingdomMarketHostComponent))]
    public class EquiKingdomMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public long KingdomId { get; private set; }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiKingdomMarketHostComponent)base.Serialize(copy);
            ob.KingdomId = KingdomId;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiKingdomMarketHostComponent)builder;
            KingdomId = ob.KingdomId;
        }

        public override string ToString() => $"Kingdom: {MyTexts.GetString(MyPlanetAreasComponent.KingdomNames[KingdomId])}, {PlanetId}";
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiKingdomMarketHostComponent : MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent
    {
        [XmlAttribute("KingdomId")]
        public long KingdomId;

        internal override string ContainerSubtype => "EquiKingdomMarketStorage";
    }
}