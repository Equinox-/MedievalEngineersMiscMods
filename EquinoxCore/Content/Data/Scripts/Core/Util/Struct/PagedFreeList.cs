using System;
using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util.Struct
{
    public sealed class PagedFreeList<T> where T : struct
    {
        private readonly List<uint> _freeList = new List<uint>();
        private readonly List<T[]> _pages = new List<T[]>();
        private readonly int _pageShift;
        private readonly int _pageMask;
        private int _freeRevision;
        private uint _highWaterMark;

        public PagedFreeList(int pageShift = 6)
        {
            _pageShift = pageShift;
            _pageMask = (1 << pageShift) - 1;
        }

        public ref T this[uint index] => ref _pages[(int)(index >> _pageShift)][index & _pageMask];

        public uint AllocateIndex()
        {
            uint slot;
            if (_freeList.Count > 0)
            {
                var last = _freeList.Count - 1;
                slot = _freeList[last];
                _freeList.RemoveAt(last);
                Count++;
                return slot;
            }

            slot = _highWaterMark++;
            while (slot >> _pageShift >= _pages.Count)
                _pages.Add(new T[1 << _pageShift]);
            Count++;
            return slot;
        }

        public Handle AllocateVersioned() => Versioned(AllocateIndex());

        public void Free(uint index)
        {
            _freeRevision++;
            _freeList.Add(index);
            --Count;
        }

        public int Count { get; private set; }

        public void Clear()
        {
            _freeRevision++;
            _highWaterMark = 0;
            Count = 0;
            _freeList.Clear();
        }

        private void CheckFreeRevision(int freeRevision)
        {
            if (_freeRevision != freeRevision)
                throw new Exception("Concurrent free of PagedFreeList");
        }

        public Handle Versioned(uint index) => new Handle(this, _freeRevision, index);

        public readonly struct Handle
        {
            private readonly PagedFreeList<T> _backing;
            private readonly int _freeRevision;
            private readonly uint _index;

            internal Handle(PagedFreeList<T> backing, int freeRevision, uint index)
            {
                _backing = backing;
                _freeRevision = freeRevision;
                _index = index;
            }

            public bool IsValid => _backing != null && _backing._freeRevision == _freeRevision;

            // ReSharper disable once ConvertToAutoPropertyWhenPossible
            public uint Index => _index;

            public ref T Value
            {
                get
                {
                    _backing.CheckFreeRevision(_freeRevision);
                    return ref _backing[_index];
                }
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<Handle>
        {
            private readonly PagedFreeList<T> _backing;
            private readonly int _freeRevision;
            private uint _index;

            // -1 when iteration starts, then points to the first index in free list that is >= _index.
            private int _freeIndex;

            public Enumerator(PagedFreeList<T> backing)
            {
                _backing = backing;
                _freeRevision = backing._freeRevision;
                _backing._freeList.Sort();
                _index = 0;
                _freeIndex = -1;
            }

            public bool MoveNext()
            {
                _backing.CheckFreeRevision(_freeRevision);
                var freeList = _backing._freeList;
                var highWaterMark = _backing._highWaterMark;
                while (true)
                {
                    if (_freeIndex == -1)
                        _freeIndex = 0;
                    else
                        _index++;
                    if (_index >= highWaterMark) return false;
                    while (true)
                    {
                        if (_freeIndex >= freeList.Count)
                            return true;
                        var freeValue = freeList[_freeIndex];
                        if (freeValue > _index)
                            return true;
                        if (freeValue == _index)
                            break;
                        _freeIndex++;
                    }
                }
            }

            public void Reset()
            {
                _index = 0;
                _freeIndex = -1;
            }

            public Handle Current => new Handle(_backing, _freeRevision, _index);

            public void Dispose()
            {
            }

            object IEnumerator.Current => Current;
        }
    }
}