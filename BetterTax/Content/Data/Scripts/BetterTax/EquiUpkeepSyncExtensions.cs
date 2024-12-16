using System;
using System.Collections.Generic;
using Medieval.Entities.Components.Planet;
using ObjectBuilders.Components;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Network;

namespace Equinox76561198048419394.BetterTax
{
    [StaticEventOwner]
    internal static class EquiUpkeepSyncExtensions
    {
        internal static void UpdateUpkeep(MyPlanetAreaUpkeepComponent component, Dictionary<long, TimeSpan> newExpiryTime)
        {
            var temp = new Dictionary<long, long>(newExpiryTime.Count);
            foreach (var kv in newExpiryTime)
                temp.Add(kv.Key, kv.Value.Ticks / EquiBetterTaxComponent.Granularity.Ticks);
            UpdateUpkeep_Internal(component, temp);
            MyAPIGateway.Multiplayer?.RaiseEvent(component.Entity, x => x.UpdateUpkeep_Client, temp);
        }

        [Event, Reliable, Broadcast]
        private static void UpdateUpkeep_Client(this MyEntity planetEntity, Dictionary<long, long> newExpirations)
        {
            UpdateUpkeep_Internal(planetEntity.Get<MyPlanetAreaUpkeepComponent>(), newExpirations);
        }

        private const long MarkerRemoveExpiration = -1;

        private static void UpdateUpkeep_Internal(MyPlanetAreaUpkeepComponent component, Dictionary<long, long> newExpirations)
        {
            var ob = (MyObjectBuilder_PlanetAreaUpkeepComponent)component.Serialize();
            if (ob.ExpirationTimes != null)
                for (var i = 0; i < ob.ExpirationTimes.Count; i++)
                {
                    var entry = ob.ExpirationTimes[i];
                    if (!newExpirations.TryGetValue(entry.AreaId, out var newExpiry)) continue;

                    newExpirations.Remove(entry.AreaId);

                    if (newExpiry == MarkerRemoveExpiration)
                    {
                        ob.ExpirationTimes.RemoveAtFast(i);
                        i--;
                        continue;
                    }

                    entry.ExpirationTime = newExpiry;
                    ob.ExpirationTimes[i] = entry;
                }

            foreach (var entry in newExpirations)
            {
                if (entry.Value == MarkerRemoveExpiration) continue;
                ob.ExpirationTimes = ob.ExpirationTimes ?? new List<MyObjectBuilder_PlanetAreaUpkeepComponent.AreaEntry>();
                ob.ExpirationTimes.Add(new MyObjectBuilder_PlanetAreaUpkeepComponent.AreaEntry
                {
                    AreaId = entry.Key,
                    ExpirationTime = entry.Value,
                });
            }

            component.Deserialize(ob);
        }
    }
}