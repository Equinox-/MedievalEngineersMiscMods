using System;
using System.Collections.Generic;
using VRage.Library.Threading;

namespace Equinox76561198048419394.Core.Util
{
    public class LruCache<TK, TV>
    {
        private struct CacheItem
        {
            public readonly TK Key;
            public readonly TV Value;

            public CacheItem(TK key, TV value)
            {
                Key = key;
                Value = value;
            }
        }

        private readonly Dictionary<TK, LinkedListNode<CacheItem>> _cache;
        private readonly LinkedList<CacheItem> _lruCache;
        private readonly int _capacity;
        private readonly FastResourceLock _lock;

        public LruCache(int capacity, IEqualityComparer<TK> comparer = null)
        {
            _cache = new Dictionary<TK, LinkedListNode<CacheItem>>(capacity > 1024 ? (int)Math.Sqrt(capacity) : capacity, comparer ?? EqualityComparer<TK>.Default);
            _lruCache = new LinkedList<CacheItem>();
            _capacity = capacity;
            _lock = new FastResourceLock();
        }
        
        public TV GetOrCreate(TK key, Func<TK, TV> del)
        {
            using (_lock.AcquireExclusiveUsing())
            {
                TV res;
                if (TryGetUnsafe(key, out res)) return res;
                if (_cache.Count >= _capacity)
                    while (_cache.Count >= _capacity / 1.5)
                    {
                        _cache.Remove(_lruCache.First.Value.Key);
                        _lruCache.RemoveFirst();
                    }

                var node = new LinkedListNode<CacheItem>(new CacheItem(key, del(key)));
                _lruCache.AddLast(node);
                _cache.Add(key, node);
                return node.Value.Value;
            }
        }

        public void Clear()
        {
            using (_lock.AcquireExclusiveUsing())
            {
                this._cache.Clear();
                this._lruCache.Clear();
            }
        }

        public TV Store(TK key, TV value)
        {
            using (_lock.AcquireExclusiveUsing())
            {
                var node = new LinkedListNode<CacheItem>(new CacheItem(key, value));
                _lruCache.AddLast(node);
                _cache[key] = node;
                return node.Value.Value;
            }
        }

        private bool TryGetUnsafe(TK key, out TV value)
        {
            LinkedListNode<CacheItem> node;
            if (_cache.TryGetValue(key, out node))
            {
                _lruCache.Remove(node);
                _lruCache.AddLast(node);
                value = node.Value.Value;
                return true;
            }
            value = default(TV);
            return false;
        }

        public bool TryGet(TK key, out TV value)
        {
            using (_lock.AcquireExclusiveUsing())
                return TryGetUnsafe(key, out value);
        }
    }
}
