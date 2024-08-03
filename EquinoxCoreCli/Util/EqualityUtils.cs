using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class EqualityUtils
    {
        public static readonly IEqualityComparer<object> ReferenceEquality = Custom<object>(ReferenceEquals, RuntimeHelpers.GetHashCode); 
        public static IEqualityComparer<T> Custom<T>(Func<T, T, bool> equals, Func<T, int> hash) => new CustomEquality<T>(equals, hash);

        public static IEqualityComparer<HashSet<T>> Set<T>() => SetEquality<T>.Instance;

        #region Dictionary

        public static IEqualityComparer<Dictionary<TK, TV>> Dictionary<TK, TV>() => DictionaryEquality<TK, TV>.Instance;

        public static void DifferingKeys<TK, TV>(Dictionary<TK, TV> x, Dictionary<TK, TV> y, HashSet<TK> keys)
        {
            var valueEquality = Default<TV>();
            foreach (var itemX in x)
                if (!y.TryGetValue(itemX.Key, out var itemY) || !valueEquality.Equals(itemX.Value, itemY))
                    keys.Add(itemX.Key);

            foreach (var keyY in y.Keys)
                if (!x.ContainsKey(keyY))
                    keys.Add(keyY);
        }

        #endregion

        #region Tuples

        public static IEqualityComparer<ValueTuple<T1, T2>> ValueTuple<T1, T2>(
            IEqualityComparer<T1> t1 = null,
            IEqualityComparer<T2> t2 = null)
        {
            if (t1 == null && t2 == null)
                return ValueTupleEquality<T1, T2>.Instance;
            return new ValueTupleEquality<T1, T2>(t1 ?? Default<T1>(), t2 ?? Default<T2>());
        }

        public static IEqualityComparer<ValueTuple<T1, T2, T3>> ValueTuple<T1, T2, T3>(
            IEqualityComparer<T1> t1 = null,
            IEqualityComparer<T2> t2 = null,
            IEqualityComparer<T3> t3 = null)
        {
            if (t1 == null && t2 == null && t3 == null)
                return ValueTupleEquality<T1, T2, T3>.Instance;
            return new ValueTupleEquality<T1, T2, T3>(t1 ?? Default<T1>(), t2 ?? Default<T2>(), t3 ?? Default<T3>());
        }

        public static IEqualityComparer<ValueTuple<T1, T2, T3, T4>> ValueTuple<T1, T2, T3, T4>(
            IEqualityComparer<T1> t1 = null,
            IEqualityComparer<T2> t2 = null,
            IEqualityComparer<T3> t3 = null,
            IEqualityComparer<T4> t4 = null)
        {
            if (t1 == null && t2 == null && t3 == null && t4 == null)
                return ValueTupleEquality<T1, T2, T3, T4>.Instance;
            return new ValueTupleEquality<T1, T2, T3, T4>(
                t1 ?? Default<T1>(),
                t2 ?? Default<T2>(),
                t3 ?? Default<T3>(),
                t4 ?? Default<T4>());
        }

        #endregion

        #region Default Equality

        private static readonly ConcurrentDictionary<Type, object> DefaultInstances = new ConcurrentDictionary<Type, object>();

        private static MethodInfo Method(string name, int arity) => typeof(EqualityUtils)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(x => x.Name == name && x.GetGenericArguments().Length == arity);

        private static readonly MethodInfo SetMethod = Method(nameof(Set), 1);

        private static readonly MethodInfo DictionaryMethod = Method(nameof(Dictionary), 2);

        private static readonly MethodInfo ValueTuple2 = Method(nameof(ValueTuple), 2);

        private static readonly MethodInfo ValueTuple3 = Method(nameof(ValueTuple), 3);

        private static readonly MethodInfo ValueTuple4 = Method(nameof(ValueTuple), 4);

        public static IEqualityComparer<T> Default<T>()
        {
            return (IEqualityComparer<T>)DefaultInstances.GetOrAdd(typeof(T), type =>
            {
                MethodInfo factoryMethod = null;
                if (type.TryGetGenericBase(typeof(HashSet<>), out var setImpl))
                    factoryMethod = SetMethod.MakeGenericMethod(setImpl.GenericTypeArguments);
                else if (type.TryGetGenericBase(typeof(Dictionary<,>), out var dictionaryImpl))
                    factoryMethod = DictionaryMethod.MakeGenericMethod(dictionaryImpl.GenericTypeArguments);
                else if (type.TryGetGenericBase(typeof(ValueTuple<,>), out var tuple2Impl))
                    factoryMethod = ValueTuple2.MakeGenericMethod(tuple2Impl.GenericTypeArguments);
                else if (type.TryGetGenericBase(typeof(ValueTuple<,,>), out var tuple3Impl))
                    factoryMethod = ValueTuple3.MakeGenericMethod(tuple3Impl.GenericTypeArguments);
                else if (type.TryGetGenericBase(typeof(ValueTuple<,,,>), out var tuple4Impl))
                    factoryMethod = ValueTuple4.MakeGenericMethod(tuple4Impl.GenericTypeArguments);
                return factoryMethod != null
                    ? factoryMethod.Invoke(null, new object[factoryMethod.GetParameters().Length])
                    : EqualityComparer<T>.Default;
            });
        }

        #endregion

        private static int NullSafeHashCode<T>(T value, IEqualityComparer<T> equality)
        {
            if (!typeof(T).IsValueType && ReferenceEquals(value, null))
                return 0;
            return equality.GetHashCode(value);
        }

        private class CustomEquality<T> : IEqualityComparer<T>
        {
            private readonly Func<T, T, bool> _equals;
            private readonly Func<T, int> _hash;

            public CustomEquality(Func<T, T, bool> equals, Func<T, int> hash)
            {
                _equals = equals;
                _hash = hash;
            }

            public bool Equals(T x, T y) => _equals(x, y);
            public int GetHashCode(T obj) => _hash(obj);
        }

        private class SetEquality<T> : IEqualityComparer<HashSet<T>>
        {
            public static readonly SetEquality<T> Instance = new SetEquality<T>();

            private SetEquality()
            {
            }

            public bool Equals(HashSet<T> x, HashSet<T> y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                return x.SetEquals(y);
            }

            public int GetHashCode(HashSet<T> obj)
            {
                var hash = 0;
                foreach (var item in obj)
                    hash += NullSafeHashCode(item, obj.Comparer);
                return hash;
            }
        }

        private class DictionaryEquality<TK, TV> : IEqualityComparer<Dictionary<TK, TV>>
        {
            public static readonly DictionaryEquality<TK, TV> Instance = new DictionaryEquality<TK, TV>();
            private static readonly IEqualityComparer<TV> ValueEquality = Default<TV>();

            private DictionaryEquality()
            {
            }

            public bool Equals(Dictionary<TK, TV> x, Dictionary<TK, TV> y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                foreach (var itemX in x)
                    if (!y.TryGetValue(itemX.Key, out var itemY) || !ValueEquality.Equals(itemX.Value, itemY))
                        return false;

                foreach (var keyY in y.Keys)
                    if (!x.ContainsKey(keyY))
                        return false;

                return true;
            }

            public int GetHashCode(Dictionary<TK, TV> obj)
            {
                var hash = 0;
                foreach (var item in obj)
                    hash += NullSafeHashCode(item.Key, obj.Comparer) * 397 + NullSafeHashCode(item.Value, ValueEquality);
                return hash;
            }
        }

        private class ValueTupleEquality<T1, T2> : IEqualityComparer<ValueTuple<T1, T2>>
        {
            public static readonly ValueTupleEquality<T1, T2> Instance =
                new ValueTupleEquality<T1, T2>(Default<T1>(), Default<T2>());

            private readonly IEqualityComparer<T1> _t1;
            private readonly IEqualityComparer<T2> _t2;

            public ValueTupleEquality(IEqualityComparer<T1> t1, IEqualityComparer<T2> t2)
            {
                _t1 = t1;
                _t2 = t2;
            }

            public bool Equals((T1, T2) x, (T1, T2) y) => _t1.Equals(x.Item1, y.Item1) && _t2.Equals(x.Item2, y.Item2);

            public int GetHashCode((T1, T2) obj) => (NullSafeHashCode(obj.Item1, _t1) * 397) ^ NullSafeHashCode(obj.Item2, _t2);
        }

        private class ValueTupleEquality<T1, T2, T3> : IEqualityComparer<ValueTuple<T1, T2, T3>>
        {
            public static readonly ValueTupleEquality<T1, T2, T3> Instance =
                new ValueTupleEquality<T1, T2, T3>(Default<T1>(), Default<T2>(), Default<T3>());

            private readonly IEqualityComparer<T1> _t1;
            private readonly IEqualityComparer<T2> _t2;
            private readonly IEqualityComparer<T3> _t3;

            public ValueTupleEquality(IEqualityComparer<T1> t1, IEqualityComparer<T2> t2, IEqualityComparer<T3> t3)
            {
                _t1 = t1;
                _t2 = t2;
                _t3 = t3;
            }

            public bool Equals((T1, T2, T3) x, (T1, T2, T3) y)
            {
                return _t1.Equals(x.Item1, y.Item1) && _t2.Equals(x.Item2, y.Item2) && _t3.Equals(x.Item3, y.Item3);
            }

            public int GetHashCode((T1, T2, T3) obj)
            {
                var hashCode = NullSafeHashCode(obj.Item1, _t1);
                hashCode = (hashCode * 397) ^ NullSafeHashCode(obj.Item2, _t2);
                hashCode = (hashCode * 397) ^ NullSafeHashCode(obj.Item3, _t3);
                return hashCode;
            }
        }

        private class ValueTupleEquality<T1, T2, T3, T4> : IEqualityComparer<ValueTuple<T1, T2, T3, T4>>
        {
            public static readonly ValueTupleEquality<T1, T2, T3, T4> Instance =
                new ValueTupleEquality<T1, T2, T3, T4>(Default<T1>(), Default<T2>(), Default<T3>(), Default<T4>());

            private readonly IEqualityComparer<T1> _t1;
            private readonly IEqualityComparer<T2> _t2;
            private readonly IEqualityComparer<T3> _t3;
            private readonly IEqualityComparer<T4> _t4;

            public ValueTupleEquality(IEqualityComparer<T1> t1, IEqualityComparer<T2> t2, IEqualityComparer<T3> t3, IEqualityComparer<T4> t4)
            {
                _t1 = t1;
                _t2 = t2;
                _t3 = t3;
                _t4 = t4;
            }

            public bool Equals((T1, T2, T3, T4) x, (T1, T2, T3, T4) y)
            {
                return _t1.Equals(x.Item1, y.Item1) && _t2.Equals(x.Item2, y.Item2) && _t3.Equals(x.Item3, y.Item3) && _t4.Equals(x.Item4, y.Item4);
            }

            public int GetHashCode((T1, T2, T3, T4) obj)
            {
                var hashCode = NullSafeHashCode(obj.Item1, _t1);
                hashCode = (hashCode * 397) ^ NullSafeHashCode(obj.Item2, _t2);
                hashCode = (hashCode * 397) ^ NullSafeHashCode(obj.Item3, _t3);
                hashCode = (hashCode * 397) ^ NullSafeHashCode(obj.Item4, _t4);
                return hashCode;
            }
        }
    }
}