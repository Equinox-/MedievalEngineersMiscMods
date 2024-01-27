using System;
using Medieval.GUI.ContextMenu.DataSources;
using VRage.Collections;

namespace Equinox76561198048419394.Core.Mesh
{
    public class DecorativeMaterialsDataSource<T> : IMyGridDataSource<IDecorativeMaterial> where T : IDecorativeMaterial
    {
        private readonly ListReader<T> _materials;
        private readonly Func<int> _get;
        private readonly Action<int> _set;

        public DecorativeMaterialsDataSource(ListReader<T> def,
            Func<int> get,
            Action<int> set)
        {
            _materials = def;
            _get = get;
            _set = set;
        }

        public void Close()
        {
        }

        public IDecorativeMaterial GetData(int index) => _materials[index];

        void IMyArrayDataSource<IDecorativeMaterial>.SetData(int index, IDecorativeMaterial value)
        {
        }

        public int Length => _materials.Count;

        public int? SelectedIndex
        {
            get => _get() % Length;
            set
            {
                if (value.HasValue)
                    _set(value.Value);
            }
        }
    }
}