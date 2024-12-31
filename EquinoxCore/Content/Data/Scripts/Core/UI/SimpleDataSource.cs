using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.DataSources;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public static class SimpleDataSources
    {
        public static IMySingleValueDataSource<T> Simple<T>(Func<T> getter, Action<T> setter) => new SimpleDataSource<T>(getter, setter);
        public static IMySingleValueDataSource<T> SimpleReadOnly<T>(Func<T> getter) => new SimpleDataSource<T>(getter, null);
        public static IMySingleValueDataSource<T> SimpleRef<T>(SimpleRefDataSource<T>.DelGetRef getter) => new SimpleRefDataSource<T>(getter);

        public static IMyArrayDataSource<T> Array<T>(int length, Func<int, T> getter, Action<int, T> setter) =>
            new SimpleArrayDataSource<T>(length, getter, setter);

        public static IMyArrayDataSource<T> ArrayReadOnly<T>(int length, Func<int, T> getter) => new SimpleArrayDataSource<T>(length, getter, null);

        public static IMyArrayDataSource<T> ArrayRef<T>(int length, SimpleArrayRefDataSource<T>.DelGetRef getter) =>
            new SimpleArrayRefDataSource<T>(length, getter);

        public static ContextMenuDropdownDataSource DropdownEnum<T>(Func<T> getter, Action<T> setter) where T : struct => new EnumDataSource<T>(getter, setter);

        public static SimpleDropdownDataSourceFactory<T> DropdownEnum<T>(Func<T, ContextMenuDropdownDataSource.DropdownItem> name) where T : struct =>
            DropdownFactory(name, (T[])Enum.GetValues(typeof(T)));

        public static SimpleDropdownDataSourceFactory<T> DropdownFactory<T>(Func<T, ContextMenuDropdownDataSource.DropdownItem> name, params T[] values)
        {
            var items = new ContextMenuDropdownDataSource.DropdownItem[values.Length];
            for (var i = 0; i < values.Length; i++)
                items[i] = name(values[i]);
            return new SimpleDropdownDataSourceFactory<T>(values, items);
        }

        public static SimpleDropdownDataSourceFactory<T> DropdownEnum<T>(Func<T, ContextMenuDropdownDataSource.DropdownItem?> name) where T : struct =>
            DropdownFactory(name, (T[])Enum.GetValues(typeof(T)));

        public static SimpleDropdownDataSourceFactory<T> DropdownFactory<T>(Func<T, ContextMenuDropdownDataSource.DropdownItem?> name, params T[] values)
        {
            var items = new ContextMenuDropdownDataSource.DropdownItem[values.Length];
            var j = 0;
            for (var i = 0; i < values.Length; i++)
            {
                var item = name(values[i]);
                if (!item.HasValue) continue;
                values[j] = values[i];
                items[j++] = item.Value;
            }
            System.Array.Resize(ref items, j);
            System.Array.Resize(ref values, j);
            return new SimpleDropdownDataSourceFactory<T>(values, items);
        }

        public static ContextMenuDropdownDataSource.DropdownItem DropdownItem(string text, string tooltip = null) =>
            new ContextMenuDropdownDataSource.DropdownItem(MyStringId.GetOrCompute(text), MyStringId.GetOrCompute(tooltip));
    }

    public sealed class SimpleDataSource<T> : IMySingleValueDataSource<T>
    {
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;

        public SimpleDataSource(Func<T> getter, Action<T> setter)
        {
            _getter = getter;
            _setter = setter;
        }

        public void Close()
        {
        }

        public T GetData() => _getter();

        public void SetData(T value) => _setter?.Invoke(value);
    }

    public sealed class SimpleRefDataSource<T> : IMySingleValueDataSource<T>
    {
        public delegate ref T DelGetRef();

        private readonly DelGetRef _getRef;

        public SimpleRefDataSource(DelGetRef getRef) => _getRef = getRef;

        public void Close()
        {
        }

        public T GetData() => _getRef();

        public void SetData(T value) => _getRef() = value;
    }

    public sealed class SimpleArrayDataSource<T> : IMyArrayDataSource<T>
    {
        private readonly Func<int, T> _getter;
        private readonly Action<int, T> _setter;

        public SimpleArrayDataSource(int length, Func<int, T> getter, Action<int, T> setter)
        {
            Length = length;
            _getter = getter;
            _setter = setter;
        }

        public void Close()
        {
        }

        public int Length { get; }

        public T GetData(int index) => _getter(index);

        public void SetData(int index, T value) => _setter?.Invoke(index, value);
    }

    public sealed class SimpleArrayRefDataSource<T> : IMyArrayDataSource<T>
    {
        public delegate ref T DelGetRef(int index);

        private readonly DelGetRef _getRef;

        public SimpleArrayRefDataSource(int length, DelGetRef getRef)
        {
            Length = length;
            _getRef = getRef;
        }

        public void Close()
        {
        }

        public int Length { get; }

        public T GetData(int index) => _getRef(index);

        public void SetData(int index, T value) => _getRef(index) = value;
    }

    public sealed class SimpleDropdownDataSourceFactory<T>
    {
        private readonly ContextMenuDropdownDataSource.DropdownItem[] _items;
        private readonly T[] _values;
        private readonly Dictionary<T, int> _table;

        internal SimpleDropdownDataSourceFactory(T[] values, ContextMenuDropdownDataSource.DropdownItem[] items)
        {
            _values = values;
            _items = items;
            _table = new Dictionary<T, int>();
            for (var i = 0; i < _items.Length; i++)
                _table.Add(_values[i], i);
        }

        public ContextMenuDropdownDataSource Create(Func<T> getter, Action<T> setter) => new Impl(this, getter, setter);

        private sealed class Impl : ContextMenuDropdownDataSource
        {
            private readonly SimpleDropdownDataSourceFactory<T> _owner;
            private readonly Func<T> _getter;
            private readonly Action<T> _setter;

            public Impl(SimpleDropdownDataSourceFactory<T> owner, Func<T> getter, Action<T> setter)
            {
                _owner = owner;
                _getter = getter;
                _setter = setter;
            }

            public override int Count => _owner._items.Length;

            public override int Selected
            {
                get => _owner._table.GetValueOrDefault(_getter(), 0);
                set => _setter(_owner._values[value]);
            }

            public override void GetItems(List<DropdownItem> output)
            {
                output.Clear();
                output.AddSpan(_owner._items.AsEqSpan());
            }
        }
    }

    public interface IEquiDataSourceWithChangeEvent : IMyContextMenuDataSource
    {
        event Action OnChange;
    }
}