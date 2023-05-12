using System;
using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util.Memory
{
    public readonly struct EqSpan<T> : IEnumerable<T>
    {
        private readonly T[] _array;
        private readonly int _offset;
        public readonly int Length;

        public EqSpan(T[] array, int offset, int length)
        {
            _array = array;
            _offset = offset;
            Length = length;
        }

        public static implicit operator EqReadOnlySpan<T>(EqSpan<T> span) => new EqReadOnlySpan<T>(span._array, span._offset, span.Length);

        public ref T this[int index] => ref _array[index + _offset];

        public void CopyTo(T[] dest, int offset) => Array.Copy(_array, _offset, dest, offset, Length);

        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        
        public struct Enumerator : IEnumerator<T>
        {
            private EqSpan<T> _array;
            private int _currentIndex;

            public Enumerator(EqSpan<T> array)
            {
                _array = array;
                _currentIndex = -1;
            }

            public T Current => _array[_currentIndex];

            public void Dispose()
            {
            }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                ++_currentIndex;
                return _currentIndex < _array.Length;
            }

            public void Reset()
            {
                _currentIndex = -1;
            }
        }

        public EqSpan<T> Slice(int offset, int count) => new EqSpan<T>(_array, _offset + offset, count);
    }
}