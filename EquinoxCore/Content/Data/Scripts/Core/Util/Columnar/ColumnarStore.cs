using System;
using System.Collections;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using Equinox76561198048419394.Core.Util.Struct;

namespace Equinox76561198048419394.Core.Util.Columnar
{
    // ReSharper disable once UnusedTypeParameter
    public readonly struct ColumnReference<TK, TV> where TK : struct, IColumnarStoreRow<TK>
    {
        internal readonly int Column;

        internal ColumnReference(int column) => Column = column;

        public ref TV Access(ColumnarStore<TK> store, TK key, uint strideElement = 0) => ref store.Access(key, this, strideElement);

        public Span<TV> AccessSpan(ColumnarStore<TK> store, TK key) => store.AccessSpan(key, this);
    }

    public sealed class ColumnarStore<TK> where TK : struct, IColumnarStoreRow<TK>
    {
        private const int PageShift = 6;
        private const uint PageMask = (1 << PageShift) - 1;
        private const uint PageSize = 1 << PageShift;

        private readonly EqAllocator _allocator = new EqAllocator();
        private readonly List<IColumn> _components = new List<IColumn>();

        public ColumnReference<TK, TV> AddColumn<TV>(bool clearOnFree = false, int strideShift = 0)
        {
            var ix = _components.Count;
            _components.Add(new ColumnImpl<TV>(clearOnFree, strideShift));
            return new ColumnReference<TK, TV>(ix);
        }

        public uint AllocatedRows => _allocator.Allocated;

        public TK AddRow() => default(TK).Create(_allocator.Allocate(1));

        public ColumnarStoreRows<TK> AddRows(uint count) => new ColumnarStoreRows<TK>(_allocator.Allocate(count), count);

        public void RemoveRow(TK row)
        {
            var ix = row.Value;
            foreach (var component in _components)
                component.OnFree(ix);
            _allocator.Free(ix, 1);
        }

        private ColumnImpl<TV> Column<TV>(ColumnReference<TK, TV> col) => (ColumnImpl<TV>)_components[col.Column];

        public Span<TV> AccessSpan<TV>(TK key, ColumnReference<TK, TV> col) => Column(col).AccessSpan(key.Value);

        public ref TV Access<TV>(TK key, ColumnReference<TK, TV> col, uint strideElement = 0) => ref Column(col).Access(key.Value, strideElement);

        #region Row Enumeration

        public RowEnumerable Rows => new RowEnumerable(this);

        public readonly struct RowEnumerable : IEnumerable<RowSegment>
        {
            private readonly ColumnarStore<TK> _owner;

            internal RowEnumerable(ColumnarStore<TK> owner) => _owner = owner;

            public RowEnumerator GetEnumerator() => new RowEnumerator(_owner);
            IEnumerator<RowSegment> IEnumerable<RowSegment>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct RowEnumerator : IEnumerator<RowSegment>
        {
            private readonly ColumnarStore<TK> _owner;
            private EqAllocator.AllocatedRangesEnumerator _ranges;
            private bool _alignedInit;
            private AlignedRangeEnumerator _aligned;

            public RowEnumerator(ColumnarStore<TK> owner)
            {
                _owner = owner;
                _ranges = owner._allocator.AllocatedRanges.GetEnumerator();
                _alignedInit = false;
                _aligned = default;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_alignedInit && _aligned.MoveNext())
                        return true;
                    if (!_ranges.MoveNext())
                        return false;
                    var range = _ranges.Current;
                    _alignedInit = true;
                    _aligned = new AlignedRangeEnumerator(PageShift, PageMask, PageSize, range.Offset, range.Count);
                }
            }

            public RowSegment Current => new RowSegment(_owner, _aligned.Offset, _aligned.PageIndex, _aligned.OffsetInPage, _aligned.CountInPage);
            object IEnumerator.Current => Current;
            void IEnumerator.Reset() => throw new NotImplementedException();
            public void Dispose() => _ranges.Dispose();
        }

        public readonly struct RowSegment : IReadOnlyList<TK>
        {
            private readonly ColumnarStore<TK> _owner;
            private readonly uint _offset;
            private readonly uint _pageIndex;
            private readonly uint _offsetInPage;
            public readonly uint Count;

            public RowSegment(ColumnarStore<TK> owner, uint offset, uint pageIndex, uint offsetInPage, uint count)
            {
                _owner = owner;
                _offset = offset;
                _pageIndex = pageIndex;
                _offsetInPage = offsetInPage;
                Count = count;
            }

            public TK RowKey(uint offsetInSegment) => default(TK).Create(_offset + offsetInSegment);

            public Span<TV> TryColumn<TV>(ColumnReference<TK, TV> col, out bool okay)
                => _owner.Column(col).TryPageSegment(_pageIndex, _offsetInPage, Count, out okay);

            public Span<TV> Column<TV>(ColumnReference<TK, TV> col)
                => _owner.Column(col).PageSegment(_pageIndex, _offsetInPage, Count);

            public ColumnarStoreRowKeyEnumerator<TK> GetEnumerator() => new ColumnarStoreRowKeyEnumerator<TK>(_offset, Count);

            IEnumerator<TK> IEnumerable<TK>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            int IReadOnlyCollection<TK>.Count => (int)Count;

            public TK this[int index] => this[(uint)index];
            public TK this[uint index] => RowKey(index);
        }

        #endregion

        #region Column Impl

        private interface IColumn
        {
            void OnFree(uint ix);
            void Compact(in EqAllocator.CompactionReport report);
        }

        private sealed class ColumnImpl<TV> : IColumn
        {
            private readonly bool _clearOnFree;
            private readonly uint _stride;
            private readonly int _strideShift;
            private readonly PagedList<TV> _values;

            public ColumnImpl(bool clearOnFree, int strideShift)
            {
                _clearOnFree = clearOnFree;
                _strideShift = strideShift;
                _stride = 1u << strideShift;
                _values = new PagedList<TV>(PageShift + strideShift);
            }

            internal Span<TV> TryPageSegment(uint pageIndex, uint offsetInPage, uint count, out bool okay)
                => _values.TryPageSegment(pageIndex, offsetInPage << _strideShift, count << _strideShift, out okay);

            internal Span<TV> PageSegment(uint pageIndex, uint offsetInPage, uint count)
                => _values.PageSegment(pageIndex, offsetInPage << _strideShift, count << _strideShift);

            internal Span<TV> AccessSpan(uint ix) => _values.ContinuousRange(ix << _strideShift, _stride);

            internal ref TV Access(uint ix, uint strideElement = 0) => ref _values[ix << _strideShift | strideElement];

            void IColumn.OnFree(uint ix)
            {
                if (_clearOnFree) _values[ix] = default;
            }

            void IColumn.Compact(in EqAllocator.CompactionReport report) => _values.Compact(report);
        }

        #endregion

        /// <summary>
        /// Compacts all free regions of this list.
        /// Callers should update their references using the returned value.
        /// Then, the actual compaction of stored values occurs when the returned value is disposed.
        /// </summary>
        public CompactionReport Compact() => new CompactionReport(this);

        public readonly struct CompactionReport : IDisposable
        {
            private readonly ColumnarStore<TK> _owner;
            private readonly EqAllocator.CompactionReport _report;

            public CompactionReport(ColumnarStore<TK> owner)
            {
                _owner = owner;
                _report = owner._allocator.Compact();
            }

            public bool IsCompacted => _report.IsCompacted;

            public void UpdateRef(ref TK row) => row = UpdateRef(row);

            public TK UpdateRef(TK row) => row.IsNull() ? row : default(TK).Create(_report.UpdateIndex(row.Value));

            public void Dispose()
            {
                if (IsCompacted)
                    foreach (var col in _owner._components)
                        col.Compact(in _report);
                _report.Dispose();
            }
        }
    }
}