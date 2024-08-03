using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.EqMath;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Components.Session;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    public partial class EquiDecorativeMeshComponent
    {
        /// <summary>
        /// Queries for mesh objects that overlap the given ray, up to a given max distance.
        /// </summary>
        public void Query(in Ray ray, List<FeatureHandle> results, float maxDistance = float.PositiveInfinity)
        {
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<ulong>> backingResults))
            {
                if (_dynamicMesh != null)
                {
                    _dynamicMesh.Query(in ray, backingResults, maxDistance);
                    foreach (var meshObject in backingResults)
                        if (_featureByMeshObject.TryGetValue(meshObject.Element, out var key))
                        {
                            var result = new FeatureHandle(this, in key, (float)meshObject.Distance);
                            if (result.IsValid)
                                results.Add(result);
                        }
                }

                if (_dynamicModels != null)
                {
                    backingResults.Clear();
                    _dynamicModels.Query(in ray, backingResults, maxDistance);
                    foreach (var modelObject in backingResults)
                        if (_featureByModelObject.TryGetValue(modelObject.Element, out var key))
                        {
                            var result = new FeatureHandle(this, in key, (float)modelObject.Distance);
                            if (result.IsValid)
                                results.Add(result);
                        }
                }
            }
        }

        public static void QueryWorld(in RayD ray, List<FeatureHandle> results, double maxDistance = double.PositiveInfinity)
        {
            using (PoolManager.Get(out List<MyLineSegmentOverlapResult<MyEntity>> entities))
            {
                // Buffer distance slightly due to hanging ropes.
                var line = ray.AsLine(maxDistance + 10);
                MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref line, entities);
                foreach (var entity in entities)
                    if (entity.Distance < maxDistance && entity.Element.Components.TryGet(out EquiDecorativeMeshComponent decor))
                    {
                        var pos = entity.Element.PositionComp.WorldMatrixNormalizedInv;
                        var fr = Vector3D.Transform(in ray.Position, in pos);
                        var dir = Vector3D.TransformNormal(ray.Direction, ref pos);
                        decor.Query(new Ray((Vector3)fr, (Vector3)dir), results, (float)maxDistance);
                    }
            }
        }

        public static bool QueryNearestInWorld(in LineD line, out FeatureHandle result) =>
            QueryNearestInWorld(new RayD(line.From, line.Direction), out result, line.Length);

        public static bool QueryNearestInWorld(in RayD ray, out FeatureHandle result, double maxDistance = double.PositiveInfinity)
        {
            using (PoolManager.Get(out List<FeatureHandle> hits))
            {
                QueryWorld(in ray, hits, maxDistance);
                if (hits.Count == 0)
                {
                    result = default;
                    return false;
                }

                var best = float.PositiveInfinity;
                var bestI = -1;
                var hitSpan = hits.AsEqSpan();
                for (var i = 0; i < hitSpan.Length; i++)
                {
                    var dist = hitSpan[i].Distance;
                    if (dist >= best) continue;
                    bestI = i;
                    best = dist;
                }
                result = hitSpan[bestI];
                return true;
            }
        } 
    }
}