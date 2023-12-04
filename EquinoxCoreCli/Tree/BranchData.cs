using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.Cli.Util.Graph;
using Equinox76561198048419394.Core.Cli.Util.Models;
using Equinox76561198048419394.Core.Cli.Util.Spatial;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Tree
{
    public sealed class BranchData
    {
        private const float PointBuffer = 0.01f;

        #region From Attributes

        public void AddCenterPointsFromAttributes(MeshInstance instance, string thicknessAttr)
        {
            var positions = instance.Primitive.FindPositionAccessor()?.AsVector3Array();
            var normals = instance.Primitive.FindNormalAccessor()?.AsVector3Array();
            var thickness = instance.Primitive.FindVertexAccessor(thicknessAttr)?.AsScalarArray();
            if (positions == null || normals == null || thickness == null) return;
            var matrix = instance.Node.WorldMatrix;

            var centers = new Vector3[positions.Count];
            var thicknesses = new float[positions.Count];
            for (var i = 0; i < positions.Count; i++)
            {
                var thick = thickness[i];

                if (thick <= 0)
                    continue;

                var pos = positions[i];
                var normal = normals[i];

                thicknesses[i] = thick * .15f; // what is this magic constant???
                var center = pos - normal * thicknesses[i];
                var worldCenter = System.Numerics.Vector3.Transform(center, matrix);
                centers[i] = worldCenter.ToKeen();
            }

            instance.Primitive.Visit(new AddCentersViaTopology(this, centers, thicknesses));
        }

        public readonly struct AddCentersViaTopology : IPrimitiveVisitor
        {
            private readonly BranchData _owner;
            private readonly Vector3[] _centers;
            private readonly float[] _thicknesses;

            public AddCentersViaTopology(BranchData owner, Vector3[] centers, float[] thicknesses)
            {
                _owner = owner;
                _centers = centers;
                _thicknesses = thicknesses;
            }

            public void Point(uint a) => _owner.AddCenter(_centers[(int)a], 0, _thicknesses[(int)a]);

            public void Line(uint a, uint b)
            {
                ref var ca = ref _centers[(int)a];
                ref var cb = ref _centers[(int)b];
                Vector3.DistanceSquared(ref ca, ref cb, out var dist2);
                var thickness = (_thicknesses[(int)a] + _thicknesses[(int)b]) / 2;
                var ai = _owner.AddCenter(ca, dist2, thickness);
                var bi = _owner.AddCenter(cb, dist2, thickness);
                _owner._centerTopology.AddEdge(ai, bi, 0);
            }

            public void Triangle(uint a, uint b, uint c)
            {
                ref var ca = ref _centers[(int)a];
                ref var cb = ref _centers[(int)b];
                ref var cc = ref _centers[(int)c];
                Vector3.DistanceSquared(ref ca, ref cb, out var dab);
                Vector3.DistanceSquared(ref cb, ref cc, out var dbc);
                Vector3.DistanceSquared(ref cc, ref ca, out var dca);
                var thickness = (_thicknesses[(int)a] + _thicknesses[(int)b] + _thicknesses[(int)c]) / 3;
                var ai = _owner.AddCenter(ca, Math.Max(dab, dca), thickness);
                var bi = _owner.AddCenter(cb, Math.Max(dab, dbc), thickness);
                var ci = _owner.AddCenter(cc, Math.Max(dbc, dca), thickness);

                _owner._centerTopology.AddEdge(ai, bi, 0);
                _owner._centerTopology.AddEdge(bi, ci, 0);
                _owner._centerTopology.AddEdge(ci, ai, 0);
            }
        }

        #endregion

        #region Center Points

        private readonly RTree<CenterPoint> _centers = new RTree<CenterPoint>();
        private readonly Graph<float> _centerTopology = new Graph<float>();

        private uint AddCenter(Vector3 pt, float edgeLengthSquared, float thickness)
        {
            var data = new CenterPoint { Count = 1, Center = pt, EdgeLengthSquared = edgeLengthSquared, Thickness = thickness };
            var query = new AddToClusterQuery { Data = data, Hit = null };
            _centers.Search(ref query);
            if (query.Hit.HasValue)
                return query.Hit.Value;
            var id = _centers.Insert(data);
            _centers.GetLeaf(id).Id = id;
            return id;
        }

        private struct CenterPoint : IBoxBounded
        {
            public uint Id;
            public int Count;
            public Vector3 Center;
            public float EdgeLengthSquared;
            public float Thickness;

            public void GetBounds(ref BoundingBox box)
            {
                box.Min = Center - PointBuffer;
                box.Max = Center + PointBuffer;
            }
        }

        private struct AddToClusterQuery : ISpatialQuery<BoundingBox, CenterPoint>
        {
            public CenterPoint Data;
            public uint? Hit;
            public uint RootFlags => 0;

            private bool Test(ref BoundingBox box)
            {
                box.Contains(ref Data.Center, out var type);
                return type != ContainmentType.Disjoint;
            }

            public NodeQueryResult VisitNode(ref BoundingBox box, ref uint flags)
            {
                if (Hit.HasValue)
                    return NodeQueryResult.Terminate;
                return Test(ref box) ? NodeQueryResult.VisitChildren : NodeQueryResult.SkipChildren;
            }

            public LeafQueryResult VisitLeaf(ref BoundingBox box, ref CenterPoint leafData, uint parentQueryFlags)
            {
                if (Hit.HasValue)
                    return LeafQueryResult.Terminate;
                if (!Test(ref box))
                    return LeafQueryResult.Continue;
                var count = leafData.Count;
                leafData.Center.Multiply(count);
                leafData.Center.Add(Data.Center);
                leafData.Center.Divide(count + 1);
                leafData.Count = count + 1;
                if (Data.EdgeLengthSquared > leafData.EdgeLengthSquared)
                    leafData.EdgeLengthSquared = Data.EdgeLengthSquared;
                if (Data.Thickness > leafData.Thickness)
                    leafData.Thickness = Data.Thickness;
                Hit = leafData.Id;
                return LeafQueryResult.Terminate;
            }
        }

        #endregion

        #region Graph Building

        public ref readonly Vector3 CenterPointLocation(uint id) => ref _centers.GetLeaf(id).Center;

        public float Thickness(uint id) => _centers.GetLeaf(id).Thickness;

        private const float NonTopologyDisadvantage = 1;
        private const int NearestNeighborsPointLimit = 10;
        private const int NearestNeighborsNonPointLimit = 32;
        private const float NearestNeighborDistanceMultiplier = 1.25f;

        public CondensedGraph<float> CreateCenterPointGraph(float minBranchLength, float minThickness)
        {
            var mst = new MstCalculator(deduplicateEdges: true);
            // Start with all the topology edges.  They are higher priority (lower weight)
            foreach (var edge in _centerTopology.Edges)
            {
                var dist = Vector3.Distance(CenterPointLocation(edge.Node1), CenterPointLocation(edge.Node2));
                mst.AddEdge(edge.Node1, edge.Node2, dist);
            }

            // Extend with nearest-nodes query
            var args = new BuildGraphArgs(this, mst);
            var query = new BuildGraphQuery(args);
            _centers.Search(ref query);
            var condensedGraph = mst.ComputeAndReset(new CondensedGraph<float>());

            var minBranchLengthSquared = minBranchLength * minBranchLength;

            using (PoolManager.Get(out List<Edge<CondensedGraph<float>.CondensedEdgeView>> shortEdges))
            {
                while (true)
                {
                    shortEdges.Clear();
                    var condensed = condensedGraph.Condensed;
                    foreach (var edge in condensed.Edges)
                    {
                        // Only drop leaf edges.
                        if (condensed.Neighbors(edge.Node1).Count != 1 && condensed.Neighbors(edge.Node2).Count != 1)
                            continue;
                        var dist2 = Vector3.DistanceSquared(CenterPointLocation(edge.Node1), CenterPointLocation(edge.Node2));
                        if (dist2 <= minBranchLengthSquared || EverythingTooThin(edge, minThickness))
                            shortEdges.Add(edge);
                    }

                    if (shortEdges.Count == 0)
                        break;

                    foreach (var edge in shortEdges)
                        condensedGraph.RemoveCondensedEdge(edge);
                }
            }

            return condensedGraph;
        }

        private bool EverythingTooThin(Edge<CondensedGraph<float>.CondensedEdgeView> edge, float minThickness)
        {
            if (Thickness(edge.Node1) >= minThickness)
                return false;
            foreach (var node in edge.Data)
                if (Thickness(node.Node) >= minThickness)
                    return false;
            return true;
        }

        private readonly struct BuildGraphArgs
        {
            public readonly BranchData Owner;
            public readonly IEdgeSink<float> Target;

            public BuildGraphArgs(BranchData owner, IEdgeSink<float> target)
            {
                Owner = owner;
                Target = target;
            }
        }

        private readonly struct BuildGraphQuery : ISpatialQuery<BoundingBox, CenterPoint>
        {
            private readonly BuildGraphArgs _args;

            public BuildGraphQuery(BuildGraphArgs args) => _args = args;

            public uint RootFlags => 0;
            public NodeQueryResult VisitNode(ref BoundingBox box, ref uint flags) => NodeQueryResult.VisitChildren;

            public LeafQueryResult VisitLeaf(ref BoundingBox box, ref CenterPoint leafData, uint parentQueryFlags)
            {
                var sortedQuery = new CollectNeighborsQuery(in _args, in leafData);
                _args.Owner._centers.SearchSorted(ref sortedQuery);
                return LeafQueryResult.Continue;
            }
        }

        private struct CollectNeighborsQuery : ISpatialSortedQuery<BoundingBox, CenterPoint>
        {
            private readonly BuildGraphArgs _args;
            private readonly CenterPoint _center;
            private readonly float _maxDistanceSquared;
            private int _remaining;

            public CollectNeighborsQuery(in BuildGraphArgs args, in CenterPoint center)
            {
                _args = args;
                _center = center;
                if (center.EdgeLengthSquared > 0)
                {
                    _maxDistanceSquared = center.EdgeLengthSquared * NearestNeighborDistanceMultiplier * NearestNeighborDistanceMultiplier;
                    _remaining = NearestNeighborsNonPointLimit;
                }
                else
                {
                    _maxDistanceSquared = float.PositiveInfinity;
                    _remaining = NearestNeighborsPointLimit;
                }
            }

            public uint RootFlags => 0;

            public NodeQueryResult VisitNode(ref BoundingBox box, ref uint flags, out float score)
            {
                score = box.DistanceSquared(_center.Center);
                return score <= _maxDistanceSquared ? NodeQueryResult.VisitChildren : NodeQueryResult.SkipChildren;
            }

            public LeafUnsortedQueryResult VisitLeafUnsorted(ref BoundingBox box, ref CenterPoint leafData, ref uint flags, out float score)
            {
                if (leafData.Id == _center.Id)
                {
                    score = default;
                    return LeafUnsortedQueryResult.SkipLeaf;
                }

                score = Vector3.DistanceSquared(leafData.Center, _center.Center);
                return score <= _maxDistanceSquared ? LeafUnsortedQueryResult.VisitLeaf : LeafUnsortedQueryResult.SkipLeaf;
            }

            public LeafQueryResult VisitLeafSorted(ref CenterPoint leafData, uint leafQueryFlags, float score)
            {
                var ai = _center.Id;
                var bi = leafData.Id;
                _args.Target.AddEdge(ai, bi, NonTopologyDisadvantage + (float)Math.Sqrt(score));
                --_remaining;
                return _remaining <= 0 ? LeafQueryResult.Terminate : LeafQueryResult.Continue;
            }
        }

        #endregion
    }
}