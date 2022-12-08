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
}