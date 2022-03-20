using System.Diagnostics.Contracts;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util
{
    public static class BubbleSort
    {
        public interface IBubbleSorter<TV>
        {
            /// <summary>
            /// Determines if A should be after B in the output order.
            /// </summary>
            [Pure]
            bool ShouldSwap(in TV a, in TV b);
        }

        public static void Sort<TV, TC>(ref TV a, ref TV b, ref TV c, ref TV d, in TC comparer) where TC : struct, IBubbleSorter<TV>
        {
            if (comparer.ShouldSwap(in a, in b))
                MyUtils.Swap(ref a, ref b);
            if (comparer.ShouldSwap(in c, in d))
                MyUtils.Swap(ref c, ref d);
            if (comparer.ShouldSwap(in a, in c))
                MyUtils.Swap(ref a, ref c);
            if (comparer.ShouldSwap(in b, in d))
                MyUtils.Swap(ref b, ref d);
            if (comparer.ShouldSwap(in b, in c))
                MyUtils.Swap(ref b, ref c);
        }
    }
}