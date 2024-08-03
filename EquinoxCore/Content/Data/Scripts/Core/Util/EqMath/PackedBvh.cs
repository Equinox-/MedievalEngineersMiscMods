using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Collections;
using VRage.Library.Collections;
using VRageMath;
using VRageMath.Serialization;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public class PackedBvh
    {
        private readonly int[] _proxyTable;
        private readonly Node[] _nodeTable;

        public PackedBvh(int[] proxyTable, Node[] nodeTable)
        {
            _proxyTable = proxyTable;
            _nodeTable = nodeTable;
        }

        #region Node Accessor

        public int NodeCount => _nodeTable.Length;

        public ref readonly Node GetNode(int nodeId)
        {
            return ref _nodeTable[nodeId];
        }

        public EqReadOnlySpan<int> GetProxies(int nodeId)
        {
            ref var node = ref _nodeTable[nodeId];
            return new EqReadOnlySpan<int>(_proxyTable, node.Min, node.Count);
        }

        #endregion

        #region Node Query

        public OrderedRayEnumerator IntersectRayOrdered(in Ray ray) => new OrderedRayEnumerator(this, ray);

        public UnorderedRayEnumerator IntersectRayUnordered(in Ray ray) => new UnorderedRayEnumerator(this, ray);

        /// <summary>
        /// Weakly ordered ray enumerator, where nodes are typically evaluated near to far, but not always.
        /// </summary>
        public struct UnorderedRayEnumerator : IEnumerator<int>
        {
            private UnorderedRayProxyEnumerator<NodeReturningProxyTest> _backing;

            public UnorderedRayEnumerator(PackedBvh bvh, Ray ray) => _backing = new UnorderedRayProxyEnumerator<NodeReturningProxyTest>(bvh, ray, default);

            public bool MoveNext() => _backing.MoveNext();

            public void Reset() => _backing.Reset();

            public float CurrentDist => _backing.CurrentDist;
            public int Current => _backing.Current;

            object IEnumerator.Current => Current;

            public void Dispose() => _backing.Dispose();
        }

        /// <summary>
        /// Strongly ordered ray enumerator.  Nodes are always evaluated near to far.
        /// </summary>
        public struct OrderedRayEnumerator : IEnumerator<int>
        {
            private OrderedRayProxyEnumerator<NodeReturningProxyTest> _backing;

            public OrderedRayEnumerator(PackedBvh bvh, Ray ray) => _backing = new OrderedRayProxyEnumerator<NodeReturningProxyTest>(bvh, ray, default);

            public bool MoveNext() => _backing.MoveNext();

            public void Reset() => _backing.Reset();

            public float CurrentDist => _backing.CurrentDist;
            public int Current => _backing.Current;

            object IEnumerator.Current => Current;

            public void Dispose() => _backing.Dispose();
        }

        #endregion

        #region Proxy Query

        public OrderedRayProxyEnumerator<T> IntersectRayProxiesOrdered<T>(in Ray ray, in T proxyTest) where T : IProxyTest
        {
            return new OrderedRayProxyEnumerator<T>(this, ray, in proxyTest);
        }

        public UnorderedRayProxyEnumerator<T> IntersectRayProxiesUnordered<T>(in Ray ray, in T proxyTest) where T : IProxyTest
        {
            return new UnorderedRayProxyEnumerator<T>(this, ray, in proxyTest);
        }

        public interface IProxyTest
        {
            /// <summary>
            /// Checks if the given proxy intersects the ray, returning the distance if it does.
            /// </summary>
            float? Intersects(int proxy, in Ray ray);
        }

        private static bool IsEnumeratorProxyId(int id) => id < 0;
        private static int EnumeratorProxyId(int id) => -1 - id;

        // Special marker type that indicates to the proxy enumerator that it should return the node IDs.
        private readonly struct NodeReturningProxyTest : IProxyTest
        {
            public float? Intersects(int proxy, in Ray ray) => throw new Exception();
        }

        /// <summary>
        /// Weakly ordered ray enumerator, where proxies are typically evaluated near to far, but not always.
        /// </summary>
        public struct UnorderedRayProxyEnumerator<T> : IEnumerator<int> where T: IProxyTest
        {
            private Stack<KeyValuePair<float, int>> _nodes;
            private PackedBvh _bvh;
            private Ray _ray;
            private T _proxyTest;

            public UnorderedRayProxyEnumerator(PackedBvh bvh, Ray ray, in T proxyTest)
            {
                _bvh = bvh;
                _ray = ray;
                _nodes = null;
                _proxyTest = proxyTest;
                Current = -1;
                CurrentDist = float.MaxValue;
                Reset();
            }

            public bool MoveNext() => TryMoveNext(float.PositiveInfinity);

            public bool TryMoveNext(float distanceLimit)
            {
                if (_nodes == null)
                    return false;
                while (_nodes.Count > 0)
                {
                    var nodeData = _nodes.Peek();
                    var nodeDist = nodeData.Key;
                    var nodeId = nodeData.Value;
                    if (nodeDist > distanceLimit)
                    {
                        Current = -1;
                        CurrentDist = nodeDist;
                        return false;
                    }
                    _nodes.Pop();
                    if (typeof(T) != typeof(NodeReturningProxyTest) && IsEnumeratorProxyId(nodeId))
                    {
                        Current = EnumeratorProxyId(nodeId);
                        CurrentDist = nodeDist;
                        return true;
                    }
                    ref var node = ref _bvh._nodeTable[nodeId];
                    if (node.IsLeaf)
                    {
                        if (typeof(T) == typeof(NodeReturningProxyTest))
                        {
                            Current = nodeId;
                            CurrentDist = nodeDist;
                            return true;
                        }
                        foreach (var proxy in new EqReadOnlySpan<int>(_bvh._proxyTable, node.Min, node.Count))
                        {
                            var proxyDist = _proxyTest.Intersects(proxy, in _ray);
                            if (proxyDist <= distanceLimit)
                                _nodes.Push(new KeyValuePair<float, int>(proxyDist.Value, EnumeratorProxyId(proxy)));
                        }

                        continue;
                    }

                    ref var lhs = ref _bvh._nodeTable[node.Lhs];
                    var lhsDist = _ray.Intersects(lhs.Box);

                    ref var rhs = ref _bvh._nodeTable[node.Rhs];
                    var rhsDist = _ray.Intersects(rhs.Box);
                    if (lhsDist.HasValue && rhsDist.HasValue)
                    {
                        var ld = new KeyValuePair<float, int>(lhsDist.Value, node.Lhs);
                        var rd = new KeyValuePair<float, int>(rhsDist.Value, node.Rhs);
                        if (lhsDist.Value < rhsDist.Value)
                        {
                            _nodes.Push(rd);
                            _nodes.Push(ld);
                        }
                        else
                        {
                            _nodes.Push(ld);
                            _nodes.Push(rd);
                        }
                    }
                    else if (lhsDist.HasValue)
                    {
                        _nodes.Push(new KeyValuePair<float, int>(lhsDist.Value, node.Lhs));
                    }
                    else if (rhsDist.HasValue)
                    {
                        _nodes.Push(new KeyValuePair<float, int>(rhsDist.Value, node.Rhs));
                    }
                }

                return false;
            }

            public void Reset()
            {
                if (_nodes != null)
                    PoolManager.Return(ref _nodes);

                var point = _ray.Intersects(_bvh._nodeTable[0].Box);
                if (point.HasValue)
                {
                    _nodes = PoolManager.Get<Stack<KeyValuePair<float, int>>>();
                    _nodes.Push(new KeyValuePair<float, int>(point.Value, 0));
                }

                Current = -1;
                CurrentDist = float.MaxValue;
            }

            public float CurrentDist { get; private set; }
            public int Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                if (_nodes != null)
                    PoolManager.Return(ref _nodes);
                _bvh = null;
            }
        }

        /// <summary>
        /// Strongly ordered ray enumerator.  Proxies are always evaluated near to far.
        /// </summary>
        public struct OrderedRayProxyEnumerator<T> : IEnumerator<int> where T : IProxyTest
        {
            private MyBinaryHeap<float, int> _nodesHeap;
            private PackedBvh _bvh;
            private Ray _ray;
            private T _proxyTest;

            public OrderedRayProxyEnumerator(PackedBvh bvh, Ray ray, in T proxyTest)
            {
                _bvh = bvh;
                _ray = ray;
                _nodesHeap = null;
                _proxyTest = proxyTest;
                Current = -1;
                CurrentDist = float.MaxValue;
                Reset();
            }

            public bool MoveNext() => TryMoveNext(float.PositiveInfinity);

            public bool TryMoveNext(float distanceLimit)
            {
                if (_nodesHeap == null)
                    return false;
                while (_nodesHeap.Count > 0)
                {
                    var nodeDist = _nodesHeap.MinKey();
                    if (nodeDist > distanceLimit)
                    {
                        Current = -1;
                        CurrentDist = nodeDist;
                        return false;
                    }

                    var nodeId = _nodesHeap.RemoveMin();
                    if (typeof(T) != typeof(NodeReturningProxyTest) && IsEnumeratorProxyId(nodeId))
                    {
                        Current = EnumeratorProxyId(nodeId);
                        CurrentDist = nodeDist;
                        return true;
                    }
                    ref var node = ref _bvh._nodeTable[nodeId];
                    if (node.IsLeaf)
                    {
                        if (typeof(T) == typeof(NodeReturningProxyTest))
                        {
                            Current = nodeId;
                            CurrentDist = nodeDist;
                            return true;
                        }
                        foreach (var proxy in new EqReadOnlySpan<int>(_bvh._proxyTable, node.Min, node.Count))
                        {
                            var proxyDist = _proxyTest.Intersects(proxy, in _ray);
                            if (proxyDist <= distanceLimit)
                                _nodesHeap.Insert(EnumeratorProxyId(proxy), proxyDist.Value);
                        }

                        continue;
                    }

                    ref var lhs = ref _bvh._nodeTable[node.Lhs];
                    var lhsDist = _ray.Intersects(lhs.Box);
                    if (lhsDist.HasValue)
                        _nodesHeap.Insert(node.Lhs, lhsDist.Value);

                    ref var rhs = ref _bvh._nodeTable[node.Rhs];
                    var rhsDist = _ray.Intersects(rhs.Box);
                    if (rhsDist.HasValue)
                        _nodesHeap.Insert(node.Rhs, rhsDist.Value);
                }

                return false;
            }

            public void Reset()
            {
                var point = _ray.Intersects(_bvh._nodeTable[0].Box);
                if (point.HasValue)
                {
                    if (_nodesHeap == null)
                        _nodesHeap = BinaryHeapPool<float, int>.Get();
                    else
                        _nodesHeap.Clear();
                    _nodesHeap.Insert(0, point.Value);
                }
                else
                    BinaryHeapPool<float, int>.Return(ref _nodesHeap);

                Current = -1;
                CurrentDist = float.MaxValue;
            }

            public float CurrentDist { get; private set; }
            public int Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                BinaryHeapPool<float, int>.Return(ref _nodesHeap);
                _bvh = null;
            }
        }

        #endregion

        public struct Node
        {
            private Node(in BoundingBox box, int a, int b)
            {
                Box = box;
                Min = a;
                Count = b;
            }

            public readonly BoundingBox Box;

            public static Node NewNode(in BoundingBox box, int lhs, int rhs)
            {
                return new Node(box, -lhs, -rhs);
            }

            public static Node NewLeaf(in BoundingBox box, int min, int count)
            {
                return new Node(in box, min, count);
            }

            public bool IsLeaf => Min >= 0;

            // Leaf only
            public int Min { get; }

            public int Count { get; }

            // Node only
            public int Lhs => -Min;
            public int Rhs => -Count;

            public static readonly ISerializer<Node> Serializer = new SerializerType();

            private class SerializerType : ISerializer<Node>
            {
                public void Write(BinaryWriter target, in Node value)
                {
                    target.Write(value.Box.Min);
                    target.Write(value.Box.Max);
                    target.Write(value.Min);
                    target.Write(value.Count);
                }

                public void Read(BinaryReader source, out Node value)
                {
                    var box = default(BoundingBox);
                    box.Min = source.ReadVector3();
                    box.Max = source.ReadVector3();
                    value = new Node(in box, source.ReadInt32(), source.ReadInt32());
                }
            }
        }

        public static readonly ISerializer<PackedBvh> Serializer = new SerializerType();

        private class SerializerType : ISerializer<PackedBvh>
        {
            public void Write(BinaryWriter target, in PackedBvh value)
            {
                target.Write(value._nodeTable.Length);
                foreach (var t in value._nodeTable)
                    Node.Serializer.Write(target, in t);

                target.Write(value._proxyTable.Length);
                foreach (var p in value._proxyTable)
                    target.Write(p);
            }

            public void Read(BinaryReader source, out PackedBvh value)
            {
                var nodes = new Node[source.ReadInt32()];
                for (var i = 0; i < nodes.Length; i++)
                    Node.Serializer.Read(source, out nodes[i]);

                var proxy = new int[source.ReadInt32()];
                for (var i = 0; i < proxy.Length; i++)
                    proxy[i] = source.ReadInt32();

                value = new PackedBvh(proxy, nodes);
            }
        }
    }
}