using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using VRage;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiKingdomMarketHostComponent))]
    public class EquiKingdomMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public int KingdomId { get; private set; }

        public override bool IsLocal(in Vector3D position)
        {
            if (!TryGetPlanet(out var planet)
                || MyGamePruningStructureSandbox.GetClosestPlanet(position) != planet
                || !planet.Components.TryGet(out MyPlanetAreasComponent areas))
                return false;
            var localQuery = Vector3D.Transform(in position, in planet.PositionComp.WorldMatrixInvScaledRef);
            var localCenter = areas.CalculateKingdomCenter(KingdomId);
            var cosBetween = localQuery.Dot(localCenter) / localQuery.Length() / localCenter.Length();
            var trustedCos = Math.Cos(MathHelper.Sqrt2 * MathHelper.PiOver2);
            return cosBetween >= trustedCos;
        }

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
        public int KingdomId;

        internal override string ContainerSubtype => "EquiKingdomMarketStorage";
    }
}