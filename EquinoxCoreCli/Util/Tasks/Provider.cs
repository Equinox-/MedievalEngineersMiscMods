using System;
using System.Collections.Generic;
using System.Linq;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public interface IProviderFactory
    {
        /// <summary>
        /// Locked in version for providers. When the version changes providers will recompute if necessary.
        /// </summary>
        public int ProviderVersion { get; set; }
    }

    public interface IProvider<out T>
    {
        bool HasValue { get; }
        T Value { get; }
        IProviderFactory ProviderFactory { get; }
    }

    public delegate bool TryFunc<TResult>(out TResult result);

    public delegate bool TryFunc<in T, TResult>(T input, out TResult result);

    public static class Providers
    {
        public static bool TryGetValue<TOut>(this IProvider<TOut> provider, out TOut value)
        {
            if (provider.HasValue)
            {
                value = provider.Value;
                return true;
            }

            value = default;
            return false;
        }
        
        public static IProvider<TOut> Map<TIn, TOut>(
            this IProvider<TIn> input,
            Func<TIn, TOut> map,
            IEqualityComparer<TIn> equality = null)
        {
            return new TransformProvider<TIn, TOut>(input.ProviderFactory, (out TIn tuple) =>
            {
                if (!input.HasValue)
                {
                    tuple = default;
                    return false;
                }

                tuple = input.Value;
                return true;
            }, map, equality);
        }

        public static IProvider<TOut> Map<T1, T2, TOut>(
            this IProvider<T1> p1, IProvider<T2> p2,
            Func<T1, T2, TOut> map,
            IEqualityComparer<T1> eq1 = null, IEqualityComparer<T2> eq2 = null)
        {
            if (!ReferenceEquals(p1.ProviderFactory, p2.ProviderFactory))
                throw new Exception("Input task managers differ");
            return new TransformProvider<ValueTuple<T1, T2>, TOut>(
                p1.ProviderFactory,
                (out ValueTuple<T1, T2> tuple) =>
                {
                    if (!p1.HasValue || !p2.HasValue)
                    {
                        tuple = default;
                        return false;
                    }

                    tuple = ValueTuple.Create(p1.Value, p2.Value);
                    return true;
                },
                values => map(values.Item1, values.Item2),
                EqualityUtils.ValueTuple(eq1, eq2));
        }

        public static IProvider<TOut> Map<T1, T2, T3, TOut>(
            this IProvider<T1> p1, IProvider<T2> p2, IProvider<T3> p3,
            Func<T1, T2, T3, TOut> map,
            IEqualityComparer<T1> eq1 = null, IEqualityComparer<T2> eq2 = null,
            IEqualityComparer<T3> eq3 = null)
        {
            if (!ReferenceEquals(p1.ProviderFactory, p2.ProviderFactory) || !ReferenceEquals(p1.ProviderFactory, p3.ProviderFactory))
                throw new Exception("Input task managers differ");
            return new TransformProvider<ValueTuple<T1, T2, T3>, TOut>(
                p1.ProviderFactory,
                (out ValueTuple<T1, T2, T3> tuple) =>
                {
                    if (!p1.HasValue || !p2.HasValue || !p3.HasValue)
                    {
                        tuple = default;
                        return false;
                    }

                    tuple = ValueTuple.Create(p1.Value, p2.Value, p3.Value);
                    return true;
                },
                values => map(values.Item1, values.Item2, values.Item3),
                EqualityUtils.ValueTuple(eq1, eq2, eq3));
        }

        public static IProvider<TOut> Map<T1, T2, T3, T4, TOut>(
            this IProvider<T1> p1, IProvider<T2> p2, IProvider<T3> p3, IProvider<T4> p4,
            Func<T1, T2, T3, T4, TOut> map,
            IEqualityComparer<T1> eq1 = null, IEqualityComparer<T2> eq2 = null,
            IEqualityComparer<T3> eq3 = null, IEqualityComparer<T4> eq4 = null)
        {
            if (!ReferenceEquals(p1.ProviderFactory, p2.ProviderFactory)
                || !ReferenceEquals(p1.ProviderFactory, p3.ProviderFactory)
                || !ReferenceEquals(p1.ProviderFactory, p4.ProviderFactory))
                throw new Exception("Input task managers differ");
            return new TransformProvider<ValueTuple<T1, T2, T3, T4>, TOut>(
                p1.ProviderFactory,
                (out ValueTuple<T1, T2, T3, T4> tuple) =>
                {
                    if (!p1.HasValue || !p2.HasValue || !p3.HasValue || !p4.HasValue)
                    {
                        tuple = default;
                        return false;
                    }

                    tuple = ValueTuple.Create(p1.Value, p2.Value, p3.Value, p4.Value);
                    return true;
                },
                values => map(values.Item1, values.Item2, values.Item3, values.Item4),
                EqualityUtils.ValueTuple(eq1, eq2, eq3, eq4));
        }

        public static IProvider<TOut> MapFile<TOut>(this IProvider<string> input, Func<string, TOut> map)
        {
            return input.Map(x => new[] { x }).MapFiles(files => map(files[0]), SingleFileEquality);
        }

        private static readonly IEqualityComparer<string[]>
            SingleFileEquality = EqualityUtils.Custom<string[]>((x, y) => x[0] == y[0], x => x[0].GetHashCode());

        public static IProvider<TOut> MapFiles<TFiles, TOut>(
            this IProvider<TFiles> input,
            Func<TFiles, TOut> map,
            IEqualityComparer<TFiles> equality = null) where TFiles : IEnumerable<string>
        {
            var fingerprints = input
                .Map(x => x.SelectMany(AssetTaskExecutionContext.ResolveFiles).ToHashSet())
                .Map(x => x.Select(file => FileFingerprint.Compute(file)).ToHashSet());
            return input.Map(fingerprints, (files, _) => map(files), eq1: equality);
        }

        public static IProvider<T> Provider<T>(this IProviderFactory providerFactory, T value) => new FixedProvider<T>(providerFactory, value);

        public static IProvider<T> Provider<T>(this IProviderFactory providerFactory, Func<T> func)
        {
            return new LazyProvider<T>(providerFactory, (out T result) =>
            {
                result = func();
                return true;
            });
        }

        private abstract class LazyProviderBase<T> : IProvider<T>
        {
            private int? _computedAt;
            private T _value;
            private bool _hasValue;

            protected LazyProviderBase(IProviderFactory providerFactory) => ProviderFactory = providerFactory;

            private void ComputeIfNeeded()
            {
                var version = ProviderFactory.ProviderVersion;
                if (_computedAt == version)
                    return;
                lock (this)
                {
                    if (_computedAt == version)
                        return;
                    _hasValue = TryCompute(ref _value);
                    _computedAt = version;
                }
            }

            public bool HasValue
            {
                get
                {
                    ComputeIfNeeded();
                    return _hasValue;
                }
            }

            public T Value
            {
                get
                {
                    ComputeIfNeeded();
                    if (!_hasValue) throw new NullReferenceException("No value present");
                    return _value;
                }
            }

            protected abstract bool TryCompute(ref T value);

            public IProviderFactory ProviderFactory { get; }
        }

        private sealed class LazyProvider<T> : LazyProviderBase<T>
        {
            private readonly TryFunc<T> _compute;

            public LazyProvider(IProviderFactory providerFactory, TryFunc<T> compute) : base(providerFactory) => _compute = compute;

            protected override bool TryCompute(ref T value) => _compute(out value);
        }

        private sealed class TransformProvider<TIn, TOut> : LazyProviderBase<TOut>
        {
            private readonly IEqualityComparer<TIn> _equality;
            private readonly TryFunc<TIn> _upstream;
            private readonly Func<TIn, TOut> _compute;
            private bool _computed;
            private TIn _computedFor;

            public TransformProvider(
                IProviderFactory providerFactory,
                TryFunc<TIn> upstream,
                Func<TIn, TOut> compute,
                IEqualityComparer<TIn> equality = null) : base(providerFactory)
            {
                _upstream = upstream;
                _compute = compute;
                _equality = equality ?? EqualityUtils.Default<TIn>();
            }

            protected override bool TryCompute(ref TOut value)
            {
                if (!_upstream(out var upstreamValue))
                {
                    _computedFor = default;
                    _computed = false;
                    return false;
                }

                if (_computed && _equality.Equals(upstreamValue, _computedFor))
                    return true;
                value = _compute(upstreamValue);
                _computedFor = upstreamValue;
                _computed = true;
                return true;
            }
        }

        private sealed class FixedProvider<T> : IProvider<T>
        {
            public FixedProvider(IProviderFactory providerFactory, T value)
            {
                ProviderFactory = providerFactory;
                Value = value;
            }

            public bool HasValue => true;
            public T Value { get; }
            public IProviderFactory ProviderFactory { get; }
        }
    }
}