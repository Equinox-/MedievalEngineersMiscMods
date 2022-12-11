using System;
using Medieval.GUI.ContextMenu.DataSources;

namespace Equinox76561198048419394.Core.UI
{
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

        public void SetData(T value) => _setter(value);
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

        public void SetData(int index, T value) => _setter(index, value);
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
}