using System;
using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util.Columnar
{
    public static class ColumnarStoreRow
    {
        private const uint NullValue = uint.MaxValue;
        public static T Null<T>() where T : struct, IColumnarStoreRow<T> => default(T).Create(NullValue);
        public static bool IsNull<T>(this T val) where T : struct, IColumnarStoreRow<T> => val.Value == NullValue;

        public static ColumnarStoreRows<T> Range<T>(this T val, uint count) where T : struct, IColumnarStoreRow<T>
            => new ColumnarStoreRows<T>(val.Value, count);
    }

    public interface IColumnarStoreRow<T> : IEquatable<T>, IComparable<T> where T : struct, IColumnarStoreRow<T>
    {
        uint Value { get; }
        T Create(uint value);
    }

    public readonly struct ColumnarStoreRows<TK> : IReadOnlyList<TK> where TK : struct, IColumnarStoreRow<TK>
    {
        private readonly uint _offset;
        private readonly uint _count;

        internal ColumnarStoreRows(uint offset, uint count)
        {
            _offset = offset;
            _count = count;
        }

        public ColumnarStoreRowKeyEnumerator<TK> GetEnumerator() => new ColumnarStoreRowKeyEnumerator<TK>(_offset, _count);

        IEnumerator<TK> IEnumerable<TK>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public TK Offset => default(TK).Create(_offset);
        public int Count => (int)_count;

        public TK this[int index] => this[(uint)index];
        public TK this[uint index] => default(TK).Create(_offset + index);
    }

    public struct ColumnarStoreRowKeyEnumerator<TK> : IEnumerator<TK> where TK : struct, IColumnarStoreRow<TK>
    {
        private uint _offset;
        private uint _count;

        internal ColumnarStoreRowKeyEnumerator(uint offset, uint count)
        {
            _offset = offset;
            _count = count;
        }

        public bool MoveNext()
        {
            if (_count == 0) return false;
            _offset++;
            _count--;
            return true;
        }

        void IEnumerator.Reset() => throw new NotImplementedException();

        public TK Current => default(TK).Create(_offset - 1);

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }
    }
}