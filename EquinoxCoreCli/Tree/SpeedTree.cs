using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using Equinox76561198048419394.Core.Cli.Util.SpeedTree;
using VRage.Collections.Graph;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Tree
{
    public sealed class SpeedTree
    {
        public readonly Dictionary<int, SpeedTreeBranch> BoneToBranch = new Dictionary<int, SpeedTreeBranch>();
        public readonly HashSet<SpeedTreeBranch> Branches = new HashSet<SpeedTreeBranch>();
        public readonly List<SpeedTreeBranch> Roots = new List<SpeedTreeBranch>();

        public KeenModel[] LevelsOfDetail;

        public KeenModel LevelOfDetail(int lod)
        {
            while (LevelsOfDetail == null || LevelsOfDetail.Length <= lod)
                Array.Resize(ref LevelsOfDetail, (LevelsOfDetail?.Length ?? 2) * 2);
            ref var model = ref LevelsOfDetail[lod];
            return model ??= new KeenModel();
        }
    }

    public sealed class SpeedTreeBranch
    {
        public readonly string Name;
        public readonly Dictionary<int, SpeedTreeBranchLod> Lods = new Dictionary<int, SpeedTreeBranchLod>();

        public readonly SpeedTreeBranch Parent;
        public readonly List<SpeedTreeBranch> Children = new List<SpeedTreeBranch>();

        public readonly List<BranchSpinePoint> SpinePoints = new List<BranchSpinePoint>();

        public float Mass
        {
            get
            {
                var mass = 0f;
                foreach (var pt in SpinePoints)
                    mass += pt.Mass;
                return mass;
            }
        }

        public float Length
        {
            get
            {
                var prev = SpinePoints[0].Point;
                var len = 0f;
                for (var i = 1; i < SpinePoints.Count; i++)
                {
                    var curr = SpinePoints[i].Point;
                    len += Vector3.Distance(prev, curr);
                    prev = curr;
                }

                return len;
            }
        }

        public SpeedTreeBranch(string name, SpeedTreeBranch parent)
        {
            Name = name;
            Parent = parent;
        }

        public SpeedTreeBranchLod Lod(int lod)
        {
            if (!Lods.TryGetValue(lod, out var value))
                Lods.Add(lod, value = new SpeedTreeBranchLod(lod));
            return value;
        }

        public override string ToString() =>
            $"{Name}[Points={SpinePoints.Count}, Tri=[{string.Join(", ", Lods.Select(x => x.Key + "=" + x.Value.Triangles))}]]";
    }

    public readonly struct BranchSpinePoint
    {
        public readonly Vector3 Point;
        public readonly float Radius;
        public readonly float Mass;

        public BranchSpinePoint(Vector3 point, float radius, float mass)
        {
            Point = point;
            Radius = radius;
            Mass = mass;
        }

        public static BranchSpinePoint Merge(BranchSpinePoint a, BranchSpinePoint b) => new BranchSpinePoint(
            (a.Point * a.Radius + b.Point * b.Radius) / (a.Radius + b.Radius),
            Math.Max(a.Radius, b.Radius),
            a.Mass + b.Mass);
    }

    public sealed class SpeedTreeBranchLod
    {
        public readonly int LevelOfDetail;
        public readonly Dictionary<string, SpeedTreePrimitive> Primitives = new Dictionary<string, SpeedTreePrimitive>();

        public SpeedTreeBranchLod(int levelOfDetail) => LevelOfDetail = levelOfDetail;

        public SpeedTreePrimitive Primitive(string material)
        {
            if (!Primitives.TryGetValue(material, out var value))
                Primitives.Add(material, value = new SpeedTreePrimitive());
            return value;
        }

        public int Triangles
        {
            get
            {
                var tri = 0;
                foreach (var primitive in Primitives.Values)
                    tri += primitive.Indices.Count / 3;
                return tri;
            }
        }
    }

    public sealed class SpeedTreePrimitive
    {
        public readonly List<int> Indices = new List<int>();
    }

    public static class SpeedTreeExt
    {
        private static readonly Regex LodPattern = new Regex("LOD([0-9]+)", RegexOptions.IgnoreCase);
        private static readonly int[] FlipWindingOrder = { 0, 2, 1 };

        public static bool TryGetDirectLod(this SpeedTreeRawObjectsObject obj, out int lod)
        {
            var match = LodPattern.Match(obj.Name);
            if (match.Success && int.TryParse(match.Groups[1].Value, out lod))
                return true;
            lod = default;
            return false;
        }

        public static void AddFromScene(
            this SpeedTree tree,
            SpeedTreeRaw scene,
            ISpeedTreeOptions options,
            Matrix transform)
        {
            tree.AddBranchesFromScene(options, scene.Bones, ref transform);

            var objects = scene.Objects.Object.ToDictionary(x => x.ID, x => x);

            foreach (var obj in objects.Values)
            {
                var points = obj.Points;
                var vertices = obj.Vertices;
                var triangles = obj.Triangles;
                if (points == null || vertices == null || triangles == null)
                    continue;

                var lodRoot = obj;
                int lodIndex;
                while (true)
                {
                    if (lodRoot.TryGetDirectLod(out lodIndex))
                        break;
                    if (objects.TryGetValue(lodRoot.ParentID, out lodRoot))
                        continue;
                    lodIndex = 0;
                    break;
                }

                var lod = tree.LevelOfDetail(lodIndex);

                var branchHelpers = new Dictionary<SpeedTreeBranch, Dictionary<ulong, int>>();
                for (var i = 0; i < triangles.Count * 3; i += 3)
                {
                    var minBone = int.MaxValue;
                    for (var j = i; j < i + 3; j++)
                    {
                        var bone = vertices.BoneID[triangles.VertexIndices[j]];
                        if (bone < minBone)
                            minBone = bone;
                    }

                    if (!tree.BoneToBranch.TryGetValue(minBone, out var branch))
                        branch = tree.Roots[0];
                    if (!branchHelpers.TryGetValue(branch, out var helper))
                        branchHelpers.Add(branch, helper = new Dictionary<ulong, int>());
                    var primitive = branch.Lod(lodIndex).Primitive(triangles.Material);
                    foreach (var offset in FlipWindingOrder)
                    {
                        var j = i + offset;
                        var pt = triangles.PointIndices[j];
                        var vert = triangles.VertexIndices[j];

                        var key = ((ulong)pt << 32) | (uint)vert;
                        if (!helper.TryGetValue(key, out var index))
                        {
                            var vertex = lod.AllocateVertex();
                            helper.Add(key, index = vertex.Index);
                            vertex.Position = Vector3.Transform(points.Pt(pt), ref transform);
                            vertex.Normal = Vector3.TransformNormal(vertices.Normal(vert), ref transform);
                            vertex.BiTangent = Vector3.TransformNormal(vertices.Binormal(vert), ref transform);
                            vertex.Tangent = Vector3.TransformNormal(vertices.Tangent(vert), ref transform);
                            vertex.TexCoord = vertices.TexCoord(vert);
                        }

                        primitive.Indices.Add(index);
                    }
                }
            }
        }


        private static void AddBranchesFromScene(this SpeedTree tree, ISpeedTreeOptions options, SpeedTreeRawBones bones, ref Matrix transform)
        {
            var indexed = bones.Bone.ToDictionary(x => x.ID, x => x);
            var graph = new AlGraph<SpeedTreeRawBonesBone>();

            var explore = new Queue<(SpeedTreeBranch Parent, SpeedTreeRawBonesBone Bone)>();
            var visited = new HashSet<SpeedTreeRawBonesBone>();

            foreach (var bone in bones.Bone)
                graph.AddVertex(bone);

            foreach (var bone in bones.Bone)
            {
                if (indexed.TryGetValue(bone.ParentID, out var parent))
                {
                    graph.AddEdge(parent, bone);
                    continue;
                }

                explore.Enqueue((null, bone));
                visited.Add(bone);
            }

            // Remove thin bones.
            var thinBoneMapping = RemoveThinBones(graph, visited, options.PhysicsMinRadius);

            // Build fracture islands.
            while (explore.TryDequeue(out var start))
            {
                var branch = new SpeedTreeBranch($"stb_{start.Bone.ID}", start.Parent);
                if (start.Parent != null)
                    start.Parent.Children.Add(branch);
                else
                    tree.Roots.Add(branch);
                tree.Branches.Add(branch);

                var prevEndPoint = new Vector3(0, float.NegativeInfinity, 0);
                var search = start.Bone;
                while (true)
                {
                    var capsuleStart = search.Start;
                    var capsuleEnd = search.End;

                    // Start should be the closest capsule point to the previous capsule's end.
                    if (Vector3.DistanceSquared(prevEndPoint, capsuleEnd) < Vector3.DistanceSquared(prevEndPoint, capsuleStart))
                        MyUtils.Swap(ref capsuleStart, ref capsuleEnd);
                    prevEndPoint = capsuleEnd;

                    var startSpine = new BranchSpinePoint(Vector3.Transform(capsuleStart, ref transform), search.Radius, search.Mass / 2);
                    var endSpine = new BranchSpinePoint(Vector3.Transform(capsuleEnd, ref transform), search.Radius, search.Mass / 2);
                    if (branch.SpinePoints.Count > 0)
                        branch.SpinePoints[branch.SpinePoints.Count - 1] = BranchSpinePoint.Merge(branch.SpinePoints[branch.SpinePoints.Count - 1], startSpine);
                    else
                        branch.SpinePoints.Add(startSpine);
                    branch.SpinePoints.Add(endSpine);

                    tree.BoneToBranch.Add(search.ID, branch);

                    SpeedTreeRawBonesBone next;
                    if (start.Parent != null)
                        // Only extend the branch if it hasn't hit the length limit.
                        next = branch.Length < options.FractureLength ? FindAlignedChild(search) : null;
                    else
                        // Only extend the stump if it hasn't hit the Y cutoff.
                        next = branch.SpinePoints[branch.SpinePoints.Count - 1].Point.Y < options.FractureStumpLength ? FindAlignedChild(search) : null;

                    foreach (var adjacent in graph.GetAdjacentVertices(search))
                        if (adjacent != next && visited.Add(adjacent))
                            explore.Enqueue((branch, adjacent));

                    if (next == null) break;

                    visited.Add(next);
                    search = next;
                }
            }

            // Remap thin bones onto fracture islands.
            foreach (var thin in thinBoneMapping)
                tree.BoneToBranch.Add(thin.Removed, tree.BoneToBranch[thin.Preserved]);
            return;

            SpeedTreeRawBonesBone FindAlignedChild(SpeedTreeRawBonesBone bone)
            {
                var dir = Vector3.Normalize(bone.End - bone.Start);

                SpeedTreeRawBonesBone next = null;
                var nextAngle = options.PhysicsSnapAngle;
                foreach (var adjacent in graph.GetAdjacentVertices(bone))
                {
                    if (visited.Contains(adjacent))
                        continue;
                    var adjacentDir = Vector3.Normalize(adjacent.End - adjacent.Start);
                    var angle = (float)Math.Acos(Math.Abs(adjacentDir.Dot(dir)));
                    if (angle > nextAngle) continue;
                    nextAngle = angle;
                    next = adjacent;
                }

                return next;
            }
        }

        private static List<(int Removed, int Preserved)> RemoveThinBones(AlGraph<SpeedTreeRawBonesBone> graph,
            HashSet<SpeedTreeRawBonesBone> roots,
            float minRadius)
        {
            var removedToPreserved = new List<(int Removed, int Preserved)>();
            using var handle = PoolManager.Get(out List<SpeedTreeRawBonesBone> shrink);
            while (true)
            {
                shrink.Clear();
                foreach (var node in graph.Vertices)
                {
                    if (roots.Contains(node) || node.Radius >= minRadius) continue;
                    var adj = graph.GetAdjacentVertices(node);
                    if (adj.Count != 1)
                        continue;
                    removedToPreserved.Add((node.ID, adj.First().ID));
                    shrink.Add(node);
                }

                if (shrink.Count == 0)
                {
                    removedToPreserved.Reverse();
                    return removedToPreserved;
                }

                foreach (var node in shrink)
                    graph.RemoveVertex(node);
            }
        }
    }
}