using System.Collections;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Util.Struct
{
    public class OffloadedDictionary<TK, TV> : IEnumerable<OffloadedDictionary<TK, TV>.KeyedHandle> where TV : struct
    {
        private readonly PagedFreeList<TV> _storage;
        private readonly Dictionary<TK, uint> _lookup;

        public OffloadedDictionary(IEqualityComparer<TK> comparer = null)
        {
            _lookup = new Dictionary<TK, uint>(comparer);
            _storage = new PagedFreeList<TV>();
        }

        public Dictionary<TK, uint>.KeyCollection Keys => _lookup.Keys;
        public ValueCollection Values => new ValueCollection(this);
        public int Count => _lookup.Count;

        public bool ContainsKey(in TK key) => _lookup.ContainsKey(key);

        public void Clear()
        {
            _lookup.Clear();
            _storage.Clear();
        }

        public ref TV Add(in TK key)
        {
            var index = _storage.AllocateIndex();
            try
            {
                _lookup.Add(key, index);
                return ref _storage[index];
            }
            catch
            {
                _storage.Free(index);
                throw;
            }
        }

        public void Add(in TK key, in TV value) => Add(in key) = value;

        public PagedFreeList<TV>.Handle AddHandle(in TK key)
        {
            var index = _storage.AllocateIndex();
            try
            {
                _lookup.Add(key, index);
                return _storage.Versioned(index);
            }
            catch
            {
                _storage.Free(index);
                throw;
            }
        }

        public ref TV this[in TK key] => ref _storage[_lookup[key]];

        public bool TryGetValue(in TK key, out PagedFreeList<TV>.Handle handle)
        {
            if (!_lookup.TryGetValue(key, out var index))
            {
                handle = default;
                return false;
            }

            handle = _storage.Versioned(index);
            return true;
        }

        public bool Remove(in TK key)
        {
            if (!_lookup.TryGetValue(key, out var index))
                return false;
            _lookup.Remove(key);
            _storage.Free(index);
            return true;
        }

        public readonly struct KeyedHandle
        {
            public readonly TK Key;
            private readonly PagedFreeList<TV>.Handle _handle;

            internal KeyedHandle(OffloadedDictionary<TK, TV> backing, in TK key, uint index)
            {
                Key = key;
                _handle = backing._storage.Versioned(index);
            }

            public ref TV Value => ref _handle.Value;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<KeyedHandle> IEnumerable<KeyedHandle>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<KeyedHandle>
        {
            private readonly OffloadedDictionary<TK, TV> _backing;
            private Dictionary<TK, uint>.Enumerator _enumerator;

            internal Enumerator(OffloadedDictionary<TK, TV> backing)
            {
                _backing = backing;
                _enumerator = backing._lookup.GetEnumerator();
            }

            public void Dispose() => _enumerator.Dispose();

            public bool MoveNext() => _enumerator.MoveNext();

            public void Reset() => ((IEnumerator)_enumerator).Reset();
            object IEnumerator.Current => Current;

            public KeyedHandle Current
            {
                get
                {
                    var curr = _enumerator.Current;
                    return new KeyedHandle(_backing, curr.Key, curr.Value);
                }
            }
        }

        public readonly struct ValueCollection
        {
            private readonly OffloadedDictionary<TK, TV> _offloaded;

            public ValueCollection(OffloadedDictionary<TK, TV> offloaded) => _offloaded = offloaded;

            public PagedFreeList<TV>.Enumerator GetEnumerator() => _offloaded._storage.GetEnumerator();
        }
    }
}