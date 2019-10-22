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

        public OrderedRayEnumerator IntersectRayOrdered(in Ray ray)
        {
            return new OrderedRayEnumerator(this, ray);
        }

        public UnorderedRayEnumerator IntersectRayUnordered(in Ray ray)
        {
            return new UnorderedRayEnumerator(this, ray);
        }

        /// <summary>
        /// Weakly ordered ray enumerator, where nodes are typically evaluated near to far, but not always.
        /// </summary>
        public struct UnorderedRayEnumerator : IEnumerator<int>
        {
            private Stack<KeyValuePair<float, int>> _nodes;
            private PackedBvh _bvh;
            private Ray _ray;

            public UnorderedRayEnumerator(PackedBvh bvh, Ray ray)
            {
                _bvh = bvh;
                _ray = ray;
                _nodes = null;
                Current = -1;
                CurrentDist = float.MaxValue;
                Reset();
            }

            public bool MoveNext()
            {
                if (_nodes == null)
                    return false;
                while (_nodes.Count > 0)
                {
                    var nodeData = _nodes.Pop();
                    var nodeDist = nodeData.Key;
                    var nodeId = nodeData.Value;
                    ref var node = ref _bvh._nodeTable[nodeId];
                    if (node.IsLeaf)
                    {
                        Current = nodeId;
                        CurrentDist = nodeDist;
                        return true;
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
        /// Strongly ordered ray enumerator.  Nodes are always evaluated near to far.
        /// </summary>
        public struct OrderedRayEnumerator : IEnumerator<int>
        {
            private MyBinaryHeap<float, int> _nodesHeap;
            private PackedBvh _bvh;
            private Ray _ray;

            public OrderedRayEnumerator(PackedBvh bvh, Ray ray)
            {
                _bvh = bvh;
                _ray = ray;
                _nodesHeap = null;
                Current = -1;
                CurrentDist = float.MaxValue;
                Reset();
            }

            public bool MoveNext()
            {
                if (_nodesHeap == null)
                    return false;
                while (_nodesHeap.Count > 0)
                {
                    var nodeDist = _nodesHeap.MinKey();
                    var nodeId = _nodesHeap.RemoveMin();
                    ref var node = ref _bvh._nodeTable[nodeId];
                    if (node.IsLeaf)
                    {
                        Current = nodeId;
                        CurrentDist = nodeDist;
                        return true;
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
                if (_nodesHeap != null)
                {
                    _nodesHeap.Clear();
                    BinaryHeapPool.Add(_nodesHeap);
                    _nodesHeap = null;
                }

                var point = _ray.Intersects(_bvh._nodeTable[0].Box);
                if (point.HasValue)
                {
                    if (!BinaryHeapPool.TryTake(out _nodesHeap))
                        _nodesHeap = new MyBinaryHeap<float, int>();
                    _nodesHeap.Insert(0, point.Value);
                }

                Current = -1;
                CurrentDist = float.MaxValue;
            }

            public float CurrentDist { get; private set; }
            public int Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                if (_nodesHeap != null)
                {
                    _nodesHeap.Clear();
                    BinaryHeapPool.Add(_nodesHeap);
                    _nodesHeap = null;
                }
                _bvh = null;
            }
        }

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
        private static readonly ConcurrentBag<MyBinaryHeap<float, int>> BinaryHeapPool = new ConcurrentBag<MyBinaryHeap<float, int>>();

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