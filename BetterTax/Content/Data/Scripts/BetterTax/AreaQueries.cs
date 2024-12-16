using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Planet;
using Medieval.GameSystems;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Players;

namespace Equinox76561198048419394.BetterTax
{
    internal static class AreaQueries
    {
        /// <summary>
        /// Collects the areas connected to the given starting area.
        /// </summary>
        /// <param name="ownership">ownership component</param>
        /// <param name="payableAreas">output set</param>
        /// <param name="identity">player identity</param>
        /// <param name="startingArea">starting area ID</param>
        /// <param name="includeFaction">include areas owned by other members in the faction</param>
        internal static void ConnectedPayableAreas(
            MyPlanetAreaOwnershipComponent ownership,
            HashSet<long> payableAreas,
            MyIdentity identity,
            long startingArea,
            bool includeFaction)
        {
            var areas = ownership.Container?.Get<MyPlanetAreasComponent>();
            if (areas == null) return;
            var data = new ConnectedPayableAreasData { Ownership = ownership, Output = payableAreas, IncludeFaction = includeFaction, Identity = identity.Id };
            areas.FloodFillAreas(ref data, (ref ConnectedPayableAreasData userData, long areaId) =>
            {
                if (!IsPayable(userData.Ownership, userData.Identity, areaId, userData.IncludeFaction)) return false;
                userData.Output.Add(areaId);
                return true;
            }, startingArea);
        }

        internal static bool IsPayable(MyPlanetAreaOwnershipComponent ownership, long identity, long area, bool includeFaction)
        {
            var owner = ownership.GetAreaOwner(area);
            if (owner == identity) return true;
            if (!includeFaction) return false;
            var ownerFaction = MyFactionManager.GetPlayerFaction(owner);
            return ownerFaction != null && ownerFaction.IsMember(identity);
        }

        private struct ConnectedPayableAreasData
        {
            public MyPlanetAreaOwnershipComponent Ownership;
            public HashSet<long> Output;
            public long Identity;
            public bool IncludeFaction;
        }
    }
}