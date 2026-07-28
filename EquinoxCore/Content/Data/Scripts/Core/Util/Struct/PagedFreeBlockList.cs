using System;
using Equinox76561198048419394.Core.Util.Memory;

namespace Equinox76561198048419394.Core.Util.Struct
{
    internal sealed class PagedFreeBlockList<T>
    {
        private readonly EqAllocator _allocator = new EqAllocator();
        private readonly PagedList<T> _pages;
        private readonly bool _clearOnFree;
        public uint Allocated => _allocator.Allocated;

        public PagedFreeBlockList(int pageShift = 6, bool clearOnFree = false)
        {
            _pages = new PagedList<T>(pageShift);
            _clearOnFree = clearOnFree;
        }

        public ref T this[uint index] => ref _pages[index];

        /// <summary>
        /// Allocates a new continuous block and returns the offset.
        /// </summary>
        public uint Allocate(uint count) => _allocator.Allocate(count);

        public uint Reallocate(uint offset, uint oldCount, uint newCount)
        {
            if (oldCount == 0)
                return Allocate(newCount);
            if (newCount == oldCount)
                return offset;
            if (newCount < oldCount)
            {
                // Shrink-in-place
                Free(offset + newCount, oldCount - newCount);
                return offset;
            }

            // Attempt expand-in-place.
            if (_allocator.TryAllocateExact(offset + oldCount, newCount - oldCount))
                return offset;

            // Allocate new block and expand.
            var newOffset = Allocate(newCount);
            _pages.Copy(offset, newOffset, oldCount);
            Free(offset, oldCount);
            return newOffset;
        }

        /// <summary>
        /// Frees a continuous block.
        /// </summary>
        public void Free(uint offset, uint count)
        {
            if (_clearOnFree)
                _pages.Clear(offset, count);

            _allocator.Free(offset, count);
        }

        public void Copy(uint srcIndex, uint dstIndex, uint count) => _pages.Copy(srcIndex, dstIndex, count);

        public PagedList<T>.RangeEnumerable Range(uint offset, uint count, bool skipUnAllocated = false) => _pages.Range(offset, count, skipUnAllocated);

        #region Enumerator

        public AllocatedEnumerable AllocatedItems => new AllocatedEnumerable(this);

        public readonly struct AllocatedEnumerable
        {
            private readonly PagedFreeBlockList<T> _owner;

            public AllocatedEnumerable(PagedFreeBlockList<T> owner) => _owner = owner;

            public AllocatedEnumerator GetEnumerator() => new AllocatedEnumerator(_owner);
        }

        public struct AllocatedEnumerator : IDisposable
        {
            private readonly PagedFreeBlockList<T> _owner;
            private EqAllocator.AllocatedRangesEnumerator _ranges;
            private bool _spansInit;
            private PagedList<T>.RangeEnumerator _spans;

            public AllocatedEnumerator(PagedFreeBlockList<T> owner)
            {
                _owner = owner;
                _ranges = owner._allocator.AllocatedRanges.GetEnumerator();
                _spansInit = false;
                _spans = default;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_spansInit && _spans.MoveNext())
                        return true;
                    if (!_ranges.MoveNext())
                        return false;
                    var range = _ranges.Current;
                    _spansInit = true;
                    _spans = _owner._pages.Range(range.Offset, range.Count).GetEnumerator();
                }
            }

            public PagedList<T>.RangeSpan Current => _spans.Current;

            public void Dispose()
            {
                _ranges.Dispose();
            }
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
            private readonly PagedFreeBlockList<T> _owner;
            private readonly EqAllocator.CompactionReport _report;

            public CompactionReport(PagedFreeBlockList<T> owner)
            {
                _owner = owner;
                _report = owner._allocator.Compact();
            }

            public bool IsCompacted => _report.IsCompacted;

            public ReadOnlySpan<EqAllocator.CompactionRange> Ranges => _report.Ranges;

            public void UpdateIndex(ref uint pos) => _report.UpdateIndex(ref pos);

            public uint UpdateIndex(uint pos) => _report.UpdateIndex(pos);

            public void Dispose()
            {
                if (IsCompacted) _owner._pages.Compact(_report);
                _report.Dispose();
            }
        }

        #endregion
    }
}