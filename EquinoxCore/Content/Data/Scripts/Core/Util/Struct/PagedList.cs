using System;
using Equinox76561198048419394.Core.Util.Memory;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.Struct
{
    internal class PagedList<T>
    {
        private T[][] _pages;
        private readonly int _pageShift;
        private readonly uint _pageMask;
        private readonly uint _pageSize;

        public PagedList(int pageShift = 6)
        {
            _pages = Array.Empty<T[]>();
            _pageShift = pageShift;
            _pageMask = (uint)((1 << pageShift) - 1);
            _pageSize = (uint)(1 << _pageShift);
        }

        private T[] PageOrNull(uint pageIndex) => pageIndex < _pages.Length ? _pages[pageIndex] : null;

        private T[] Page(uint pageIndex)
        {
            if (pageIndex >= _pages.Length)
                Array.Resize(ref _pages, (int)MathHelper.GetNearestBiggerPowerOfTwo(pageIndex + 1));
            return _pages[pageIndex] ?? (_pages[pageIndex] = new T[_pageSize]);
        }

        internal Span<T> TryPageSegment(uint pageIndex, uint offsetInPage, uint count, out bool okay)
        {
            var page = PageOrNull(pageIndex);
            okay = page != null;
            return okay ? page.AsSpan((int)offsetInPage, (int)count) : default;
        }

        internal Span<T> PageSegment(uint pageIndex, uint offsetInPage, uint count) => Page(pageIndex).AsSpan((int)offsetInPage, (int)count);

        /// <summary>Accesses a continuous range of elements. It must be aligned within a single page of the underlying data.</summary>
        public Span<T> ContinuousRange(uint index, uint count) => Page(index >> _pageShift).AsSpan((int)(index & _pageMask), (int)count);

        public ref T this[uint index] => ref Page(index >> _pageShift)[(int)(index & _pageMask)];

        public void Clear(uint index, uint count)
        {
            foreach (var span in Range(index, count, true))
                span.Span.Clear();
        }

        public void Copy(uint srcIndex, uint dstIndex, uint count)
        {
            while (count > 0)
            {
                var srcPage = PageOrNull(srcIndex >> _pageShift);
                var srcIndexInPage = srcIndex & _pageMask;
                var dstPageIndex = dstIndex >> _pageShift;
                var dstPage = srcPage != null ? Page(dstPageIndex) : PageOrNull(dstPageIndex);
                var dstIndexInPage = dstIndex & _pageMask;
                var copy = Math.Min(count, _pageSize - Math.Max(srcIndexInPage, dstIndexInPage));
                if (srcPage != null)
                    Array.Copy(srcPage, srcIndexInPage, dstPage, dstIndexInPage, copy);
                else if (dstPage != null)
                    Array.Clear(dstPage, (int)dstIndexInPage, (int)copy);
                srcIndex += copy;
                dstIndex += copy;
                count -= copy;
            }
        }

        public void Compact(EqAllocator.CompactionReport report)
        {
            foreach (var range in report.Ranges)
                if (range.LeftShift != 0)
                    Copy(range.OldOffset, range.OldOffset - range.LeftShift, range.Count);
        }

        #region Range Enumerable

        /// <summary>
        /// Enumerates the continuous spans of memory within the range.
        /// </summary>
        /// <param name="offset">offset of the range to access</param>
        /// <param name="count">width of the range to access</param>
        /// <param name="skipUnAllocated">skip blocks that aren't allocated yet</param>
        public RangeEnumerable Range(uint offset, uint count, bool skipUnAllocated = false) => new RangeEnumerable(this, offset, count, skipUnAllocated);

        public readonly struct RangeEnumerable
        {
            private readonly PagedList<T> _owner;
            private readonly bool _skipUnAllocated;
            private readonly uint _offset;
            private readonly uint _count;

            internal RangeEnumerable(PagedList<T> owner, uint offset, uint count, bool skipUnAllocated)
            {
                _owner = owner;
                _skipUnAllocated = skipUnAllocated;
                _offset = offset;
                _count = count;
            }

            public RangeEnumerator GetEnumerator() => new RangeEnumerator(_owner, _offset, _count, _skipUnAllocated);
        }

        public struct RangeEnumerator
        {
            private readonly PagedList<T> _owner;
            private readonly bool _skipUnAllocated;
            private AlignedRangeEnumerator _base;

            internal RangeEnumerator(PagedList<T> owner, uint offset, uint count, bool skipUnAllocated)
            {
                _owner = owner;
                _skipUnAllocated = skipUnAllocated;
                _base = new AlignedRangeEnumerator(owner._pageShift, owner._pageMask, owner._pageSize, offset, count);
            }

            public bool MoveNext()
            {
                while (_base.MoveNext())
                {
                    if (!_skipUnAllocated || _owner.PageOrNull(_base.PageIndex) != null)
                        return true;
                }

                return false;
            }

            public RangeSpan Current => new RangeSpan(
                _base.Offset,
                _owner.PageSegment(_base.PageIndex, _base.OffsetInPage, _base.CountInPage));
        }

        public readonly ref struct RangeSpan
        {
            public readonly uint Offset;
            public readonly Span<T> Span;

            public RangeSpan(uint offset, Span<T> span)
            {
                Offset = offset;
                Span = span;
            }

            public int Length => Span.Length;

            public ref T this[int ix] => ref Span[ix];

            public Span<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

            public static implicit operator Span<T>(RangeSpan range) => range.Span;
        }

        #endregion
    }
}