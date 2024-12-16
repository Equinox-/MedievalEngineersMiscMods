using System;
using System.Collections.Generic;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util.Memory
{
    public static class SpanUtils
    {
        public static EqSpan<T> AsEqSpan<T>(this List<T> list) => new EqSpan<T>(list.GetInternalArray(), 0, list.Count);
        public static EqSpan<T> AsEqSpan<T>(this T[] array) => new EqSpan<T>(array, 0, array.Length);
        public static EqSpan<T> AsEqSpan<T>(this ArraySegment<T> array) => new EqSpan<T>(array.Array, array.Offset, array.Count);

        public static void AddSpan<T>(this List<T> list, EqReadOnlySpan<T> span)
        {
            list.EnsureCapacity(list.Count + span.Length);
            span.CopyTo(list.GetInternalArray(), list.Count);
            list.SetSize(list.Count + span.Length);
        }

        public static void Reverse<T>(this EqSpan<T> span)
        {
            for (int i = 0, j = span.Length - 1; i < j; i++, j--)
                MyUtils.Swap(ref span[i], ref span[j]);
        }
    }
}