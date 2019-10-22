//using System;
//using System.Collections.Generic;
//using Equinox76561198048419394.Core.Util.EqMath;
//using VRage.Library.Collections;
//using VRageMath;
//
//namespace Equinox76561198048419394.Core.Util.Memory
//{
//    public class SpanAllocator<T>
//    {
//        private readonly T[] _memory;
//        private readonly List<ImmutableRange<int>> _free = new List<ImmutableRange<int>>();
//
//        public SpanAllocator(int capacity, int minimumChunk = 32)
//        {
//            capacity = Math.Max(capacity, minimumChunk * 16);
//            
//            _memory = new T[capacity];
//            _free.Add(new ImmutableRange<int>(0, _memory.Length));
//        }
//
//        private void Allocate(int size, out ImmutableRange<int> region)
//        {
//            var bestId = -1;
//            var bestSize = int.MaxValue;
//            
//            for (var i = _free.Count - 1; i >= 0; i--)
//            {
//                var chunk = _free[i];
//                var chunkSize = chunk.Max - chunk.Min;
//                if (chunkSize >= size && (bestId == -1 || chunkSize < bestSize))
//                {
//                    bestSize = chunkSize;
//                    bestId = i;
//                }
//            }
//            if (bestId == -1)
//                throw new Exception($"Out of memory.  Requesting {size}, free list {string.Join(", ", _free)}");
//
//            var remaining = bestSize - size;
//            if (remaining == 0)
//            {
//                region = _free[bestId];
//                _free.RemoveAt(bestId);
//                return;
//            }
//
//            var block = _free[bestId];
//            region = new ImmutableRange<int>(block.Min, block.Min + size);
//            _free[bestId] = new ImmutableRange<int>(region.Max, block.Max);
//        }
//
//        private void Free(in ImmutableRange<int> region)
//        {
//            var index = _free.BinarySearch(region, Comparer.Instance);
//        }
//        
//
//        public struct Handle
//        {
//            private readonly SpanAllocator<T> _allocator;
//            private readonly ImmutableRange<int> _range;
//            public readonly Span<T> Memory;
//            
//            public Handle(SpanAllocator<T> allocator, int size)
//            {
//                _allocator = allocator;
//            }
//
//        }
//
//        private class Comparer : IComparer<ImmutableRange<int>>
//        {
//            public static readonly Comparer Instance = new Comparer();
//
//            public int Compare(ImmutableRange<int> x, ImmutableRange<int> y)
//            {
//                return x.Min.CompareTo(y.Min);
//            }
//        }
//    }
//}