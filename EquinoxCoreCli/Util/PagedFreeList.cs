using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public sealed class PagedFreeList<T>
    {
        private readonly List<uint> _freeList = new List<uint>();
        private readonly List<T[]> _pages = new List<T[]>();
        private readonly int _pageShift;
        private readonly int _pageMask;
        public uint HighWaterMark { get; private set; }

        public PagedFreeList(int pageShift = 5)
        {
            _pageShift = pageShift;
            _pageMask = (1 << pageShift) - 1;
        }

        public ref T this[uint index] => ref _pages[(int)(index >> _pageShift)][index & _pageMask];

        public uint Allocate()
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

            slot = HighWaterMark++;
            while (slot >> _pageShift >= _pages.Count)
                _pages.Add(new T[1 << _pageShift]);
            Count++;
            return slot;
        }

        public void Free(uint index)
        {
            _freeList.Add(index);
            --Count;
        }

        public int Count { get; private set; }

        public void Clear()
        {
            HighWaterMark = 0;
            Count = 0;
            _freeList.Clear();
        }
    }
}