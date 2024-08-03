using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public sealed class Property<T> : Properties.PropertyBase<T, Property<T>>
    {
        protected override Property<T> Self => this;

        internal Property(IProviderFactory providerFactory) : base(providerFactory)
        {
        }
    }

    public sealed class ListProperty<T> : Properties.CollectionProperty<T, List<T>, ListProperty<T>>
    {
        protected override ListProperty<T> Self => this;
        protected override List<T> Copy(IEnumerable<T> values) => values as List<T> ?? values.ToList();

        internal ListProperty(IProviderFactory providerFactory) : base(providerFactory)
        {
        }
    }

    public sealed class SetProperty<T> : Properties.CollectionProperty<T, HashSet<T>, SetProperty<T>>
    {
        protected override SetProperty<T> Self => this;
        protected override HashSet<T> Copy(IEnumerable<T> values) => values as HashSet<T> ?? values.ToHashSet();

        internal SetProperty(IProviderFactory providerFactory) : base(providerFactory)
        {
        }
    }

    public sealed class PathProperty : Properties.PropertyBase<string, PathProperty>
    {
        internal PathProperty(IProviderFactory providerFactory) : base(providerFactory)
        {
        }

        protected override PathProperty Self => this;
    }

    public sealed class PathCollectionProperty : Properties.CollectionProperty<string, HashSet<string>, PathCollectionProperty>
    {
        internal PathCollectionProperty(IProviderFactory providerFactory) : base(providerFactory)
        {
        }

        protected override PathCollectionProperty Self => this;
        protected override HashSet<string> Copy(IEnumerable<string> values) => values as HashSet<string> ?? values.ToHashSet();
    }

    public static class Properties
    {
        public static Property<T> Property<T>(this IProviderFactory factory) => new Property<T>(factory);
        public static ListProperty<T> ListProperty<T>(this IProviderFactory factory) => new ListProperty<T>(factory);
        public static SetProperty<T> SetProperty<T>(this IProviderFactory factory) => new SetProperty<T>(factory);
        public static PathProperty PathProperty(this IProviderFactory factory) => new PathProperty(factory);
        public static PathCollectionProperty PathCollectionProperty(this IProviderFactory factory) => new PathCollectionProperty(factory);

        public abstract class PropertyBase<T, TSelf> : IProvider<T> where TSelf : PropertyBase<T, TSelf>
        {
            public IProviderFactory ProviderFactory { get; }

            private IProvider<T> _convention;
            private IProvider<T> _provider;

            protected PropertyBase(IProviderFactory providerFactory) => ProviderFactory = providerFactory;

            public bool HasValue => _provider != null || _convention != null;

            public T Value
            {
                get
                {
                    if (_provider != null)
                        return _provider.Value;
                    if (_convention != null)
                        return _convention.Value;
                    throw new NullReferenceException("Not configured");
                }
                set => Set(value);
            }

            protected abstract TSelf Self { get; }

            public TSelf Convention(T value) => Convention(ProviderFactory.Provider(value));
            public TSelf Convention(Func<T> value) => Convention(ProviderFactory.Provider(value));

            public TSelf Convention(IProvider<T> value)
            {
                _convention = value;
                return Self;
            }

            public Func<T> ValueLazy
            {
                set => Set(value);
            }

            public IProvider<T> ValueProvider
            {
                set => Set(value);
            }

            public TSelf Set(T value) => Set(ProviderFactory.Provider(value));
            public TSelf Set(Func<T> value) => Set(ProviderFactory.Provider(value));

            public TSelf Set(IProvider<T> value)
            {
                _provider = value;
                return Self;
            }
        }

        public abstract class CollectionProperty<T, TCollection, TSelf> : IProvider<TCollection>
            where TCollection : class, ICollection<T>
            where TSelf : CollectionProperty<T, TCollection, TSelf>
        {
            public IProviderFactory ProviderFactory { get; }

            private IProvider<TCollection> _convention;
            private bool _configured;
            private readonly List<object> _values = new List<object>();

            private readonly IProvider<TCollection> _provider;

            protected CollectionProperty(IProviderFactory providerFactory)
            {
                ProviderFactory = providerFactory;
                _configured = false;
                _provider = ProviderFactory.Provider(() => Copy(_values.SelectMany(value => value switch
                {
                    T val => new[] { val },
                    IEnumerable<T> values => values,
                    IProvider<T> valProvider => new[] { valProvider.Value },
                    IProvider<IEnumerable<T>> valuesProvider => valuesProvider.Value,
                    _ => throw new Exception($"Invalid provider {value.GetType()}")
                })));
            }

            public bool HasValue => _configured || _convention != null;

            public TCollection Value
            {
                get
                {
                    if (_configured)
                        return _provider.Value;
                    if (_convention != null)
                        return _convention.Value;
                    throw new NullReferenceException("Not configured");
                }
                set
                {
                    Clear();
                    Add(value);
                }
            }

            protected abstract TSelf Self { get; }
            protected abstract TCollection Copy(IEnumerable<T> values);

            private TSelf ConventionInternal(IProvider<TCollection> convention)
            {
                _convention = convention;
                return Self;
            }

            public TSelf Convention(params T[] values) => ConventionInternal(ProviderFactory.Provider(Copy(values)));
            public TSelf Convention(IEnumerable<T> values) => ConventionInternal(ProviderFactory.Provider(Copy(values)));
            public TSelf Convention(Func<IEnumerable<T>> value) => ConventionInternal(ProviderFactory.Provider(() => Copy(value())));
            public TSelf Convention(IProvider<IEnumerable<T>> value) => ConventionInternal(value.Map(Copy));
            public TSelf Convention(Func<T> value) => ConventionInternal(ProviderFactory.Provider(() => Copy(new[] { value() })));
            public TSelf Convention(IProvider<T> value) => ConventionInternal(value.Map(item => Copy(new[] { item })));

            public TSelf Clear()
            {
                _configured = true;
                _values.Clear();
                return Self;
            }

            private TSelf AddInternal(object value)
            {
                _configured = true;
                _values.Add(value);
                return Self;
            }

            public TSelf Add(params T[] values) => AddInternal(values);
            public TSelf Add(IEnumerable<T> values) => AddInternal(values);
            public TSelf Add(Func<IEnumerable<T>> values) => AddInternal(ProviderFactory.Provider(values));
            public TSelf Add(IProvider<IEnumerable<T>> values) => AddInternal(values);
            public TSelf Add(Func<T> value) => AddInternal(ProviderFactory.Provider(value));
            public TSelf Add(IProvider<T> value) => AddInternal(value);


            public InitializerHelper ValueInitializer => new InitializerHelper(this);
            public sealed class InitializerHelper : IEnumerable
            {
                private readonly CollectionProperty<T, TCollection, TSelf> _owner;

                internal InitializerHelper(CollectionProperty<T, TCollection, TSelf> owner) => _owner = owner;

                public IEnumerator GetEnumerator() => throw new NotImplementedException();

                public void Add(params T[] values) => _owner.Add(values);
                public void Add(IEnumerable<T> values) => _owner.Add(values);
                public void Add(Func<IEnumerable<T>> values) => _owner.Add(values);
                public void Add(IProvider<IEnumerable<T>> values) => _owner.Add(values);
                public void Add(Func<T> value) => _owner.Add(value);
                public void Add(IProvider<T> value) => _owner.Add(value);
            }
        }
    }
}