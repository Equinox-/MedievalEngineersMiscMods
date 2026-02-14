using System;
using Equinox76561198048419394.Core.Util.EqMath;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Network;
using VRage.Scene;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketManager
    {
        /// <summary>
        /// What type of locational markets are enabled.
        /// </summary>
        public LocationalMarketsMode LocationalMarkets => LocationalMarketsOverride ?? Definition.LocationalMarkets;

        private LocationalMarketsMode? _locationalMarketsOverride;

        /// <summary>
        /// Override the type of locational markets enabled in a single save file.
        /// </summary>
        internal LocationalMarketsMode? LocationalMarketsOverride
        {
            get => _locationalMarketsOverride;
            set
            {
                if (!MyMultiplayerModApi.Static.IsServer) return;
                _locationalMarketsOverride = value;
                MyAPIGateway.Multiplayer?.RaiseEvent(this, a => a.BroadcastChangeLocationalMarketMode, value == null ? -1 : (int)value.Value);
            }
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void BroadcastChangeLocationalMarketMode(int newMode)
        {
            _locationalMarketsOverride = newMode == -1 ? null : (LocationalMarketsMode?)(LocationalMarketsMode)newMode;
        }

        [Event]
        [Reliable]
        [Server]
        private void RequestCreateLocationalMarket(SerializableVector3D location) => TryGetLocationalMarket(location, out _, createIfMissing: true);

        /// <summary>
        /// Tries to get the market corresponding to the given location.
        /// </summary>
        /// <param name="location">location to find a market for</param>
        /// <param name="entity">market storage</param>
        /// <param name="createIfMissing">attempt to create the market if it is missing, this may fail or take a network round trip</param>
        /// <returns>true if the market exists locally</returns>
        public bool TryGetLocationalMarket(in Vector3D location, out MyEntity entity, bool createIfMissing = false)
        {
            entity = null;
            if (TryGetLocationalMarketIdentifiers(in location, out var id, false, out _) && Scene.TryGetEntity(id, out entity))
                return true;
            if (!createIfMissing)
                return false;

            if (!MyMultiplayerModApi.Static.IsServer)
            {
                MyAPIGateway.Multiplayer.RaiseEvent(this, a => a.RequestCreateLocationalMarket, (SerializableVector3D)location);
                return false;
            }

            if (TryGetLocationalMarketIdentifiers(in location, out id, true, out var host))
                entity = CreateMarketStorage(id, host.ContainerSubtype, host);
            return entity != null;
        }

        private bool TryGetLocationalMarketIdentifiers(in Vector3D location, out EntityId id, bool returnHost, out MyObjectBuilder_EquiLocationalMarketHostComponent host)
        {
            id = default;
            host = null;
            switch (LocationalMarkets)
            {
                case LocationalMarketsMode.Disabled: return false;
                case LocationalMarketsMode.Planet:
                {
                    var planet = MyGamePruningStructureSandbox.GetClosestPlanet(location);
                    if (planet == null) return false;
                    const long planetBits = 0x12320fcbea7b48;
                    id = ConstructId(planet, 0, planetBits);
                    host = returnHost ? new MyObjectBuilder_EquiPlanetMarketHostComponent { PlanetId = planet.Id.Value } : null;
                    return true;
                }
                case LocationalMarketsMode.PerKingdom:
                {
                    if (!TryAreaAndLocalLocation(in location, out var areas, out var localLocation)) return false;
                    const long kingdomBits = 0xa673df589378;
                    MyEnvironmentCubemapHelper.ProjectToCube(ref localLocation, out var direction, out _);
                    id = ConstructId(areas.Entity, direction, kingdomBits);
                    host = returnHost ? new MyObjectBuilder_EquiKingdomMarketHostComponent { PlanetId = areas.Entity.Id.Value, KingdomId = direction } : null;
                    return true;
                }
                case LocationalMarketsMode.PerRegion:
                {
                    if (!TryAreaAndLocalLocation(in location, out var areas, out var localLocation)) return false;
                    const long regionBits = 0xd7a15111f067e8;
                    var regionId = areas.GetRegion(localLocation);
                    id = ConstructId(areas.Entity, regionId, regionBits);
                    host = returnHost ? new MyObjectBuilder_EquiRegionMarketHostComponent { PlanetId = areas.Entity.Id.Value, RegionId = regionId } : null;
                    return true;
                }
                case LocationalMarketsMode.PerArea:
                {
                    if (!TryAreaAndLocalLocation(in location, out var areas, out var localLocation)) return false;
                    const long areaBits = 0x698da101999f72;
                    var areaId = areas.GetArea(localLocation);
                    id = ConstructId(areas.Entity, areaId, areaBits);
                    host = returnHost ? new MyObjectBuilder_EquiAreaMarketHostComponent { PlanetId = areas.Entity.Id.Value, AreaId = areaId } : null;
                    return true;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            EntityId ConstructId(MyEntity planet, long locationIdValue, long bits) => MyEntityIdentifier.ConstructId(
                MyEntityIdentifier.ObjectType.Entity, Hashing.Mix64((long)planet.Id.Value) ^ Hashing.Mix64(locationIdValue * 7) ^ bits);
        }

        private bool TryAreaAndLocalLocation(in Vector3D location, out MyPlanetAreasComponent areas, out Vector3D localLocation)
        {
            areas = MyGamePruningStructureSandbox.GetClosestPlanet(location)?.Get<MyPlanetAreasComponent>();
            if (areas == null)
            {
                localLocation = default;
                return false;
            }

            localLocation = Vector3D.Transform(in location, in areas.Entity.PositionComp.WorldMatrixInvScaledRef);
            return true;
        }
    }

    public enum LocationalMarketsMode
    {
        Disabled,
        Planet,
        PerKingdom,
        PerRegion,
        PerArea,
    }
}