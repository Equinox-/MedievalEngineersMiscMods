
using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public class SahBvhBuilder : IDisposable
    {
        private const int BucketCount = 8;

        private readonly EqReadOnlySpan<BoundingBox> _shapes;
        private readonly BucketData[] _buckets;
        private List<PackedBvh.Node> _nodes;
        private readonly int[] _proxyTable;
        private readonly int _shapesPerNode;

        public SahBvhBuilder(int shapesPerNode, EqReadOnlySpan<BoundingBox> shapes)
        {
            _shapesPerNode = shapesPerNode;
            _shapes = shapes;
            _proxyTable = new int[shapes.Length];
            _nodes = PoolManager.Get<List<PackedBvh.Node>>();
            _buckets = new BucketData[BucketCount];
            for (var i = 0; i < _buckets.Length; i++)
                _buckets[i] = new BucketData(PoolManager.Get<List<int>>());
        }

        public PackedBvh Build()
        {
            _nodes.Clear();
            using (PoolManager.Get(out Queue<int> work))
            {
                // Create root
                {
                    var rootBb = BoundingBox.CreateInvalid();
                    for (var i = 0; i < _shapes.Length; i++)
                    {
                        rootBb.Include(_shapes[i]);
                        _proxyTable[i] = i;
                    }
                    work.Enqueue(_nodes.Count);
                    _nodes.Add(PackedBvh.Node.NewLeaf(in rootBb, 0, _shapes.Length));
                }

                while (work.Count > 0)
                {
                    var inputId = work.Dequeue();
                    var input = _nodes[inputId];
                    if (input.Count > _shapesPerNode && TryPartition(in input, out var lhs, out var rhs))
                    {
                        var originalScore = input.Count * input.Box.SurfaceAreaExt();
                        var newScore = lhs.Box.SurfaceAreaExt() * lhs.Count + rhs.Box.SurfaceAreaExt() * rhs.Count;

                        // Is the split score better than the original score?
                        if (newScore < originalScore)
                        {
                            var lid = _nodes.Count;
                            var rid = _nodes.Count + 1;
                            _nodes[inputId] = PackedBvh.Node.NewNode(in input.Box, lid, rid);
                            work.Enqueue(lid);
                            work.Enqueue(rid);
                            _nodes.Add(lhs);
                            _nodes.Add(rhs);
                            continue;
                        }
                    }
                    // Don't split this node
                    // Sort indirection table for this node to improve cache locality
                    Array.Sort(_proxyTable, input.Min, input.Count);
                }
            }
            return new PackedBvh(_proxyTable, _nodes.ToArray());
        }

        public void Dispose()
        {
            PoolManager.Return(ref _nodes);
            for (var i = 0; i < _buckets.Length; i++)
            {
                var proxies = _buckets[i].Proxies;
                PoolManager.Return(ref proxies);
                _buckets[i] = default;
            }
        }

        private bool TryPartition(in PackedBvh.Node input, out PackedBvh.Node lhs, out PackedBvh.Node rhs)
        {
            var centroids = BoundingBox.CreateInvalid();
            var inputMax = input.Min + input.Count;
            var averageExtents = Vector3.Zero;

            for (var i = input.Min; i < inputMax; i++)
            {
                ref readonly var shape = ref _shapes[_proxyTable[i]];
                centroids.Include(shape.Center);
                averageExtents += shape.Extents;
            }

            averageExtents /= input.Count;

            var axis = Vector3.DominantAxisProjection(centroids.Extents);
            // When the objects are larger than they are spread out we can't bucket them effectively
            if ((axis.X + axis.Y + axis.Z) < 2 * (averageExtents.X + averageExtents.Y + averageExtents.Z))
            {
                var split = (input.Min + inputMax) / 2;
                var lhsBox = BoundingBox.CreateInvalid();
                var rhsBox = BoundingBox.CreateInvalid();
                for (var i = input.Min; i < split; i++)
                    lhsBox.Include(_shapes[_proxyTable[i]]);
                for (var i = split; i < inputMax; i++)
                    rhsBox.Include(_shapes[_proxyTable[i]]);

                lhs = PackedBvh.Node.NewLeaf(in lhsBox, input.Min, split - input.Min);
                rhs = PackedBvh.Node.NewLeaf(in rhsBox, split, inputMax - split);
                return true;
            }


            for (var b = 0; b < BucketCount; b++)
            {
                _buckets[b].Proxies.Clear();
                _buckets[b].Box = BoundingBox.CreateInvalid();
            }

            for (var i = input.Min; i < inputMax; i++)
            {
                var shapeId = _proxyTable[i];
                ref readonly var shape = ref _shapes[shapeId];
                var axisFactor = (shape.Center - centroids.Min).Dot(axis) / axis.LengthSquared();
                var bucket = MathHelper.Clamp((int) Math.Round(axisFactor * (BucketCount - 1)), 0, BucketCount - 1);
                _buckets[bucket].Box.Include(shape);
                _buckets[bucket].Proxies.Add(shapeId);
            }


            // Find best partition:
            var bestSplit = -1; // index split in RHS
            var bestScore = float.MaxValue;
            lhs = default;
            rhs = default;

            for (var split = 1; split < BucketCount; split++)
            {
                var lhsBox = BoundingBox.CreateInvalid();
                var lhsCount = 0;
                var rhsBox = BoundingBox.CreateInvalid();
                var rhsCount = 0;

                for (var i = 0; i < split; i++)
                {
                    lhsBox.Include(_buckets[i].Box);
                    lhsCount += _buckets[i].Proxies.Count;
                }

                for (var i = split; i < BucketCount; i++)
                {
                    rhsBox.Include(_buckets[i].Box);
                    rhsCount += _buckets[i].Proxies.Count;
                }

                if (lhsCount == 0 || rhsCount == 0)
                    continue;

                var score = lhsCount * lhsBox.SurfaceArea() + rhsCount * rhsBox.SurfaceArea();
                if (score < bestScore)
                {
                    bestScore = score;
                    bestSplit = split;
                    lhs = PackedBvh.Node.NewLeaf(in lhsBox, input.Min, lhsCount);
                    rhs = PackedBvh.Node.NewLeaf(in rhsBox, input.Min + lhsCount, rhsCount);
                }
            }

            if (bestSplit == -1)
                return false;

            {
                var head = lhs.Min;
                for (var i = 0; i < bestSplit; i++)
                    foreach (var j in _buckets[i].Proxies)
                        _proxyTable[head++] = j;
            }
            {
                var head = rhs.Min;
                for (var i = bestSplit; i < BucketCount; i++)
                    foreach (var j in _buckets[i].Proxies)
                        _proxyTable[head++] = j;
            }
            return true;
        }

        private struct BucketData
        {
            public BoundingBox Box;
            public readonly List<int> Proxies;

            public BucketData(List<int> proxies)
            {
                Proxies = proxies;
                Box = default;
            }
        }
    }
}