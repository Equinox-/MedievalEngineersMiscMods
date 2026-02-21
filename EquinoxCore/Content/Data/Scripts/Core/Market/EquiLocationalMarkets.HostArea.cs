using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    [MyComponent(typeof(MyObjectBuilder_EquiAreaMarketHostComponent))]
    public class EquiAreaMarketHostComponent : EquiPlanetAssociatedMarketHostComponent
    {
        public long AreaId { get; private set; }

        public override bool IsLocal(in Vector3D position)
        {
            if (!TryGetPlanet(out var planet)
                || MyGamePruningStructureSandbox.GetClosestPlanet(position) != planet
                || !planet.Components.TryGet(out MyPlanetAreasComponent areas))
                return false;
            var localQuery = Vector3D.Transform(in position, in planet.PositionComp.WorldMatrixInvScaledRef);
            var localCenter = areas.CalculateAreaCenter(AreaId);
            var cosBetween = localQuery.Dot(localCenter) / localQuery.Length() / localCenter.Length();
            var trustedCos = Math.Cos(MathHelper.Sqrt2 * MathHelper.PiOver2 / areas.AreaCount);
            return cosBetween >= trustedCos;
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiAreaMarketHostComponent)base.Serialize(copy);
            ob.AreaId = AreaId;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiAreaMarketHostComponent)builder;
            AreaId = ob.AreaId;
        }

        public override string ToString()
        {
            if (TryGetPlanet(out var planet) && planet.Components.TryGet(out MyPlanetAreasComponent areas))
            {
                areas.UnpackAreaIdToNames(AreaId, out var kingdom, out var region, out var area);
                return $"Area: {kingdom}, {region}, {area}, {PlanetId}";
            }

            MyPlanetAreasComponent.UnpackAreaId(AreaId, out int rawKingdom, out var x, out var y);
            return $"Area: {rawKingdom}, {x}, {y}, {PlanetId}";
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiAreaMarketHostComponent : MyObjectBuilder_EquiPlanetAssociatedMarketHostComponent
    {
        [XmlAttribute("AreaId")]
        public long AreaId;

        internal override string ContainerSubtype => "EquiAreaMarketStorage";
    }
}