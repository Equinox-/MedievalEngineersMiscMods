using System;
using Equinox76561198048419394.Core.Util.Columnar;
using Equinox76561198048419394.Core.Util.Struct;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.Graph
{
    public sealed class PackedGraph
    {
        public readonly ColumnarStore<NodeIndex> Nodes = new ColumnarStore<NodeIndex>();
        public readonly ColumnarStore<EdgeIndex> Edges = new ColumnarStore<EdgeIndex>();
        private readonly ColumnReference<NodeIndex, NodeData> _nodeData;
        private readonly ColumnReference<EdgeIndex, EdgeData> _edgeData;
        private readonly PagedFreeBlockList<NeighborData> _neighbors = new PagedFreeBlockList<NeighborData>();
        public uint NodeCount => Nodes.AllocatedRows;
        public uint EdgeCount => Edges.AllocatedRows;

        public event DelAfterNodeAdded AfterNodeAdded;
        public event DelAfterEdgeAdded AfterEdgeAdded;
        public event DelBeforeNodeRemoved BeforeNodeRemoved;
        public event DelBeforeEdgeRemoved BeforeEdgeRemoved;
        public event DelBeforeCompaction BeforeCompaction;

        public PackedGraph()
        {
            _nodeData = Nodes.AddColumn<NodeData>();
            _edgeData = Edges.AddColumn<EdgeData>();
        }

        private struct NodeData
        {
            public uint NeighborCount;
            public uint NeighborsRangeOffset;
            public uint NeighborsRangeLength;
        }

        public struct EdgeData
        {
            public NodeIndex NodeFrom, NodeTo;
        }

        public struct NeighborData
        {
            public EdgeIndex EdgeIndex;
            public NodeIndex NodeIndex;
        }

        public delegate void DelAfterNodeAdded(NodeIndex ix);

        public delegate void DelAfterEdgeAdded(EdgeIndex ix);

        public delegate void DelBeforeNodeRemoved(NodeIndex ix);

        public delegate void DelBeforeEdgeRemoved(EdgeIndex ix);

        public delegate void DelBeforeCompaction(in CompactionReport report);

        public ref readonly EdgeData Edge(EdgeIndex edgeIx) => ref Edges.Access(edgeIx, _edgeData);

        public NodeIndex AddNode()
        {
            var nodeIx = Nodes.AddRow();
            InitNode(nodeIx);
            return nodeIx;
        }

        public void InitNode(NodeIndex nodeIx)
        {
            ref var node = ref Nodes.Access(nodeIx, _nodeData);
            node.NeighborCount = 0;
            node.NeighborsRangeLength = 0;
            AfterNodeAdded?.Invoke(nodeIx);
        }

        public void RemoveNode(NodeIndex nodeIx)
        {
            BeforeNodeRemoved?.Invoke(nodeIx);
            ref var node = ref Nodes.Access(nodeIx, _nodeData);
            if (node.NeighborsRangeLength != 0)
            {
                for (var i = 0u; i < node.NeighborCount; i++)
                {
                    ref var neighbor = ref _neighbors[node.NeighborsRangeOffset + i];
                    BeforeEdgeRemoved?.Invoke(neighbor.EdgeIndex);
                    var edgeIx = neighbor.EdgeIndex;
                    ref var edge = ref Edges.Access(edgeIx, _edgeData);
                    if (edge.NodeFrom != nodeIx) RemoveNeighbor(ref Nodes.Access(edge.NodeFrom, _nodeData), neighbor.EdgeIndex);
                    if (edge.NodeTo != nodeIx) RemoveNeighbor(ref Nodes.Access(edge.NodeTo, _nodeData), neighbor.EdgeIndex);
                    Edges.RemoveRow(edgeIx);
                }

                _neighbors.Free(node.NeighborsRangeOffset, node.NeighborsRangeLength);
            }

            Nodes.RemoveRow(nodeIx);
        }

        public EdgeIndex AddEdge(NodeIndex from, NodeIndex to)
        {
            var edgeIx = Edges.AddRow();
            InitEdge(edgeIx, from, to);
            return edgeIx;
        }

        public void InitEdge(EdgeIndex edgeIx, NodeIndex from, NodeIndex to)
        {
            ref var edge = ref Edges.Access(edgeIx, _edgeData);
            edge.NodeFrom = from;
            edge.NodeTo = to;
            AddNeighbor(ref Nodes.Access(from, _nodeData), edgeIx, to);
            if (from != to) AddNeighbor(ref Nodes.Access(to, _nodeData), edgeIx, from);
            AfterEdgeAdded?.Invoke(edgeIx);
        }

        public void RemoveEdge(EdgeIndex edgeIx)
        {
            BeforeEdgeRemoved?.Invoke(edgeIx);
            ref var edge = ref Edges.Access(edgeIx, _edgeData);
            RemoveNeighbor(ref Nodes.Access(edge.NodeFrom, _nodeData), edgeIx);
            if (edge.NodeFrom != edge.NodeTo) RemoveNeighbor(ref Nodes.Access(edge.NodeTo, _nodeData), edgeIx);
            Edges.RemoveRow(edgeIx);
        }

        #region Neighbor Maintenace

        private void EnforceSize(ref NodeData node, uint neighborCount)
        {
            // Only change the size if it must be increased for the new item, or if it at least twice as large as it needs to be.
            if (neighborCount <= node.NeighborsRangeLength && neighborCount * 2 >= node.NeighborsRangeLength)
                return;
            var newSize = MathHelper.GetNearestBiggerPowerOfTwo(neighborCount);
            node.NeighborsRangeOffset = _neighbors.Reallocate(node.NeighborsRangeOffset, node.NeighborsRangeLength, newSize);
            node.NeighborsRangeLength = newSize;
        }

        private void AddNeighbor(ref NodeData node, EdgeIndex edgeIndex, NodeIndex nodeIndex)
        {
            EnforceSize(ref node, node.NeighborCount + 1);
            ref var neighbor = ref _neighbors[node.NeighborsRangeOffset + node.NeighborCount];
            neighbor.EdgeIndex = edgeIndex;
            neighbor.NodeIndex = nodeIndex;
            node.NeighborCount++;
        }

        private void RemoveNeighbor(ref NodeData node, EdgeIndex edgeIndex)
        {
            for (var j = 0u; j < node.NeighborCount; j++)
            {
                ref var neighbor = ref _neighbors[node.NeighborsRangeOffset + j];
                if (neighbor.EdgeIndex != edgeIndex) continue;
                RemoveNeighborAt(ref node, j);
                return;
            }

            System.Diagnostics.Debug.Fail("Attempted to remove missing neighbor");
            return;

            void RemoveNeighborAt(ref NodeData innerNode, uint ix)
            {
                var offset = innerNode.NeighborsRangeOffset + ix;
                innerNode.NeighborCount--;
                _neighbors.Copy(offset + 1, offset, innerNode.NeighborCount - ix);
                EnforceSize(ref innerNode, innerNode.NeighborCount);
            }
        }

        #endregion

        #region Neighbor Access

        public NeighborCollection Neighbors(NodeIndex nodeIx) => new NeighborCollection(this, nodeIx);

        public readonly struct NeighborCollection
        {
            private readonly PackedGraph _owner;
            private readonly NodeIndex _nodeIx;

            public NeighborCollection(PackedGraph owner, NodeIndex nodeIx)
            {
                _owner = owner;
                _nodeIx = nodeIx;
            }

            public uint Count => _owner.Nodes.Access(_nodeIx, _owner._nodeData).NeighborCount;

            public NeighborEnumerator GetEnumerator() => new NeighborEnumerator(_owner, _nodeIx);
        }

        public struct NeighborEnumerator
        {
            private PagedList<NeighborData>.RangeEnumerator _base;

            public NeighborEnumerator(PackedGraph owner, NodeIndex nodeIx)
            {
                ref var node = ref owner.Nodes.Access(nodeIx, owner._nodeData);
                _base = owner._neighbors.Range(node.NeighborsRangeOffset, node.NeighborCount).GetEnumerator();
            }

            public bool MoveNext() => _base.MoveNext();

            public ReadOnlySpan<NeighborData> Current => _base.Current.Span;
        }

        #endregion

        #region Compaction

        /// <summary>
        /// Compacts all free regions of this list.
        /// Callers should update their references using the returned value.
        /// Then, the actual compaction of stored values occurs when the returned value is disposed.
        /// </summary>
        public CompactionReport Compact() => new CompactionReport(this);

        public readonly struct CompactionReport : IDisposable
        {
            private readonly PackedGraph _owner;
            public readonly ColumnarStore<NodeIndex>.CompactionReport Nodes;
            public readonly ColumnarStore<EdgeIndex>.CompactionReport Edges;
            private readonly PagedFreeBlockList<NeighborData>.CompactionReport _neighborsReport;

            public CompactionReport(PackedGraph owner)
            {
                _owner = owner;
                Nodes = owner.Nodes.Compact();
                Edges = owner.Edges.Compact();
                _neighborsReport = owner._neighbors.Compact();
            }

            public bool IsCompacted => Nodes.IsCompacted || Edges.IsCompacted || _neighborsReport.IsCompacted;

            public void Dispose()
            {
                _owner.BeforeCompaction?.Invoke(in this);

                if (IsCompacted)
                {
                    var compactNeighborRefs = Nodes.IsCompacted || Edges.IsCompacted;
                    foreach (var segment in _owner.Nodes.Rows)
                    {
                        var span = segment.Column(_owner._nodeData);
                        for (var i = 0; i < span.Length; i++)
                        {
                            ref var node = ref span[i];
                            if (node.NeighborsRangeLength == 0) continue;
                            if (compactNeighborRefs)
                                for (var j = 0u; j < node.NeighborCount; j++)
                                {
                                    ref var neighbor = ref _owner._neighbors[node.NeighborsRangeOffset + j];
                                    Nodes.UpdateRef(ref neighbor.NodeIndex);
                                    Edges.UpdateRef(ref neighbor.EdgeIndex);
                                }

                            _neighborsReport.UpdateIndex(ref node.NeighborsRangeOffset);
                        }
                    }
                }

                if (Edges.IsCompacted)
                    foreach (var segment in _owner.Edges.Rows)
                    {
                        var span = segment.Column(_owner._edgeData);
                        for (var i = 0; i < span.Length; i++)
                        {
                            ref var edge = ref span[i];
                            Nodes.UpdateRef(ref edge.NodeFrom);
                            Nodes.UpdateRef(ref edge.NodeTo);
                        }
                    }

                Nodes.Dispose();
                Edges.Dispose();
                _neighborsReport.Dispose();
            }
        }

        #endregion
    }
}