using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public class ArrayPool<T>
    {
        private static readonly ArrayPool<T> Instance = new ArrayPool<T>();

        private ArrayPool()
        {
        }

        public static ReturnHandle Get(int minSize, out T[] array)
        {
            array = Instance.GetInternal(minSize);
            return new ReturnHandle(Instance, array);
        }

        public static T[] Get(int minSize)
        {
            return Instance.GetInternal(minSize);
        }

        public static void Return(T[] handle)
        {
            Instance.ReturnInternal(handle);
        }
        
        private ConcurrentBag<T[]>[] _pools = new ConcurrentBag<T[]>[64];

        private const int MinBucket = 8;
        
        // ReSharper disable InconsistentlySynchronizedField
        private T[] GetInternal(int minSize)
        {
            var bucket = Math.Max(MathHelper.Log2Ceiling(minSize) - MinBucket, 0);
            if (bucket >= _pools.Length)
                lock (this)
                {
                    if (bucket < _pools.Length)
                        Array.Resize(ref _pools, MathHelper.GetNearestBiggerPowerOfTwo(bucket) + 1);
                }

            // ReSharper disable once InvertIf
            if (_pools[bucket] == null)
                lock (this)
                {
                    if (_pools[bucket] == null)
                        _pools[bucket] = new ConcurrentBag<T[]>();
                }

            return _pools[bucket].TryTake(out var res) ? res : new T[1 << (MinBucket + bucket)];
        }
        // ReSharper restore InconsistentlySynchronizedField

        private void ReturnInternal(T[] chunk)
        {
            var bucket = MathHelper.Log2Floor(chunk.Length);
            if (bucket < MinBucket)
                return;

            _pools[bucket - MinBucket].Add(chunk);
        }


        public struct ReturnHandle : IDisposable
        {
            public T[] Handle;
            private ArrayPool<T> _pool;

            public ReturnHandle(ArrayPool<T> pool, T[] handle)
            {
                _pool = pool;
                Handle = handle;
            }

            public void Dispose()
            {
                _pool.ReturnInternal(Handle);
                _pool = null;
                Handle = null;
            }
        }
    }
}