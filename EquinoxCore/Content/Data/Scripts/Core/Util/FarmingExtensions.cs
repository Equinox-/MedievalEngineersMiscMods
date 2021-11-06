using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.WorldEnvironment;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class FarmingExtensions
    {
        public static void DisableFarmingItemsIn(this MyVoxelBase voxel, OrientedBoundingBoxD worldObb)
        {
            if (voxel.Hierarchy == null) return;
            var worldAabb = worldObb.GetAABB();
            using (PoolManager.Get(out List<MyEntity> working))
            {
                voxel.Hierarchy.QueryBounds(in worldAabb, working);
                foreach (var entity in working)
                {
                    var sector = entity as MyEnvironmentSector;
                    if (sector == null) continue;
                    var tmp = worldObb;
                    sector.DisableItemsInObb(ref tmp);
                }
            }
        }

        public static void DisableFarmingItemsIn(this MyVoxelBase voxel, BoundingBoxD worldAabb)
        {
            if (voxel.Hierarchy == null) return;
            using (PoolManager.Get(out List<MyEntity> working))
            {
                voxel.Hierarchy.QueryBounds(in worldAabb, working);
                foreach (var entity in working)
                {
                    var sector = entity as MyEnvironmentSector;
                    if (sector == null) continue;
                    var tmp = worldAabb;
                    sector.DisableItemsInAabb(ref tmp);
                }
            }
        }

        public static void DisableItemsIn(OrientedBoundingBoxD box)
        {
            var worldBox = box.GetAABB();
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyEntities.GetTopMostEntitiesInBox(ref worldBox, entities);
                foreach (var entity in entities)
                    (entity as MyVoxelBase)?.DisableFarmingItemsIn(box);
            }
        }
    }
}