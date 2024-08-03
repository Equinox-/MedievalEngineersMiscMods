using System.Collections.Concurrent;
using VRage.Collections;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    internal static class BinaryHeapPool<TKey, TValue>
    {
        private static readonly ConcurrentBag<MyBinaryHeap<TKey, TValue>> Pool = new ConcurrentBag<MyBinaryHeap<TKey, TValue>>();

        public static MyBinaryHeap<TKey, TValue> Get() => Pool.TryTake(out var val) ? val : new MyBinaryHeap<TKey, TValue>();

        public static void Return(ref MyBinaryHeap<TKey, TValue> val)
        {
            if (val == null) return;
            val.Clear();
            Pool.Add(val);
            val = null;
        }
    }
}