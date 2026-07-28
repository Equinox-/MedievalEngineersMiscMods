using System;

namespace Equinox76561198048419394.Core.Util.Memory
{
    internal struct AlignedRangeEnumerator
    {
        private readonly int _pageShift;
        private readonly uint _pageSize;
        private bool _init;
        private uint _count;
        public uint CountInPage { get; private set; }
        public uint PageIndex { get; private set; }
        public uint OffsetInPage { get; private set; }

        internal AlignedRangeEnumerator(int pageShift, uint pageMask, uint pageSize, uint offset, uint count)
        {
            _pageShift = pageShift;
            _pageSize = pageSize;
            _init = false;
            _count = count;
            PageIndex = offset >> pageShift;
            OffsetInPage = offset & pageMask;
            CountInPage = Math.Min(_count, _pageSize - OffsetInPage);
        }

        public uint Offset => (PageIndex << _pageShift) | OffsetInPage;

        public bool MoveNext()
        {
            if (_count == 0)
                return false;
            if (_init)
            {
                PageIndex++;
                _count -= CountInPage;
                OffsetInPage = 0;
                CountInPage = Math.Min(_count, _pageSize);
            }
            else
                _init = true;

            return true;
        }
    }
}