using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FastCollections.Unsafe;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public sealed class Graph<TEdgeData> : IGraph<TEdgeData>, IEnumerableGraph<TEdgeData> where TEdgeData : struct
    {
        // Primary edges are sorted by min node in edge, then max node in edge.
        private readonly BTree<PackedEdge, TEdgeData> _primaryEdges = new BTree<PackedEdge, TEdgeData>();

        // Secondary edges are the inverse (max node in edge, then min node in edge)
        private readonly BTree<PackedEdge, TEdgeData> _secondaryEdges = new BTree<PackedEdge, TEdgeData>();

        public int EdgeCount => _primaryEdges.Count;

        public void AddEdge(uint a, uint b, TEdgeData data)
        {
            PackedEdge.PrimarySecondary(a, b, out var primary, out var secondary);
            _primaryEdges[primary] = data;
            _secondaryEdges[secondary] = data;
        }

        public bool ContainsEdge(uint a, uint b)
        {
            PackedEdge.Primary(a, b, out var primary);
            return _primaryEdges.ContainsKey(primary);
        }

        public void RemoveEdge(uint a, uint b)
        {
            PackedEdge.PrimarySecondary(a, b, out var primary, out var secondary);
            _primaryEdges.Remove(primary);
            _secondaryEdges.Remove(secondary);
        }

        public void RemoveVertex(uint a)
        {
            using (PoolManager.Get(out List<uint> others))
            {
                foreach (var neighbor in Neighbors(a))
                    others.Add(neighbor.Node);
                foreach (var neighbor in others)
                    RemoveEdge(a, neighbor);
            }
        }

        public void Clear()
        {
            _primaryEdges.Clear();
            _secondaryEdges.Clear();
        }

        #region Neighbor Enumeration

        public NeighborEnumerable Neighbors(uint v) => new NeighborEnumerable(this, v);

        IEnumerable<Neighbor<TEdgeData>> IEnumerableGraph<TEdgeData>.Neighbors(uint v) => Neighbors(v);

        private static PackedEdge MinKey(uint v) => new PackedEdge(v, 0);

        private static PackedEdge MaxKey(uint v) => new PackedEdge(v, uint.MaxValue);

        public readonly struct NeighborEnumerable : IEnumerable<Neighbor<TEdgeData>>
        {
            private readonly Graph<TEdgeData> _graph;
            private readonly uint _node;

            public NeighborEnumerable(Graph<TEdgeData> graph, uint node)
            {
                _graph = graph;
                _node = node;
            }

            public NeighborEnumerator GetEnumerator()
            {
                var min = MinKey(_node);
                return new NeighborEnumerator(
                    _node,
                    _graph._primaryEdges.LowerBound(min),
                    _graph._secondaryEdges.LowerBound(min));
            }

            IEnumerator<Neighbor<TEdgeData>> IEnumerable<Neighbor<TEdgeData>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct NeighborEnumerator : IEnumerator<Neighbor<TEdgeData>>
        {
            private readonly uint _node;
            private BTree<PackedEdge, TEdgeData>.Iterator _primary;
            private BTree<PackedEdge, TEdgeData>.Iterator _secondary;
            private State _state;

            internal NeighborEnumerator(uint node, BTree<PackedEdge, TEdgeData>.Iterator primary, BTree<PackedEdge, TEdgeData>.Iterator secondary)
            {
                _node = node;
                _primary = primary;
                _secondary = secondary;
                _state = State.NotStarted;
            }

            public void Dispose()
            {
            }

            private bool IsValid(ref BTree<PackedEdge, TEdgeData>.Iterator itr) => itr.IsValid && itr.Key.First == _node;

            private static Neighbor<TEdgeData> Read(ref BTree<PackedEdge, TEdgeData>.Iterator itr) => new Neighbor<TEdgeData>(itr.Key.Second, itr.Value);

            public bool MoveNext()
            {
                switch (_state)
                {
                    case State.NotStarted:
                        _state = State.OnPrimary;
                        break;
                    case State.OnPrimary:
                        _primary.Increment();
                        break;
                    case State.OnSecondary:
                        _secondary.Increment();
                        break;
                    case State.Finished:
                        return false;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (_state == State.OnPrimary)
                {
                    if (IsValid(ref _primary))
                        return true;
                    _state = State.OnSecondary;
                }

                if (_state == State.OnSecondary)
                {
                    if (IsValid(ref _secondary))
                        return true;
                    _state = State.Finished;
                    return false;
                }

                throw new Exception("Unreachable");
            }

            public void Reset() => throw new NotImplementedException();

            public Neighbor<TEdgeData> Current
            {
                get
                {
                    switch (_state)
                    {
                        case State.NotStarted:
                            throw new Exception("Not started");
                        case State.OnPrimary:
                            return Read(ref _primary);
                        case State.OnSecondary:
                            return Read(ref _secondary);
                        case State.Finished:
                            throw new Exception("Finished");
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            object IEnumerator.Current => Current;

            private enum State
            {
                NotStarted,
                OnPrimary,
                OnSecondary,
                Finished
            }
        }

        #endregion

        #region Edge Enumeration

        public EdgeEnumerable Edges => new EdgeEnumerable(this);

        IEnumerable<Edge<TEdgeData>> IEnumerableGraph<TEdgeData>.Edges => Edges;

        public readonly struct EdgeEnumerable : IEnumerable<Edge<TEdgeData>>
        {
            private readonly Graph<TEdgeData> _graph;

            public EdgeEnumerable(Graph<TEdgeData> graph) => _graph = graph;

            public EdgeEnumerator GetEnumerator() => new EdgeEnumerator(_graph);

            IEnumerator<Edge<TEdgeData>> IEnumerable<Edge<TEdgeData>>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct EdgeEnumerator : IEnumerator<Edge<TEdgeData>>
        {
            private BTree<PackedEdge, TEdgeData>.Iterator _iterator;

            public EdgeEnumerator(Graph<TEdgeData> graph)
            {
                _iterator = graph._primaryEdges.Begin;
                _iterator.Decrement();
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                _iterator.Increment();
                return _iterator.IsValid;
            }

            public void Reset()
            {
            }

            public Edge<TEdgeData> Current
            {
                get
                {
                    var key = _iterator.Key;
                    return new Edge<TEdgeData>(key.First, key.Second, _iterator.Value);
                }
            }

            object IEnumerator.Current => Current;
        }

        #endregion

        #region Edge Enumeration

        public NodeEnumerable Nodes => new NodeEnumerable(this);

        IEnumerable<uint> IEnumerableGraph<TEdgeData>.Nodes => Nodes;

        public readonly struct NodeEnumerable : IEnumerable<uint>
        {
            private readonly Graph<TEdgeData> _graph;

            public NodeEnumerable(Graph<TEdgeData> graph) => _graph = graph;

            public NodeEnumerator GetEnumerator() => new NodeEnumerator(_graph);

            IEnumerator<uint> IEnumerable<uint>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct NodeEnumerator : IEnumerator<uint>
        {
            private BTree<PackedEdge, TEdgeData>.Iterator _primary;
            private BTree<PackedEdge, TEdgeData>.Iterator _secondary;
            private bool _started;

            public NodeEnumerator(Graph<TEdgeData> graph)
            {
                _primary = graph._primaryEdges.Begin;
                _secondary = graph._secondaryEdges.Begin;
                _started = false;
                Current = default;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (!_started)
                {
                    // The lowest entry in primary is guaranteed to be lower than the lowest entry in secondary.
                    if (!_primary.IsValid)
                        return false;
                    Current = _primary.Key.First;
                    _primary.Increment();
                    _started = true;
                    return true;
                }

                var prev = Current;
                // Advance both primary & secondary until they are pointing to new nodes.
                while (_primary.IsValid && _primary.Key.First == prev)
                    _primary.Increment();
                while (_secondary.IsValid && _secondary.Key.First == prev)
                    _secondary.Increment();
                switch (_primary.IsValid)
                {
                    // If both are valid return the lowest of the pair.
                    case true when _secondary.IsValid:
                    {
                        var pk = _primary.Key.First;
                        var sk = _secondary.Key.First;
                        if (pk <= sk)
                        {
                            _primary.Increment();
                            Current = pk;
                        }

                        if (sk <= pk)
                        {
                            _secondary.Increment();
                            Current = sk;
                        }

                        return true;
                    }
                    // If only primary is valid return it.
                    case true:
                        Current = _primary.Key.First;
                        _primary.Increment();
                        return true;
                    // If only secondary is valid return it.
                    case false when _secondary.IsValid:
                        Current = _secondary.Key.First;
                        _secondary.Increment();
                        return true;
                    // If none are valid we've hit the end.
                    case false:
                        return false;
                }
            }

            public void Reset()
            {
            }

            public uint Current { get; private set; }

            object IEnumerator.Current => Current;
        }

        #endregion

        public override string ToString()
        {
            const int limit = 25;
            return $"G[E={_primaryEdges.Count},{string.Join(", ", Edges.Take(limit))}{(_primaryEdges.Count > limit ? ", ..." : "")}]";
        }
    }
}