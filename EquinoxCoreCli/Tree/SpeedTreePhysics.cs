using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.Cli.Util.Collider;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using Equinox76561198048419394.Core.Util.Memory;
using Havok;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Tree
{
    public static class SpeedTreePhysics
    {
        public static byte[] CreateRootShape(SpeedTree tree, string name, Func<string, string> materialName)
        {
            using var havok = HavokContext.Create();

            var meshLod = TryFindAcceptableLod(tree);
            var capsules = new Dictionary<SpeedTreeBranch, (HkShape Shape, HkMassProperties Mass)>();
            HkShape listShape = default;
            HkdBreakableShape breakableShape = default;
            try
            {
                var queue = new Queue<SpeedTreeBranch>();
                foreach (var root in tree.Roots)
                    queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    var branch = queue.Dequeue();

                    foreach (var child in branch.Children)
                        queue.Enqueue(child);

                    var mass = branch.HavokMassProperties();
                    HkShape shape;
                    if (branch.SpinePoints.Count == 2)
                    {
                        shape = new HkCapsuleShape(
                            branch.SpinePoints[0].Point,
                            branch.SpinePoints[branch.SpinePoints.Count - 1].Point,
                            branch.SpinePoints[0].Radius);
                    }
                    else
                    {
                        var hull = ConvexHull.CreateFromSpheres(branch.SpinePoints.Select(x => new BoundingSphere(x.Point, x.Radius)));
                        var minRadius = branch.SpinePoints.Min(x => x.Radius);
                        OptimalShapes.Of(hull.Points.AsEqSpan(), out var sphere, out var box, out var capsule);
                        if (sphere.Volume() < hull.Volume * 1.4f)
                            shape = new HkConvexTranslateShape(new HkSphereShape(sphere.Radius), sphere.Center, HkReferencePolicy.TakeOwnership);
                        else if (capsule.Volume() < hull.Volume * 1.3f)
                            shape = new HkCapsuleShape(capsule.P0, capsule.P1, capsule.Radius);
                        else if (box.Volume() < hull.Volume * 1.2f)
                            shape = new HkConvexTransformShape(new HkBoxShape(box.HalfExtent), box.Center, box.Orientation, Vector3.One,
                                HkReferencePolicy.TakeOwnership);
                        else
                            shape = new HkConvexVerticesShape(hull.Points, hull.Points.Length, true, minRadius);
                    }

                    capsules.Add(branch, (shape, mass));
                }

                listShape = new HkListShape(capsules.Values.Select(x => x.Shape).ToArray(), HkReferencePolicy.None);
                var totalMassProperties = HkInertiaTensorComputer.CombineMassProperties(capsules.Values.Select(x => new HkMassElement
                {
                    Properties = x.Mass,
                    Tranform = Matrix.Identity
                }).ToList());

                breakableShape = new HkdBreakableShape(listShape, ref totalMassProperties)
                {
                    Name = $"{name}_physics"
                };
                havok.DestructionStorage.RegisterShapeWithGraphics(new PhysicsMesh(), breakableShape, breakableShape.Name);
                foreach (var child in capsules)
                {
                    var mass = child.Value.Mass;
                    var childShape = new HkdBreakableShape(child.Value.Shape, ref mass)
                    {
                        Name = $"{name}_{child.Key.Name}"
                    };
                    var instance = new HkdShapeInstanceInfo(childShape, Matrix.Identity);
                    TryHighestVisibleLod(child.Key, meshLod, out var branchLod, out var branchMesh);
                    havok.DestructionStorage.RegisterShapeWithGraphics(
                        new PhysicsMesh(tree.LevelsOfDetail[branchLod], branchMesh, materialName),
                        childShape, childShape.Name);
                    breakableShape.AddShape(ref instance);
                }

                return havok.SaveBreakableShapes(breakableShape);
            }
            finally
            {
                foreach (var shape in capsules.Values)
                    shape.Shape.Delete();
                if (breakableShape.IsValid())
                    breakableShape.DeleteOrRemoveRefRecursivelly();
                if (listShape.IsValid)
                    listShape.Delete();
            }
        }

        private static HkMassProperties HavokMassProperties(this SpeedTreeBranch branch)
        {
            var massElements = new List<HkMassElement>(branch.SpinePoints.Count - 1);
            for (var i = 0; i < branch.SpinePoints.Count - 1; i++)
            {
                var from = branch.SpinePoints[0];
                var to = branch.SpinePoints[1];
                var fromMassContribution = i == 0 ? from.Mass : from.Mass / 2;
                var toMassContribution = i == branch.SpinePoints.Count - 2 ? to.Mass : to.Mass / 2;
                massElements.Add(new HkMassElement
                {
                    Tranform = Matrix.Identity,
                    Properties = HkInertiaTensorComputer.ComputeCapsuleVolumeMassProperties(
                        from.Point,
                        to.Point,
                        Math.Max(from.Radius, to.Radius),
                        fromMassContribution + toMassContribution
                    )
                });
            }

            return HkInertiaTensorComputer.CombineMassProperties(massElements);
        }

        private static int TryFindAcceptableLod(SpeedTree tree)
        {
            // Isn't clear why this limit exists, but having more seems to cause issues.
            const int maxTriangles = 5000;

            for (var lod = 0; lod < tree.LevelsOfDetail.Length; lod++)
                if (tree.LevelsOfDetail[lod] != null && IsLodOkay(lod))
                    return lod;
            var msg = "\nAll LODs have branches that can't be stored in havok.\n";
            for (var lod = 0; lod < tree.LevelsOfDetail.Length; lod++)
                if (tree.LevelsOfDetail[lod] != null)
                {
                    var lodCopy = lod;
                    msg += $"LOD {lod}, bad branches {string.Join(", ", tree.Branches.Where(x => !IsBranchLodOkay(x, lodCopy)))}\n";
                }

            throw new Exception(msg);

            bool IsLodOkay(int lod)
            {
                foreach (var branch in tree.Branches)
                    if (!IsBranchLodOkay(branch, lod))
                        return false;
                return true;
            }

            bool IsBranchLodOkay(SpeedTreeBranch branch, int lod)
            {
                return TryHighestVisibleLod(branch, lod, out _, out var mesh) && mesh.Triangles < maxTriangles;
            }
        }

        private static bool TryHighestVisibleLod(SpeedTreeBranch branch, int maxLod, out int branchLod, out SpeedTreeBranchLod lod)
        {
            lod = default;
            for (branchLod = maxLod; branchLod >= 0; branchLod--)
            {
                if (branch.Lods.TryGetValue(branchLod, out lod))
                    return true;
            }

            return false;
        }

        private class PhysicsMesh : IPhysicsMesh
        {
            private readonly List<int> _usedVertices = new List<int>();
            private readonly List<int> _indices = new List<int>();
            private readonly List<(string Material, int IndexStart, int TriCount)> _primitives = new List<(string Material, int IndexStart, int TriCount)>();
            private readonly KeenModel _mesh;

            public PhysicsMesh()
            {
            }

            public PhysicsMesh(KeenModel mesh, SpeedTreeBranchLod lod, Func<string, string> materialName)
            {
                _mesh = mesh;

                var vertexMapping = new Dictionary<int, int>();
                foreach (var primitive in lod.Primitives)
                {
                    var indexOffset = _indices.Count;
                    foreach (var idx in primitive.Value.Indices)
                    {
                        if (!vertexMapping.TryGetValue(idx, out var mapped))
                        {
                            vertexMapping.Add(idx, mapped = _usedVertices.Count);
                            _usedVertices.Add(idx);
                        }

                        _indices.Add(mapped);
                    }

                    _primitives.Add((materialName(primitive.Key), indexOffset, primitive.Value.Indices.Count / 3));
                }
            }

            public void SetAABB(Vector3 min, Vector3 max) => throw new System.NotImplementedException();

            public void AddSectionData(int vertexCount, int indexStart, int triCount, string materialName) => throw new System.NotImplementedException();

            public void AddIndex(int index) => throw new System.NotImplementedException();

            public void AddVertex(Vector3 position, Vector3 normal, Vector3 tangent, Vector2 texCoord) => throw new System.NotImplementedException();

            public int GetSectionsCount() => _primitives.Count;

            public bool GetSectionData(int idx, ref int indexStart, ref int triCount, ref string matIdx)
            {
                if (idx >= _primitives.Count) return false;
                var prim = _primitives[idx];
                indexStart = prim.IndexStart;
                triCount = prim.TriCount;
                matIdx = prim.Material;
                return true;
            }

            public int GetIndicesCount() => _indices.Count;

            public int GetIndex(int idx) => _indices[idx];

            public int GetVerticesCount() => _usedVertices.Count;

            public bool GetVertex(int vertexId, ref Vector3 position, ref Vector3 normal, ref Vector3 tangent, ref Vector2 texCoord)
            {
                if (vertexId >= _usedVertices.Count) return false;
                var vert = _mesh.Vertex(_usedVertices[vertexId]);
                position = vert.Position;
                normal = vert.Normal;
                tangent = vert.Tangent;
                texCoord = vert.TexCoord;
                return true;
            }

            public void Transform(Matrix m) => throw new System.NotImplementedException();
        }
    }
}