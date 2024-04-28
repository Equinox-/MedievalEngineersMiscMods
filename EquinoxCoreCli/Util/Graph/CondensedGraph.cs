using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public sealed class CondensedGraph<TEdgeData> : IGraph<TEdgeData> where TEdgeData : struct
    {
        // A condensed graph is defined as a graph with no nodes that contain only two edges.
        // Effectively this condenses down simple line segments of an original graph into a single edge.

        // Condensed nodes are defined as nodes with 1 or 3+ neighbors.
        // Vapor nodes are defined as nodes with exactly two neighbors.
        // Condensed edges consist of a sequence of vapor edges.
        // Vapor edges consist of edge data.

        // Vapor data.
        private readonly Dictionary<uint, VaporNode> _vaporNodes = new Dictionary<uint, VaporNode>();

        // Condensed data.
        private readonly PagedFreeList<CondensedNode> _condensedNodeData = new PagedFreeList<CondensedNode>();
        private readonly Dictionary<uint, uint> _condensedNodes = new Dictionary<uint, uint>();

        // Edge data.
        private readonly Dictionary<PackedEdge, TEdgeData> _edgeData = new Dictionary<PackedEdge, TEdgeData>();

        private struct CondensedNode
        {
            public uint NeighborCount;
            public StackArray<CondensedNeighbor> Neighbors;

            public bool TryFindNeighbor(uint outgoingVapor, out uint index)
            {
                for (index = 0u; index < NeighborCount; index++)
                {
                    ref var slot = ref Neighbors.Ref(index);
                    if (slot.OutgoingVapor == outgoingVapor)
                        return true;
                }
                return false;
            }

            public bool TryRemoveNeighbor(uint outgoingVapor)
            {
                if (!TryFindNeighbor(outgoingVapor, out var index))
                    return false;
                RemoveNeighborIndex(index);
                return true;
            }

            public void RemoveNeighborIndex(uint index)
            {
                ref var slot = ref Neighbors.Ref(index);
                --NeighborCount;
                if (NeighborCount > 0)
                    slot = Neighbors.Ref(NeighborCount);
            }
        }

        private struct CondensedNeighbor
        {
            // The vapor node outgoing towards condensed.
            public uint OutgoingVapor;

            // The vapor node incoming towards condensed.
            public uint IncomingVapor;

            // Condensed neighbor node ID. 
            public uint Condensed;
        }

        private struct VaporNode
        {
            public uint Left;
            public uint Right;

            public uint Opposing(uint neighbor)
            {
                if (Left == neighbor)
                    return Right;
                if (Right == neighbor)
                    return Left;
                throw new Exception("Invalid vapor node in chain");
            }
        }

        private static ref CondensedNeighbor FindWithOutgoingVapor(ref CondensedNode node, uint vapor)
        {
            if (node.TryFindNeighbor(vapor, out var index))
                return ref node.Neighbors.Ref(index);
            throw new Exception("Failed to find neighbor");
        }

        private ref CondensedNeighbor FlippedEdge(ref CondensedNeighbor neighbor)
        {
            ref var other = ref _condensedNodeData[_condensedNodes[neighbor.Condensed]];
            return ref FindWithOutgoingVapor(ref other, neighbor.IncomingVapor);
        }

        private uint CondenseNode(uint midId, in VaporNode node)
        {
            var condensedIndex = _condensedNodeData.AllocateIndex();
            _condensedNodes.Add(midId, condensedIndex);
            _vaporNodes.Remove(midId);

            ref var leftToMid = ref FindCondensed(node.Left, midId, out var leftId);
            ref var rightToMid = ref FindCondensed(node.Right, midId, out var rightId);

            ref var midCondensed = ref _condensedNodeData[condensedIndex];
            midCondensed.NeighborCount = 2;

            ref var midToLeft = ref midCondensed.Neighbors.Ref(0);
            ref var midToRight = ref midCondensed.Neighbors.Ref(1);

            leftToMid.IncomingVapor = node.Left;
            leftToMid.Condensed = midId;
            midToLeft.OutgoingVapor = node.Left;
            midToLeft.IncomingVapor = leftToMid.OutgoingVapor;
            midToLeft.Condensed = leftId;

            rightToMid.IncomingVapor = node.Right;
            rightToMid.Condensed = midId;
            midToRight.OutgoingVapor = node.Right;
            midToRight.IncomingVapor = rightToMid.OutgoingVapor;
            midToRight.Condensed = rightId;
            return condensedIndex;
        }

        private ref CondensedNeighbor FindCondensed(uint current, uint prev, out uint condensed)
        {
            while (true)
            {
                if (_condensedNodes.TryGetValue(current, out var currentIndex))
                {
                    ref var curr = ref _condensedNodeData[currentIndex];
                    condensed = current;
                    return ref FindWithOutgoingVapor(ref curr, prev);
                }

                var vapor = _vaporNodes[current];
                var next = vapor.Opposing(prev);

                prev = current;
                current = next;
            }
        }

        public void AddEdge(uint a, uint b, TEdgeData data)
        {
            AddEdgeInternal(a, b, data);
            Check();
        }

        private void AddEdgeInternal(uint a, uint b, TEdgeData data)
        {
            PackedEdge.Primary(a, b, out var primaryEdge);
            _edgeData.Add(primaryEdge, data);

            if (_vaporNodes.TryGetValue(a, out var vaporANode))
                CondenseNode(a, in vaporANode);

            if (_vaporNodes.TryGetValue(b, out var vaporBNode))
                CondenseNode(b, in vaporBNode);

            if (!_condensedNodes.TryGetValue(a, out var condensedAIndex))
            {
                condensedAIndex = _condensedNodeData.AllocateIndex();
                _condensedNodeData[condensedAIndex].NeighborCount = 0;
                _condensedNodes.Add(a, condensedAIndex);
            }

            if (!_condensedNodes.TryGetValue(b, out var condensedBIndex))
            {
                condensedBIndex = _condensedNodeData.AllocateIndex();
                _condensedNodeData[condensedBIndex].NeighborCount = 0;
                _condensedNodes.Add(b, condensedBIndex);
            }

            ref var condensedA = ref _condensedNodeData[condensedAIndex];
            ref var condensedB = ref _condensedNodeData[condensedBIndex];

            if (condensedA.NeighborCount == 1 && condensedB.NeighborCount == 1)
            {
                VaporizeTwo(a, b, ref condensedA, ref condensedB);
                return;
            }

            if (condensedA.NeighborCount == 1)
            {
                VaporizeOne(ref condensedA, ref condensedB, a, b);
                return;
            }

            if (condensedB.NeighborCount == 1)
            {
                VaporizeOne(ref condensedB, ref condensedA, b, a);
                return;
            }

            ref var aToB = ref condensedA.Neighbors.Ref(condensedA.NeighborCount++, true);
            aToB.Condensed = aToB.OutgoingVapor = b;
            aToB.IncomingVapor = a;

            ref var bToA = ref condensedB.Neighbors.Ref(condensedB.NeighborCount++, true);
            bToA.Condensed = bToA.OutgoingVapor = a;
            bToA.IncomingVapor = b;
        }

        /// <summary>
        /// Converts two condensed nodes into vapor nodes by linking them together.
        /// </summary>
        private void VaporizeTwo(uint a, uint b, ref CondensedNode condensedA, ref CondensedNode condensedB)
        {
            System.Diagnostics.Debug.Assert(condensedA.NeighborCount == 1, "Can only vaporize single neighbor nodes");
            System.Diagnostics.Debug.Assert(condensedB.NeighborCount == 1, "Can only vaporize single neighbor nodes");

            // Vaporize both condensed nodes.
            ref var aToLeft = ref condensedA.Neighbors.Ref(0);
            ref var leftToA = ref FlippedEdge(ref aToLeft);

            ref var bToRight = ref condensedB.Neighbors.Ref(0);
            ref var rightToB = ref FlippedEdge(ref bToRight);

            // Update the condensed edge to skip over A & B.
            leftToA.Condensed = bToRight.Condensed;
            leftToA.IncomingVapor = bToRight.IncomingVapor;

            rightToB.Condensed = aToLeft.Condensed;
            rightToB.IncomingVapor = aToLeft.IncomingVapor;

            // Replace A with a vapor node.
            _vaporNodes.Add(a, new VaporNode { Left = aToLeft.OutgoingVapor, Right = b });
            _condensedNodeData.Free(_condensedNodes[a]);
            _condensedNodes.Remove(a);

            // Replace B with a vapor node.
            _vaporNodes.Add(b, new VaporNode { Left = a, Right = bToRight.OutgoingVapor });
            _condensedNodeData.Free(_condensedNodes[b]);
            _condensedNodes.Remove(b);
        }

        /// <summary>
        /// Converts the condensed node "vaporize" into a vapor node by linking it with the condensed node "preserve".
        /// </summary>
        private void VaporizeOne(ref CondensedNode vaporize, ref CondensedNode preserve, uint vaporizeId, uint preserveId)
        {
            System.Diagnostics.Debug.Assert(vaporize.NeighborCount == 1, "Can only vaporize single neighbor nodes");
            System.Diagnostics.Debug.Assert(preserve.NeighborCount != 1, "Preserved node must not have a single neighbor");

            ref var vaporizedToOther = ref vaporize.Neighbors.Ref(0);
            ref var otherToVaporized = ref FlippedEdge(ref vaporizedToOther);

            // Update the condensed edge to skip over the vaporized node..
            otherToVaporized.IncomingVapor = vaporizeId;
            otherToVaporized.Condensed = preserveId;

            // Add a condensed edge from the preserved node to other.
            ref var bToLeft = ref preserve.Neighbors.Ref(preserve.NeighborCount++);
            bToLeft.OutgoingVapor = vaporizeId;
            bToLeft.IncomingVapor = vaporizedToOther.IncomingVapor;
            bToLeft.Condensed = vaporizedToOther.Condensed;

            // Replace with a vapor node.
            _vaporNodes.Add(vaporizeId, new VaporNode { Left = vaporizedToOther.OutgoingVapor, Right = preserveId });
            _condensedNodeData.Free(_condensedNodes[vaporizeId]);
            _condensedNodes.Remove(vaporizeId);
        }

        /// <summary>
        /// Gets a condensed node with the given ID. If the node is a vapor node it is converted to a condensed node if it has ifNeighbor for a neighbor.
        /// </summary>
        private bool TryGetOrCondense(uint id, uint ifNeighbor, out uint condensedIndex)
        {
            if (_condensedNodes.TryGetValue(id, out condensedIndex))
                return true;
            if (!_vaporNodes.TryGetValue(id, out var vapor))
                return false;
            if (vapor.Left != ifNeighbor && vapor.Right != ifNeighbor)
                return false;
            // Convert into a condensed node.
            condensedIndex = CondenseNode(id, in vapor);
            return true;
        }

        public void RemoveEdge(uint a, uint b)
        {
            RemoveEdgeInternal(a, b);
            Check();
        }

        private void RemoveEdgeInternal(uint a, uint b)
        {
            // Condensed node with 1 neighbor -> deleted
            // Vapor node -> condensed node with 1 neighbor
            // Condensed node with 3 neighbors -> vapor node
            // Condensed node with 4+ neighbors -> condensed node

            // Promote vapor nodes into condensed nodes if they contain this edge.
            if (!TryGetOrCondense(a, b, out var condensedAIndex))
                return;

            if (!TryGetOrCondense(b, a, out var condensedBIndex))
                return;

            ref var condensedA = ref _condensedNodeData[condensedAIndex];
            ref var condensedB = ref _condensedNodeData[condensedBIndex];

            // Drop the edge from the condensed nodes.
            var removedFromA = condensedA.TryRemoveNeighbor(b);
            var removedFromB = condensedB.TryRemoveNeighbor(a);
            System.Diagnostics.Debug.Assert(removedFromA == removedFromB, "Removed link from one condensed neighbor but not the other");

            // Repair condensed nodes.
            RepairCondensed(ref condensedA, a);
            RepairCondensed(ref condensedB, b);
        }

        private void RepairCondensed(ref CondensedNode node, uint mid)
        {
            if (node.NeighborCount == 2)
            {
                // Vaporize the node.
                ref var midToLeft = ref node.Neighbors.Ref(0);
                ref var midToRight = ref node.Neighbors.Ref(1);

                ref var leftToMid = ref FlippedEdge(ref midToLeft);
                ref var rightToMid = ref FlippedEdge(ref midToRight);

                leftToMid.Condensed = midToRight.Condensed;
                leftToMid.IncomingVapor = midToRight.IncomingVapor;

                rightToMid.Condensed = midToLeft.Condensed;
                rightToMid.IncomingVapor = midToLeft.IncomingVapor;

                _vaporNodes.Add(mid, new VaporNode { Left = midToLeft.OutgoingVapor, Right = midToRight.OutgoingVapor });
            }
            else if (node.NeighborCount != 0)
                return;

            _condensedNodeData.Free(_condensedNodes[mid]);
            _condensedNodes.Remove(mid);
        }

        public void RemoveCondensedEdge(Edge<CondensedEdgeView> edge)
        {
            RemoveCondensedEdge(edge.Node1, edge.Node2, edge.Data);
        }

        public void RemoveCondensedEdge(uint node1, Neighbor<CondensedEdgeView> neighbor)
        {
            RemoveCondensedEdge(node1, neighbor.Node, neighbor.Data);
        }

        public void RemoveCondensedEdge(uint node1, uint node2, CondensedEdgeView view)
        {
            // Start and end points MUST be condensed, otherwise this will remove too much.
            if (_vaporNodes.TryGetValue(node1, out var vapor1))
                CondenseNode(node1, in vapor1);
            if (_vaporNodes.TryGetValue(node2, out var vapor2))
                CondenseNode(node2, in vapor2);
            
            // Now that the endpoints are condensed, we can remove the chain between them.
            RemoveCondensedEdgeInternal(view);

            Check();
        }

        private void RemoveCondensedEdgeInternal(CondensedEdgeView edge)
        {
            if (!_condensedNodes.TryGetValue(edge.StartingNode, out var startIndex))
                return;
            ref var start = ref _condensedNodeData[startIndex];
            if (!start.TryFindNeighbor(edge.OutgoingVapor, out var startNeighborIndex))
                return;

            var prev = edge.StartingNode;
            var curr = edge.OutgoingVapor;
            uint endIndex;
            while (true)
            {
                PackedEdge.Primary(prev, curr, out var key);
                _edgeData.Remove(key);

                if (_condensedNodes.TryGetValue(curr, out endIndex))
                    break;

                var next = _vaporNodes[curr].Opposing(prev);
                _vaporNodes.Remove(curr);
                prev = curr;
                curr = next;
            }
            
            ref var end = ref _condensedNodeData[endIndex];
            if (!end.TryFindNeighbor(prev, out var endNeighborIndex))
                throw new Exception("Inconsistent edge");

            start.RemoveNeighborIndex(startNeighborIndex);
            end.RemoveNeighborIndex(endNeighborIndex);

            // Repair condensed nodes.
            RepairCondensed(ref start, edge.StartingNode);
            RepairCondensed(ref end, curr);
        }

        public void Clear()
        {
            _vaporNodes.Clear();
            _condensedNodes.Clear();
            _condensedNodeData.Clear();
            _edgeData.Clear();
        }

        [Conditional("CONDENSED_GRAPH_CHECK")]
        public void Check()
        {
            foreach (var id in _condensedNodes)
            {
                ref var node = ref _condensedNodeData[id.Value];
                System.Diagnostics.Debug.Assert(node.NeighborCount != 0 && node.NeighborCount != 2, "Condensed node has incorrect neighbor count");
                for (var i = 0; i < node.NeighborCount; i++)
                {
                    ref var neighbor = ref node.Neighbors.Ref(i);
                    System.Diagnostics.Debug.Assert(_condensedNodes.ContainsKey(neighbor.Condensed), "Neighbor isn't condensed");
                    var prev = id.Key;
                    var curr = neighbor.OutgoingVapor;
                    while (true)
                    {
                        PackedEdge.Primary(prev, curr, out var edge);
                        System.Diagnostics.Debug.Assert(_edgeData.ContainsKey(edge), "Edge does not exist");
                        if (_condensedNodes.TryGetValue(curr, out var otherIndex))
                        {
                            System.Diagnostics.Debug.Assert(
                                curr == neighbor.Condensed,
                                "First condensed node along chain equals neighbor's condensed ID");
                            System.Diagnostics.Debug.Assert(
                                prev == neighbor.IncomingVapor,
                                "Last vapor node along chain equals neighbor's incoming vapor");

                            ref var other = ref _condensedNodeData[otherIndex];
                            var found = false;
                            for (var j = 0; j < other.NeighborCount; j++)
                            {
                                ref var otherNeighbor = ref other.Neighbors.Ref(j);
                                if (otherNeighbor.OutgoingVapor != prev) continue;
                                System.Diagnostics.Debug.Assert(
                                    otherNeighbor.Condensed == id.Key,
                                    "Other neighbor's condensed doesn't match");
                                System.Diagnostics.Debug.Assert(
                                    otherNeighbor.OutgoingVapor == neighbor.IncomingVapor,
                                    "Other neighbor's outgoing must be equal to neighbor's incoming");
                                System.Diagnostics.Debug.Assert(
                                    otherNeighbor.IncomingVapor == neighbor.OutgoingVapor,
                                    "Other neighbor's incoming must be equal to neighbor's outgoing");
                                found = true;
                            }

                            System.Diagnostics.Debug.Assert(found, "Neighbor was not found in the condensed node");
                            break;
                        }

                        var vapor = _vaporNodes[curr];
                        var next = vapor.Opposing(prev);
                        prev = curr;
                        curr = next;
                    }
                }
            }
        }

        #region Condensed View

        public CondensedView Condensed => new CondensedView(this);

        public readonly struct CondensedView : IEnumerableGraph<CondensedEdgeView>
        {
            private readonly CondensedGraph<TEdgeData> _owner;

            public CondensedView(CondensedGraph<TEdgeData> owner) => _owner = owner;

            public NeighborsEnumerable Neighbors(uint v) => new NeighborsEnumerable(_owner, v);

            IEnumerable<Neighbor<CondensedEdgeView>> IEnumerableGraph<CondensedEdgeView>.Neighbors(uint v) => Neighbors(v);

            public CondensedEdgeEnumerable Edges => new CondensedEdgeEnumerable(_owner);

            IEnumerable<Edge<CondensedEdgeView>> IEnumerableGraph<CondensedEdgeView>.Edges => Edges;

            public IEnumerable<uint> Nodes => _owner._condensedNodes.Keys;
        }

        #region Neighbor Edges

        public readonly struct NeighborsEnumerable : IReadOnlyCollection<Neighbor<CondensedEdgeView>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;
            private readonly uint _id;

            public NeighborsEnumerable(CondensedGraph<TEdgeData> owner, uint id)
            {
                _owner = owner;
                _id = id;
            }

            public NeighborsEnumerator GetEnumerator() => new NeighborsEnumerator(_owner, _id);

            IEnumerator<Neighbor<CondensedEdgeView>> IEnumerable<Neighbor<CondensedEdgeView>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int Count => _owner._condensedNodes.TryGetValue(_id, out var index) ? (int)_owner._condensedNodeData[index].NeighborCount : 0;
        }

        public struct NeighborsEnumerator : IEnumerator<Neighbor<CondensedEdgeView>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;
            private readonly uint _id;
            private readonly uint _index;
            private int _neighbor;

            public NeighborsEnumerator(CondensedGraph<TEdgeData> owner, uint id)
            {
                _owner = owner;
                _id = id;
                _index = _owner._condensedNodes.GetValueOrDefault(id, uint.MaxValue);
                _neighbor = -1;
            }

            private ref CondensedNode Node => ref _owner._condensedNodeData[_index];

            public void Dispose()
            {
            }

            public bool MoveNext() => _id != uint.MaxValue && ++_neighbor < Node.NeighborCount;

            public void Reset() => throw new NotImplementedException();

            public Neighbor<CondensedEdgeView> Current
            {
                get
                {
                    ref var neighbor = ref Node.Neighbors.Ref(_neighbor);
                    return new Neighbor<CondensedEdgeView>(neighbor.Condensed, new CondensedEdgeView(_owner, _id, neighbor.OutgoingVapor));
                }
            }

            object IEnumerator.Current => Current;
        }

        #endregion

        #region Condensed Edges

        public readonly struct CondensedEdgeEnumerable : IEnumerable<Edge<CondensedEdgeView>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;

            public CondensedEdgeEnumerable(CondensedGraph<TEdgeData> owner) => _owner = owner;

            public CondensedEdgeEnumerator GetEnumerator() => new CondensedEdgeEnumerator(_owner);

            IEnumerator<Edge<CondensedEdgeView>> IEnumerable<Edge<CondensedEdgeView>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct CondensedEdgeEnumerator : IEnumerator<Edge<CondensedEdgeView>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;
            private Dictionary<uint, uint>.Enumerator _nodesEnumerator;
            private int _nodeNeighbor;
            private State _state;

            public CondensedEdgeEnumerator(CondensedGraph<TEdgeData> owner)
            {
                _owner = owner;
                _nodesEnumerator = owner._condensedNodes.GetEnumerator();
                _nodeNeighbor = -1;
                _state = State.NotStarted;
            }

            public void Dispose() => _nodesEnumerator.Dispose();

            public bool MoveNext()
            {
                if (_state == State.NotStarted)
                {
                    _state = State.Started;
                    if (!_nodesEnumerator.MoveNext())
                        _state = State.Finished;
                }

                if (_state == State.Finished)
                    return false;

                while (true)
                {
                    var curr = _nodesEnumerator.Current;
                    ref var node = ref _owner._condensedNodeData[curr.Value];
                    ++_nodeNeighbor;
                    if (_nodeNeighbor >= node.NeighborCount)
                    {
                        _nodeNeighbor = -1;
                        if (_nodesEnumerator.MoveNext())
                            continue;
                        _state = State.Finished;
                        return false;
                    }

                    ref var neighbor = ref node.Neighbors.Ref(_nodeNeighbor);
                    if (neighbor.Condensed >= curr.Key)
                        continue;
                    return true;
                }
            }

            public void Reset() => throw new NotImplementedException();

            public Edge<CondensedEdgeView> Current
            {
                get
                {
                    var curr = _nodesEnumerator.Current;
                    ref var node = ref _owner._condensedNodeData[curr.Value];
                    ref var neighbor = ref node.Neighbors.Ref(_nodeNeighbor);
                    return new Edge<CondensedEdgeView>(curr.Key, neighbor.Condensed, new CondensedEdgeView(_owner, curr.Key, neighbor.OutgoingVapor));
                }
            }

            object IEnumerator.Current => Current;

            private enum State : byte
            {
                NotStarted,
                Started,
                Finished
            }
        }

        #endregion

        #region Condensed Edge View

        public readonly struct CondensedEdgeView : IEnumerable<Neighbor<TEdgeData>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;
            internal readonly uint StartingNode;
            internal readonly uint OutgoingVapor;

            public CondensedEdgeView(CondensedGraph<TEdgeData> owner, uint startingNode, uint outgoingVapor)
            {
                _owner = owner;
                StartingNode = startingNode;
                OutgoingVapor = outgoingVapor;
            }

            public CondensedEdgeViewEnumerator GetEnumerator() => new CondensedEdgeViewEnumerator(_owner, StartingNode, OutgoingVapor);

            IEnumerator<Neighbor<TEdgeData>> IEnumerable<Neighbor<TEdgeData>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public override string ToString() => $"condensed edge starting at {StartingNode} via {OutgoingVapor}";
        }

        public struct CondensedEdgeViewEnumerator : IEnumerator<Neighbor<TEdgeData>>
        {
            private readonly CondensedGraph<TEdgeData> _owner;
            private bool _first;
            private uint _prev;
            private uint _curr;

            public CondensedEdgeViewEnumerator(CondensedGraph<TEdgeData> owner, uint prev, uint curr)
            {
                _owner = owner;
                _first = true;
                _prev = prev;
                _curr = curr;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_first)
                {
                    _first = false;
                    return true;
                }

                if (!_owner._vaporNodes.TryGetValue(_curr, out var vapor))
                    return false;

                var next = vapor.Opposing(_prev);
                _prev = _curr;
                _curr = next;
                return true;
            }

            public void Reset() => throw new NotImplementedException();

            public Neighbor<TEdgeData> Current
            {
                get
                {
                    PackedEdge.Primary(_prev, _curr, out var edge);
                    return new Neighbor<TEdgeData>(_curr, _owner._edgeData[edge]);
                }
            }

            object IEnumerator.Current => Current;
        }

        #endregion

        #endregion
    }
}