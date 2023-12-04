using System;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class BitSetUtils
    {
        public static int LongsForBits(int bits) => bits + 63 / 64;

        public static Span<T> AsSpan<T>(this List<T> list) => list.GetInternalArray().AsSpan().Slice(0, list.Count);

        public static bool TryGetFirstBit(this Span<ulong> bitset, out int index, bool bitValue = true)
        {
            var skip = bitValue ? 0 : ulong.MaxValue;
            for (var i = 0; i < bitset.Length; i++)
            {
                ref var value = ref bitset[i];
                if (value == skip)
                    continue;
                var mask = 1ul;
                for (var j = 0; j < 64; j++)
                {
                    if ((value & mask) != 0 == bitValue)
                    {
                        index = (i << 6) + j;
                        return true;
                    }

                    mask <<= 1;
                }
            }

            index = default;
            return false;
        }

        public static bool GetBit(this Span<ulong> bitset, int index)
        {
            var entry = bitset[index >> 6];
            var maskForEntry = 1UL << (index & 63);
            return (entry & maskForEntry) != 0;
        }

        public static void SetBit(this Span<ulong> bitset, int index, bool val)
        {
            ref var entry = ref bitset[index >> 6];
            var maskForEntry = 1UL << (index & 63);
            if (val)
                entry |= maskForEntry;
            else
                entry &= ~maskForEntry;
        }
    }
}