using System;
using System.Collections;
using System.Collections.Generic;
using VRage.Library.Collections;

namespace Equinox76561198048419394.Core.Util.Memory
{
    internal class EqAllocator
    {
        private readonly List<Range> _freeList = new List<Range>();
        private uint _highWaterMark;
        public uint Allocated { get; private set; }

        /// <summary>
        /// Allocates a new continuous block and returns the offset.
        /// </summary>
        public uint Allocate(uint count)
        {
            System.Diagnostics.Debug.Assert(count > 0);
            var offset = AllocateInternal(count);
            Allocated += count;
            System.Diagnostics.Debug.Assert(Validate());
            return offset;
        }

        private uint AllocateInternal(uint count)
        {
            uint offset;
            for (var i = 0; i < _freeList.Count; i++)
            {
                var region = _freeList[i];
                // Region isn't large enough; try another.
                if (region.Count < count)
                    continue;

                offset = region.Offset;
                if (region.Count == count)
                {
                    // Region is exactly large enough, so remove the region and return the offset.
                    _freeList.RemoveAt(i);
                    return offset;
                }

                // Region is larger than needed, so shrink the free region and return the previous offset.
                region.Offset = offset + count;
                region.Count -= count;
                _freeList[i] = region;
                return offset;
            }

            offset = _highWaterMark;
            _highWaterMark += count;
            return offset;
        }

        /// <summary>
        /// Attempts to reserve an explicit range; can be used to expand existing allocations.
        /// </summary>
        public bool TryAllocateExact(uint offset, uint count)
        {
            System.Diagnostics.Debug.Assert(count > 0);
            var okay = TryAllocateExactInternal(offset, count);
            if (okay) Allocated += count;
            System.Diagnostics.Debug.Assert(Validate());
            return okay;
        }

        private bool TryAllocateExactInternal(uint offset, uint count)
        {
            System.Diagnostics.Debug.Assert(offset <= _highWaterMark, "Should not attempt allocation past the high water mark");

            // Allocating at the end will always succeed.
            if (_highWaterMark == offset)
            {
                _highWaterMark += count;
                return true;
            }

            // If nothing is free, no allocation in the middle will be successful.
            if (_freeList.Count == 0)
                return false;

            // Search for a region containing the offset.
            var ix = _freeList.BinarySearch(new Range { Offset = offset });
            if (ix >= 0)
                return AllocateAtHead();
            // Convert from the first region starting after the offset to the last region starting before the offset.
            ix = ~ix - 1;
            // First free region starts after the offset, so the exact allocation isn't possible.
            return ix >= 0 && AllocateAfterHead();

            bool AllocateAtHead()
            {
                // Found an exact match, so the offsets are identical.
                var region = _freeList[ix];
                System.Diagnostics.Debug.Assert(region.Offset == offset);
                if (region.Count < count)
                    return false;
                // Shrink the free region.
                region.Offset += count;
                region.Count -= count;
                // Remove the region if empty, otherwise update it.
                if (region.Count == 0)
                    _freeList.RemoveAt(ix);
                else
                    _freeList[ix] = region;
                return true;
            }

            bool AllocateAfterHead()
            {
                var region = _freeList[ix];
                System.Diagnostics.Debug.Assert(region.Offset < offset);

                // If the free region ends before the allocation end, the allocation can't be made.
                var regionEnd = region.Offset + region.Count;
                var allocationEnd = offset + count;
                if (regionEnd < allocationEnd)
                    return false;

                // Shrink the free region to stop at the beginning of the allocation.
                region.Count = offset - region.Offset;
                _freeList[ix] = region;

                if (regionEnd == allocationEnd)
                    return true;
                // If the free region extends past the allocation end, insert a new free region.
                _freeList.Insert(ix + 1, new Range
                {
                    Offset = allocationEnd,
                    Count = regionEnd - allocationEnd,
                });
                return true;
            }
        }

        /// <summary>
        /// Frees a continuous block.
        /// </summary>
        public void Free(uint offset, uint count)
        {
            System.Diagnostics.Debug.Assert(count > 0);
            FreeInternal(offset, count);
            Allocated -= count;
            System.Diagnostics.Debug.Assert(Validate());
        }

        private void FreeInternal(uint offset, uint count)
        {
            System.Diagnostics.Debug.Assert(offset + count <= _highWaterMark, "Should not attempt free past the high water mark");
            // Freeing the end can use the fast path.
            var freeEnd = offset + count;
            if (_highWaterMark == freeEnd)
            {
                _highWaterMark -= count;
                if (_freeList.Count <= 0)
                    return;
                var lastFreeBlock = _freeList[_freeList.Count - 1];
                if (lastFreeBlock.Offset + lastFreeBlock.Count < _highWaterMark)
                    return;
                _highWaterMark = lastFreeBlock.Offset;
                _freeList.RemoveAt(_freeList.Count - 1);
                return;
            }

            if (_freeList.Count == 0)
            {
                _freeList.Add(new Range { Offset = offset, Count = count });
                return;
            }

            // Search for a region containing the offset.
            var ix = _freeList.BinarySearch(new Range { Offset = offset });
            System.Diagnostics.Debug.Assert(ix < 0, "Should not find a free region that has the exact offset being freed");
            // Find the free region the starts after the offset.
            var regionAfterIx = ~ix;

            // Load information about the region that ends at-or-before the region-to-free.
            var hasRegionBefore = regionAfterIx > 0;
            var regionBefore = hasRegionBefore ? _freeList[regionAfterIx - 1] : default;
            System.Diagnostics.Debug.Assert(!hasRegionBefore || regionBefore.Offset + regionBefore.Count <= offset);
            var regionBeforeAligns = hasRegionBefore && regionBefore.Offset + regionBefore.Count == offset;

            // Load information about the region that starts at-or-after the region-to-free
            var hasRegionAfter = regionAfterIx < _freeList.Count;
            var regionAfter = hasRegionAfter ? _freeList[regionAfterIx] : default;
            System.Diagnostics.Debug.Assert(!hasRegionAfter || regionAfter.Offset >= freeEnd);
            var regionAfterAligns = hasRegionAfter && regionAfter.Offset == freeEnd;

            // When the region-before aligns with the beginning of the region-to-free
            // and the region-after aligns with the end of the region-to-free, merge all three regions.
            if (regionBeforeAligns && regionAfterAligns)
            {
                regionBefore.Count += regionAfter.Count + count;
                _freeList[regionAfterIx - 1] = regionBefore;
                _freeList.RemoveAt(regionAfterIx);
                return;
            }

            // When the region-before aligns with the beginning of the region-to-free, expand the region-before.
            if (regionBeforeAligns)
            {
                regionBefore.Count += count;
                _freeList[regionAfterIx - 1] = regionBefore;
                return;
            }

            // When the region-after aligns with the beginning of the region-to-free, expand the region-after.
            if (regionAfterAligns)
            {
                regionAfter.Offset = offset;
                regionAfter.Count += count;
                _freeList[regionAfterIx] = regionAfter;
                return;
            }

            // Insert a new free region.
            _freeList.Insert(regionAfterIx, new Range
            {
                Offset = offset,
                Count = count,
            });
        }

        private bool Validate()
        {
            var count = 0u;
            var left = 0u;
            for (var i = 0; i < _freeList.Count; i++)
            {
                var gap = _freeList[i];
                if (i > 0) System.Diagnostics.Debug.Assert(gap.Offset > left);
                count += gap.Offset - left;
                left = gap.Offset + gap.Count;
            }

            if (left > 0) System.Diagnostics.Debug.Assert(_highWaterMark > left);
            count += _highWaterMark - left;
            System.Diagnostics.Debug.Assert(count == Allocated);
            return true;
        }

        #region Allocated Ranges

        /// <summary>
        /// Enumerates over the continuous allocated ranges within this allocator.
        /// </summary>
        public AllocatedRangesEnumerable AllocatedRanges => new AllocatedRangesEnumerable(this);

        public readonly struct AllocatedRangesEnumerable : IEnumerable<Range>
        {
            private readonly EqAllocator _owner;

            public AllocatedRangesEnumerable(EqAllocator owner) => _owner = owner;

            public AllocatedRangesEnumerator GetEnumerator() => new AllocatedRangesEnumerator(_owner);

            IEnumerator<Range> IEnumerable<Range>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct AllocatedRangesEnumerator : IEnumerator<Range>
        {
            private readonly EqAllocator _owner;
            private int _tail;

            public AllocatedRangesEnumerator(EqAllocator owner)
            {
                _owner = owner;
                _tail = -1;
            }

            public bool MoveNext()
            {
                _tail++;
                var free = _owner._freeList.Count;
                // Check if there's nothing before the first free chunk; if so skip it.
                if (_tail == 0 && free > 0 && _owner._freeList[0].Offset == 0)
                    _tail++;
                if (_tail > free) return false;
                // Check if there's nothing after the last free chunk; if so skip it.
                if (_tail > 0 && _tail == free)
                {
                    var left = _owner._freeList[_tail - 1];
                    if (left.Offset + left.Count == _owner._highWaterMark) return false;
                }

                return true;
            }

            public Range Current
            {
                get
                {
                    Range range;
                    if (_tail > 0)
                    {
                        var left = _owner._freeList[_tail - 1];
                        range.Offset = left.Offset + left.Count;
                    }
                    else
                        range.Offset = 0;

                    range.Count = (_tail < _owner._freeList.Count ? _owner._freeList[_tail].Offset : _owner._highWaterMark) - range.Offset;
                    return range;
                }
            }

            object IEnumerator.Current => Current;

            void IEnumerator.Reset() => _tail = -1;

            public void Dispose()
            {
            }
        }

        #endregion

        #region Compaction

        /// <summary>
        /// Compacts all the fragmented free regions to the end of the allocator.
        /// </summary>
        /// <returns>A disposable </returns>
        public CompactionReport Compact() => new CompactionReport(this);

        public struct CompactionRange : IComparable<CompactionRange>
        {
            public uint OldOffset;
            public uint LeftShift;
            public uint Count;

            public int CompareTo(CompactionRange other) => OldOffset.CompareTo(other.OldOffset);
        }

        public readonly struct CompactionReport : IDisposable
        {
            private readonly EqAllocator _owner;
            private readonly ArrayPoolToken<CompactionRange> _token;
            private readonly CompactionRange[] _ranges;
            private readonly int _count;

            internal CompactionReport(EqAllocator owner)
            {
                _owner = owner;
                _token = PoolManager.GetArray(owner._freeList.Count + 1, out _ranges);
                var shift = 0u;
                var left = 0u;
                var i = 0;
                foreach (var free in owner._freeList)
                {
                    AddRange(_ranges, free.Offset);
                    shift += free.Count;
                    left = free.Offset + free.Count;
                }

                AddRange(_ranges, owner._highWaterMark);

                _count = i;
                return;

                void AddRange(CompactionRange[] ranges, uint right)
                {
                    if (right > left)
                        ranges[i++] = new CompactionRange { OldOffset = left, LeftShift = shift, Count = right - left };
                }
            }

            public bool IsCompacted => _count > 0;

            public ReadOnlySpan<CompactionRange> Ranges => _ranges;

            public void UpdateIndex(ref uint pos) => pos = UpdateIndex(pos);

            public uint UpdateIndex(uint pos)
            {
                if (_count == 0) return pos;
                var ix = Array.BinarySearch(_ranges, 0, _count, new CompactionRange { OldOffset = pos });
                // Convert from the first element after the position to the last element before the position.
                if (ix < 0) ix = ~ix - 1;
                if (ix < 0)
                    throw new ArgumentException($"Offset {pos} falls outside of a compacted region");
                var region = _ranges[ix];
                System.Diagnostics.Debug.Assert(pos >= region.OldOffset);
                if (pos < region.OldOffset + region.Count) return pos - region.LeftShift;
                throw new ArgumentException($"Offset {pos} falls outside of a compacted region");
            }

            public void Dispose()
            {
                _owner._freeList.Clear();
                if (_count > 0) _owner._highWaterMark -= _ranges[_count - 1].LeftShift;
                // Not double-free safe, but this whole type isn't.
                var tok = _token;
                tok.Dispose();
            }
        }

        #endregion

        public struct Range : IComparable<Range>
        {
            public uint Offset;
            public uint Count;

            public int CompareTo(Range other) => Offset.CompareTo(other.Offset);
        }
    }
}