using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class FindFreePlaceUtil
    {
        public static Vector3D? FindFreePlaceImproved(Vector3D pos, Quaternion orientation, Vector3 halfExtents, Vector3? onlyInHemisphere = null)
        {
            if (IsFreePlace(pos, orientation, halfExtents))
                return pos;
            var distanceStepSize = halfExtents.Length() / 10f;
            for (var distanceStep = 1; distanceStep <= 20; distanceStep++)
            {
                var distance = distanceStep * distanceStepSize;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var dir = MyUtils.GetRandomVector3Normalized();
                    if (onlyInHemisphere.HasValue && dir.Dot(onlyInHemisphere.Value) < 0)
                        dir = -dir;
                    var testPos = pos + dir * distance;
                    if (IsFreePlace(testPos, orientation, halfExtents))
                    {
                        var planet = MyPlanets.GetPlanets()[0];
                        var centerVoxData = planet.WorldPositionToStorage(testPos);
                        VoxelData.Resize(new Vector3I(1));
                        planet.Storage.ReadRange(VoxelData, MyStorageDataTypeFlags.Content, 0, centerVoxData, centerVoxData);
                        return testPos;
                    }
                }
            }

            return null;
        }

        public static bool IsFreePlace(Vector3D pos, Quaternion orientation, Vector3 halfExtents)
        {
            return MyEntities.FindFreePlace(pos, orientation, halfExtents, 0, 1, 1, false).HasValue
                   && !IntersectsVoxelSurface(new OrientedBoundingBoxD(pos, halfExtents, orientation));
        }

        private static readonly MyStorageData VoxelData = new MyStorageData(MyStorageDataTypeFlags.Content);

        private static bool IntersectsVoxelSurface(OrientedBoundingBoxD box)
        {
            var data = VoxelData;
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopmostEntitiesInBox(box.GetAABB(), entities, MyEntityQueryType.Static);
                foreach (var ent in entities)
                    if (ent is MyVoxelBase voxel && !(ent is MyVoxelPhysics))
                    {
                        var invWorld = voxel.PositionComp.WorldMatrixInvScaled;
                        var storageBounds = BoundingBoxD.CreateInvalid();
                        var voxelOffset = (voxel.Size >> 1) + voxel.StorageMin;
                        var storageObb = box;
                        storageObb.Transform(invWorld);
                        storageObb.HalfExtent /= voxel.VoxelSize;
                        storageObb.Center = storageObb.Center / voxel.VoxelSize + voxelOffset;
                        storageBounds.Include(storageObb.GetAABB());

                        var storageMin = Vector3I.Max(Vector3I.Floor(storageBounds.Min), voxel.StorageMin);
                        var storageMax = Vector3I.Min(Vector3I.Ceiling(storageBounds.Max), voxel.StorageMax);
                        var localBox = new BoundingBoxI(storageMin, storageMax);
                        localBox.Inflate(1);
                        var floatBox = new BoundingBox(localBox);
                        if (voxel.IntersectStorage(ref floatBox) == ContainmentType.Disjoint)
                            continue;
                        data.Resize(storageMin, storageMax);
                        voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Content, 0, storageMin, storageMax);
                        foreach (var pt in new BoundingBoxI(Vector3I.Zero, storageMax - storageMin).EnumeratePoints())
                        {
                            var voxelBox = new BoundingBoxD(storageMin + pt, storageMin + pt + 1);
                            var containment = storageObb.Contains(ref voxelBox);
                            if (containment == ContainmentType.Disjoint)
                                continue;
                            var tmpPt = pt;
                            var index = data.ComputeLinear(ref tmpPt);
                            var content = data.Content(index);
                            if (containment == ContainmentType.Intersects && content >= 127)
                                return true;
                            if (containment == ContainmentType.Contains && content > 0)
                                return true;
                        }
                    }
            }

            return false;
        }
    }
}