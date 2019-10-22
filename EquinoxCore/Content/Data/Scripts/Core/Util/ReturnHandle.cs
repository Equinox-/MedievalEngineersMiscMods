using System;
using VRage.Library.Collections.Concurrent;

namespace Equinox76561198048419394.Core.Util
{
    public struct ReturnHandle<T> : IDisposable where T : class, new()
    {
        public T Handle;
        private MyConcurrentPool<T> _pool;

        public ReturnHandle(MyConcurrentPool<T> pool)
        {
            _pool = pool;
            Handle = pool.Get();
        }

        public void Dispose()
        {
            _pool.Return(Handle);
            _pool = null;
            Handle = null;
        }
    }

    public static class ReturnHandleExtensions
    {
        public static ReturnHandle<T> GetHandle<T>(this MyConcurrentPool<T> pool) where T : class, new()
        {
            return new ReturnHandle<T>(pool);
        }

        public static ReturnHandle<T> Get<T>(this MyConcurrentPool<T> pool, out T result) where T : class, new()
        {
            var rh = new ReturnHandle<T>(pool);
            result = rh.Handle;
            return rh;
        }
    }
}