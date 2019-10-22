using System;
using System.Collections.Generic;
using System.IO;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRageMath;
using VRageMath.Serialization;
using VRageRender;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public class TriangleBvhBad<TAdditionalData>
    {
        private readonly FastResourceLock _lock = new FastResourceLock();
        private int _triangleCount;
        private TriangleHolder[] _triangles;
        private int _nodeCount;
        private Node[] _nodes;

        public TriangleBvhBad(in BoundingBox region, int triangles = 128)
        {
            _triangles = new TriangleHolder[triangles];
            _nodes = new Node[Math.Max(32, (int) ((triangles * 1.5f) / MaxTriPerNode))];
            _nodes[_nodeCount++] = new Node {Count = 0, Cell = region};
        }

        private TriangleBvhBad()
        {
        }

        private void CalcChildBounds(in Node parentNode, ref Node childNode, int childId)
        {
            var center = parentNode.Cell.Center;

            var cornerPoint = new Vector3(
                (childId & 1) != 0 ? parentNode.Cell.Max.X : parentNode.Cell.Min.X,
                (childId & 2) != 0 ? parentNode.Cell.Max.Y : parentNode.Cell.Min.Y,
                (childId & 4) != 0 ? parentNode.Cell.Max.Z : parentNode.Cell.Min.Z
            );
            Vector3.Min(ref center, ref cornerPoint, out childNode.Cell.Min);
            Vector3.Max(ref center, ref cornerPoint, out childNode.Cell.Max);
        }

        public void Insert(in Vector3 a, in Vector3 b, in Vector3 c, in TAdditionalData data, in Vector3? desiredNorm = null)
        {
            using (_lock.AcquireExclusiveUsing())
            {
                var holder = new TriangleHolder
                {
                    Triangle = new Triangle(a, b, c, in desiredNorm),
                    Data = data
                };
                var triId = _triangleCount++;
                if (triId >= _triangles.Length)
                    Array.Resize(ref _triangles, _triangles.Length * 2);
                _triangles[triId] = holder;

                using (PoolManager.Get(out Stack<int> nodesToInsertInto))
                {
                    nodesToInsertInto.Push(0);
                    while (nodesToInsertInto.Count > 0)
                    {
                        var nid = nodesToInsertInto.Pop();
                        ref var node = ref _nodes[nid];
                        if (!holder.Triangle.Intersects(in node.Cell))
                            continue;
                        if (node.IsLeaf())
                        {
                            if (node.Count < MaxTriPerNode)
                            {
                                node.Children[node.Count] = triId;
                                node.Count++;
                                continue;
                            }

                            if (_nodeCount + 8 > _nodes.Length)
                                Array.Resize(ref _nodes, _nodes.Length * 2);

                            // subdivide:
                            for (var i = 0; i < 8; i++)
                            {
                                CalcChildBounds(in node, ref _nodes[_nodeCount + i], i);
                                _nodes[_nodeCount + i].Count = 0;
                            }

                            // move triangles to children:
                            for (var i = 0; i < node.Count; i++)
                            {
                                var copyTriId = node.Children[i];
                                ref var copyTri = ref _triangles[copyTriId];
                                for (var child = 0; child < 8; child++)
                                {
                                    ref var childNode = ref _nodes[_nodeCount + child];
                                    if (copyTri.Triangle.Intersects(in childNode.Cell))
                                        childNode.Children[childNode.Count++] = copyTriId;
                                }
                            }

                            node.Count = -1;
                            node.Children.RemoveHeapStorage();
                            for (var i = 0; i < 8; i++)
                            {
                                var childNodeId = _nodeCount + i;
                                node.Children[i] = childNodeId;
                                nodesToInsertInto.Push(childNodeId);
                            }

                            _nodeCount += 8;
                        }
                        else
                        {
                            nodesToInsertInto.Push(node.Children[0]);
                            nodesToInsertInto.Push(node.Children[1]);
                            nodesToInsertInto.Push(node.Children[2]);
                            nodesToInsertInto.Push(node.Children[3]);
                            nodesToInsertInto.Push(node.Children[4]);
                            nodesToInsertInto.Push(node.Children[5]);
                            nodesToInsertInto.Push(node.Children[6]);
                            nodesToInsertInto.Push(node.Children[7]);
                        }
                    }
                }
            }
        }

        public bool RayCast(in Ray ray, out int triangleId, out double length)
        {
            using (_lock.AcquireSharedUsing())
            {
                triangleId = -1;
                length = double.MaxValue;


                // if we are approaching from +x we want to explore +x first:
                var childOrderMix = 0;
                if (ray.Direction.X < 0)
                    childOrderMix |= 1;
                if (ray.Direction.Y < 0)
                    childOrderMix |= 2;
                if (ray.Direction.Z < 0)
                    childOrderMix |= 4;

                using (PoolManager.Get(out Stack<int> explore))
                {
                    explore.Push(0);
                    while (explore.Count > 0)
                    {
                        var nid = explore.Pop();
                        ref var node = ref _nodes[nid];

                        var intersectTime = node.Cell.Intersects(ray);
                        if (!intersectTime.HasValue || intersectTime.Value > length)
                            continue;

                        if (node.IsLeaf())
                        {
                            for (var i = 0; i < node.Count; i++)
                            {
                                var tid = node.Children[i];
                                // ReSharper disable once InvertIf
                                if (_triangles[tid].Triangle.Intersects(in ray, out var intersectLen) && intersectLen < length)
                                {
                                    length = intersectLen;
                                    triangleId = tid;
                                }
                            }

                            continue;
                        }

                        for (var childBase = 0; childBase < 8; childBase++)
                        {
                            var child = childBase ^ childOrderMix;
                            explore.Push(node.Children[child]);
                        }
                    }
                }

                return triangleId >= 0;
            }
        }

        public ref readonly TAdditionalData GetTriangleData(int id)
        {
            return ref _triangles[id].Data;
        }

        public ref readonly Triangle GetTriangle(int id)
        {
            return ref _triangles[id].Triangle;
        }

//        public void DumpToConsole()
//        {
//            DumpToConsole(0, "");
//        }
//
//        private void DumpToConsole(int nid, string tab)
//        {
//            ref var node = ref _nodes[nid];
//            if (node.IsLeaf())
//            {
//                Console.WriteLine($"{tab}Leaf with {node.Count} tri at {node.Cell}");
//                for (var i = 0; i < node.Count; i++)
//                    Console.WriteLine($"{tab} |-" + _triangles[node.Children[i]].Triangle.Normal);
//            }
//            else
//            {
//                Console.WriteLine($"{tab}Node at {node.Cell}");
//                for (var i = 0; i < 8; i++)
//                    DumpToConsole(node.Children[i], tab + " |-");
//            }
//        }

        public void Render(in MatrixD world, int nid = 0)
        {
            ref var node = ref _nodes[nid];
            if (node.IsLeaf())
            {
                if (node.Count > 0)
                {
                    MyRenderProxy.DebugDrawOBB(new OrientedBoundingBoxD(node.Cell, world), Color.Blue, 1f, false, false);
                }
            }
            else
            {
                for (var i = 0; i < 8; i++)
                    Render(in world, node.Children[i]);
            }
        }

        private struct TriangleHolder
        {
            public Triangle Triangle;
            public TAdditionalData Data;
        }

        private const int MaxTriPerNode = StackArray<int>.MaxStackSize;

        private struct Node
        {
            public int Count;
            public BoundingBox Cell;
            public StackArray<int> Children;

            public bool IsLeaf()
            {
                return Count >= 0;
            }
        }

        public class Serializer : ISerializer<TriangleBvhBad<TAdditionalData>>
        {
            private readonly ISerializer<TAdditionalData> _additionalDataSerializer;

            public Serializer(ISerializer<TAdditionalData> additionalDataSerializer)
            {
                _additionalDataSerializer = additionalDataSerializer;
            }

            public void Write(BinaryWriter target, in TriangleBvhBad<TAdditionalData> value)
            {
                ref var rootNode = ref value._nodes[0];
                target.Write(rootNode.Cell.Min);
                target.Write(rootNode.Cell.Max);

                target.Write(value._nodeCount);
                target.Write(value._triangleCount);

                for (var i = 0; i < value._nodeCount; i++)
                {
                    ref var node = ref value._nodes[i];
                    target.Write((sbyte) node.Count);
                    for (var j = 0; j < (node.IsLeaf() ? node.Count : 8); j++)
                        target.Write(node.Children[j]);
                }

                for (var i = 0; i < value._triangleCount; i++)
                {
                    ref var tri = ref value._triangles[i];
                    target.Write(tri.Triangle.A);
                    target.Write(tri.Triangle.B);
                    target.Write(tri.Triangle.C);
                    _additionalDataSerializer.Write(target, in tri.Data);
                }
            }

            public void Read(BinaryReader source, out TriangleBvhBad<TAdditionalData> value)
            {
                var regionMin = source.ReadVector3();
                var regionMax = source.ReadVector3();

                value = new TriangleBvhBad<TAdditionalData>();
                value._nodeCount = source.ReadInt32();
                value._nodes = new Node[value._nodeCount];
                value._triangleCount = source.ReadInt32();
                value._triangles = new TriangleHolder[value._triangleCount];
                for (var i = 0; i < value._nodeCount; i++)
                {
                    ref var node = ref value._nodes[i];
                    node.Count = source.ReadSByte();
                    for (var j = 0; j < (node.IsLeaf() ? node.Count : 8); j++)
                        node.Children[j] = source.ReadInt32();
                }

                for (var i = 0; i < value._triangleCount; i++)
                {
                    ref var tri = ref value._triangles[i];
                    tri.Triangle = new Triangle(source.ReadVector3(), source.ReadVector3(), source.ReadVector3());
                    _additionalDataSerializer.Read(source, out tri.Data);
                }

                value._nodes[0].Cell = new BoundingBox(regionMin, regionMax);
                for (var i = 0; i < value._nodeCount; i++)
                {
                    ref var node = ref value._nodes[i];
                    if (node.IsLeaf())
                        continue;
                    for (var j = 0; j < 8; j++)
                        value.CalcChildBounds(in node, ref value._nodes[node.Children[j]], j);
                }
            }
        }
    }
}