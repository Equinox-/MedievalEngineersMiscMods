using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Library.Collections;
using VRage.Library.Collections.Concurrent;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public class InterningBag<T> : IEquatable<InterningBag<T>>, IEnumerable<T>
    {
        private static readonly MyConcurrentPool<InterningBag<T>> Pool = new MyConcurrentPool<InterningBag<T>>();

        private static readonly ConcurrentDictionary<InterningBag<T>, InterningBag<T>>
            InternPool = new ConcurrentDictionary<InterningBag<T>, InterningBag<T>>(SlowInterningComparer.Instance);

        private static readonly T[] EmptyArray = new T[0];
        private static readonly IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        public static readonly InterningBag<T> Empty = new InterningBag<T>();

        private class SlowInterningComparer : IEqualityComparer<InterningBag<T>>
        {
            public static readonly SlowInterningComparer Instance = new SlowInterningComparer();
            
            private SlowInterningComparer()
            {
            }

            public bool Equals(InterningBag<T> x, InterningBag<T> y)
            {
                if (x == null)
                    return y == null;
                if (ReferenceEquals(x, y))
                    return true;
                if (y == null)
                    return false;
                if (x._backingSize != y._backingSize)
                    return false;
                for (var i = 0; i < x._backingSize; i++)
                {
                    var selfId = x._backing[i];
                    var has = false;
                    for (var j = 0; j < y._backingSize; j++)
                    {
                        var otherId = y._backing[j];
                        if (!Comparer.Equals(selfId, otherId))
                            continue;
                        has = true;
                        break;
                    }

                    if (!has)
                        return false;
                }

                return true;
            }

            public int GetHashCode(InterningBag<T> obj) => obj.GetHashCode();
        }

        private static InterningBag<T> Intern(InterningBag<T> tmp)
        {
            while (true)
            {
                if (InternPool.TryGetValue(tmp, out var interned))
                {
                    if (Pool.Count < 1000)
                        Pool.Return(tmp);
                    return interned;
                }

                // ReSharper disable once InvertIf
                if (InternPool.TryAdd(tmp, tmp))
                {
                    // Interned objects should have a minimal footprint.
                    Array.Resize(ref tmp._backing, tmp._backingSize);
                    return tmp;
                }
            }
        }

        // Guaranteed to not have any repeats
        private T[] _backing = EmptyArray;
        private int _backingSize = 0;
        private int? _memorizedHash;

        public int Count => _backingSize;

        public override bool Equals(object obj)
        {
            return obj is InterningBag<T> bag && Equals(bag);
        }

        public bool Equals(InterningBag<T> other)
        {
            return ReferenceEquals(other, this);
        }

        public override int GetHashCode()
        {
            // ReSharper disable NonReadonlyMemberInGetHashCode
            var tmp = _memorizedHash;
            if (tmp.HasValue)
                return tmp.Value;
            var hash = 961;
            for (var i = 0; i < _backingSize; i++)
            {
                var eh = Comparer.GetHashCode(_backing[i]);
                hash ^= (eh + 31) * (eh + 23) * (eh + 3);
            }

            _memorizedHash = hash;
            // ReSharper restore NonReadonlyMemberInGetHashCode
            return hash;
        }

        public bool Contains(T id)
        {
            foreach (var e in this)
                if (Comparer.Equals(e, id))
                    return true;
            return false;
        }

        #region Mutators

        private void EnsureSize(int i)
        {
            if (_backing.Length < i)
                Array.Resize(ref _backing, MathHelper.GetNearestBiggerPowerOfTwo(i));
        }

        public InterningBag<T> With(T id)
        {
            if (Contains(id))
                return this;
            var result = Pool.Get();
            result.EnsureSize(_backingSize + 1);
            Array.Copy(_backing, result._backing, _backingSize);
            result._backing[_backingSize] = id;
            result._backingSize = _backingSize + 1;
            result._memorizedHash = null;
            return Intern(result);
        }

        public InterningBag<T> With(IEnumerable<T> ids)
        {
            return Of(this.Concat(ids));
        }

        public InterningBag<T> Without(T id)
        {
            var index = Array.IndexOf(_backing, id);
            if (index == -1)
                return this;
            if (_backingSize == 1)
                return Empty;
            var result = Pool.Get();
            result.EnsureSize(_backingSize - 1);
            if (index > 0)
                Array.Copy(_backing, result._backing, index);
            if (index < _backingSize - 1)
                Array.Copy(_backing, index + 1, result._backing, index, _backingSize - 1 - index);
            result._backingSize = _backingSize - 1;
            result._memorizedHash = null;
            return Intern(result);
        }

        public static InterningBag<T> Of(params T[] ids)
        {
            return Of((IEnumerable<T>) ids);
        }

        public static InterningBag<T> Of(IEnumerable<T> ids)
        {
            if (ids is ISet<T> setActual)
                return CreateInternal(setActual);
            using (PoolManager.Get(out HashSet<T> set))
            {
                foreach (var id in ids)
                    set.Add(id);
                return CreateInternal(set);
            }
        }

        private static InterningBag<T> CreateInternal(ISet<T> set)
        {
            if (set.Count == 0)
                return Empty;
            var result = Pool.Get();
            result._backingSize = set.Count;
            result.EnsureSize(set.Count);
            var i = 0;
            foreach (var id in set)
                result._backing[i++] = id;
            result._memorizedHash = null;
            return Intern(result);
        }

        #endregion

        public Memory.EqSpan<T>.Enumerator GetEnumerator()
        {
            return new Memory.EqSpan<T>(_backing, 0, _backingSize).GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return string.Join(", ", this);
        }
    }
}