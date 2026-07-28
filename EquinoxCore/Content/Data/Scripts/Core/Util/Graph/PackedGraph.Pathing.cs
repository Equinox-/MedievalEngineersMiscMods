using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Columnar;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Collections;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.Graph
{
    #region A* Heuristics

    public interface IPathHeuristic
    {
        void EdgeAndHeuristicCost(PackedGraph graph, NodeIndex from, NodeIndex to, out float edgeCost, out float heuristicCost);
    }

    public readonly struct NoHeuristic : IPathHeuristic
    {
        public void EdgeAndHeuristicCost(PackedGraph graph, NodeIndex from, NodeIndex to, out float edgeCost, out float heuristicCost)
            => throw new NotImplementedException();
    }

    public struct Vec3ManhattanHeuristic : IPathHeuristic
    {
        private readonly ColumnReference<NodeIndex, Vector3> _position;
        private Vector3 _target;

        public Vec3ManhattanHeuristic(ColumnReference<NodeIndex, Vector3> position, Vector3 target)
        {
            _position = position;
            _target = target;
        }

        public void EdgeAndHeuristicCost(PackedGraph graph, NodeIndex from, NodeIndex to, out float edgeCost, out float heuristicCost)
        {
            ref var fromPos = ref _position.Access(graph.Nodes, from);
            ref var toPos = ref _position.Access(graph.Nodes, to);
            edgeCost = Vector3.RectangularDistance(ref fromPos, ref toPos);
            heuristicCost = Vector3.RectangularDistance(ref toPos, ref _target);
        }
    }

    #endregion

    public static class PackedGraphPathing
    {
        #region Traversal

        public interface ITraversal : IDisposable
        {
            bool IsDone { get; }
            void Tick<T>(ref T condition) where T : PackedGraphConditions.IGraph;
        }

        public static class TraversalUtils
        {
            public const uint Seed = uint.MaxValue;
            public const uint TraversalFilterRejected = uint.MaxValue - 1;
        }

        public struct DijkstraTraversal : ITraversal
        {
            private readonly PackedGraph _graph;
            private Dictionary<uint, uint> _visitedToIncoming;
            private Queue<uint> _pending;

            public DijkstraTraversal(PackedGraph graph, NodeIndex seed)
            {
                _graph = graph;
                PoolManager.Get(out _visitedToIncoming);
                PoolManager.Get(out _pending);
                var rawSeed = (uint)seed;
                _visitedToIncoming.Add(rawSeed, TraversalUtils.Seed);
                _pending.Enqueue(rawSeed);
            }

            public bool IsDone => _pending.Count == 0;

            public bool HasVisited(NodeIndex ix)
                => _visitedToIncoming.TryGetValue((uint)ix, out var origin) && origin != TraversalUtils.TraversalFilterRejected;

            public void Tick<T>(ref T condition) where T : PackedGraphConditions.IGraph
            {
                if (_pending.Count == 0) return;
                var rawOrigin = _pending.Dequeue();
                var origin = (NodeIndex)rawOrigin;
                foreach (var neighbors in _graph.Neighbors(origin))
                foreach (var neighbor in neighbors)
                {
                    if (!condition.TestEdge(_graph, neighbor.EdgeIndex)) continue;
                    var rawNeighbor = (uint)neighbor.NodeIndex;
                    if (_visitedToIncoming.ContainsKey(rawNeighbor)) continue;
                    if (!condition.TestNode(_graph, neighbor.NodeIndex))
                    {
                        _visitedToIncoming.Add(rawNeighbor, TraversalUtils.TraversalFilterRejected);
                        continue;
                    }

                    _visitedToIncoming.Add(rawNeighbor, rawOrigin);
                    _pending.Enqueue(rawNeighbor);
                }
            }

            public void Dispose()
            {
                PoolManager.Return(ref _pending);
                PoolManager.Return(ref _visitedToIncoming);
            }
        }

        public struct AStarTraversal<THeuristic> : ITraversal where THeuristic : IPathHeuristic
        {
            private readonly PackedGraph _graph;
            private THeuristic _heuristic;
            private Dictionary<uint, (uint Incoming, float CostHere)> _visitedToIncoming;
            private MyBinaryHeap<float, (uint Node, float CostHere)> _pendingAStar;
            private Queue<uint> _pendingDijkstra;

            public AStarTraversal(PackedGraph graph, NodeIndex seed, in THeuristic heuristic)
            {
                _graph = graph;
                _heuristic = heuristic;
                PoolManager.Get(out _visitedToIncoming);
                _pendingAStar = BinaryHeapPool<float, (uint, float)>.Get();
                _pendingDijkstra = null;
                var rawSeed = (uint)seed;
                _visitedToIncoming.Add(rawSeed, (TraversalUtils.Seed, 0));
                _heuristic.EdgeAndHeuristicCost(graph, seed, seed, out _, out var heuristicCost);
                _pendingAStar.Insert((rawSeed, 0), heuristicCost);
            }

            public void SwitchToDijkstra()
            {
                PoolManager.Get(out _pendingDijkstra);
                while (_pendingAStar.Count > 0)
                {
                    var (rawOrigin, costHere) = _pendingAStar.RemoveMin();
                    // If the item was re-inserted into the queue with a lower cost, don't bother processing this item.
                    if (_visitedToIncoming[rawOrigin].CostHere < costHere) continue;
                    _pendingDijkstra.Enqueue(rawOrigin);
                }

                BinaryHeapPool<float, (uint, float)>.Return(ref _pendingAStar);
            }

            public bool IsDone => (_pendingAStar?.Count ?? _pendingDijkstra.Count) == 0;

            public bool HasVisited(NodeIndex ix) => _visitedToIncoming.TryGetValue((uint)ix, out var origin)
                                                    && origin.Incoming != TraversalUtils.TraversalFilterRejected;

            public void Tick<T>(ref T condition) where T : PackedGraphConditions.IGraph
            {
                if (_pendingAStar != null)
                    TickAStar(ref condition);
                else
                    TickDijkstra(ref condition);
            }

            private void TickAStar<T>(ref T condition) where T : PackedGraphConditions.IGraph
            {
                if (_pendingAStar.Count == 0) return;
                var (rawOrigin, costHere) = _pendingAStar.RemoveMin();
                // If the item was re-inserted into the queue with a lower cost, don't bother processing this item.
                if (_visitedToIncoming[rawOrigin].CostHere < costHere) return;
                var origin = (NodeIndex)rawOrigin;
                foreach (var neighbors in _graph.Neighbors(origin))
                foreach (var neighbor in neighbors)
                {
                    if (!condition.TestEdge(_graph, neighbor.EdgeIndex)) continue;
                    var rawNeighbor = (uint)neighbor.NodeIndex;
                    float costToNeighbor;
                    float heuristicToEnd;
                    if (_visitedToIncoming.TryGetValue(rawNeighbor, out var existing))
                    {
                        // Already examined and rejected from the filter; no need to check again.
                        if (existing.Incoming == TraversalUtils.TraversalFilterRejected) continue;
                        _heuristic.EdgeAndHeuristicCost(_graph, origin, neighbor.NodeIndex, out var cost, out heuristicToEnd);
                        costToNeighbor = costHere + cost;
                        // If the cost isn't reduced there's no need to re-visit the neighbor.
                        if (existing.CostHere <= costToNeighbor) continue;
                    }
                    else
                    {
                        // Not already examined, so check the filter first.
                        if (!condition.TestNode(_graph, neighbor.NodeIndex))
                        {
                            _visitedToIncoming.Add(rawNeighbor, (TraversalUtils.TraversalFilterRejected, float.PositiveInfinity));
                            continue;
                        }

                        _heuristic.EdgeAndHeuristicCost(_graph, origin, neighbor.NodeIndex, out var cost, out heuristicToEnd);
                        costToNeighbor = costHere + cost;
                    }

                    _visitedToIncoming[rawNeighbor] = (rawOrigin, costToNeighbor);
                    _pendingAStar.Insert((rawNeighbor, costToNeighbor), costToNeighbor + heuristicToEnd);
                }
            }

            private void TickDijkstra<T>(ref T condition) where T : PackedGraphConditions.IGraph
            {
                if (_pendingDijkstra.Count == 0) return;
                var rawOrigin = _pendingDijkstra.Dequeue();
                var origin = (NodeIndex)rawOrigin;
                foreach (var neighbors in _graph.Neighbors(origin))
                foreach (var neighbor in neighbors)
                {
                    if (!condition.TestEdge(_graph, neighbor.EdgeIndex)) continue;
                    var rawNeighbor = (uint)neighbor.NodeIndex;
                    if (_visitedToIncoming.ContainsKey(rawNeighbor)) continue;
                    if (!condition.TestNode(_graph, neighbor.NodeIndex))
                    {
                        _visitedToIncoming.Add(rawNeighbor, (TraversalUtils.TraversalFilterRejected, float.PositiveInfinity));
                        continue;
                    }

                    _visitedToIncoming.Add(rawNeighbor, (rawOrigin, float.PositiveInfinity));
                    _pendingDijkstra.Enqueue(rawNeighbor);
                }
            }

            public void Dispose()
            {
                BinaryHeapPool<float, (uint, float)>.Return(ref _pendingAStar);
                PoolManager.Return(ref _pendingDijkstra);
                PoolManager.Return(ref _visitedToIncoming);
            }
        }

        #endregion
    }
}