using System;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util
{
    public sealed class InventoryObserver
    {
        private MyEntityComponentContainer _container;

        public event Action InventoryChanged;

        private readonly Dictionary<MyStringHash, MyInventoryBase> _inventories = new Dictionary<MyStringHash, MyInventoryBase>();
        public DictionaryReader<MyStringHash, MyInventoryBase> Inventories => _inventories;
        private readonly Action<MyEntityComponent> _registerComponent, _unregisterComponent;

        public InventoryObserver()
        {
            // ReSharper disable HeapView.ClosureAllocation HeapView.DelegateAllocation
            // ReSharper disable once ConvertToLocalFunction
            Action<MyInventoryBase> invChanged = inv => InventoryChanged?.Invoke();
            _unregisterComponent = x =>
            {
                var inv = x as MyInventoryBase;
                if (inv == null)
                    return;
                _inventories.Remove(inv.SubtypeId);
                inv.ContentsChanged -= invChanged;
                InventoryChanged?.Invoke();
            };
            _registerComponent = x =>
            {
                var inv = x as MyInventoryBase;
                if (inv == null)
                    return;
                if (_inventories.TryGetValue(inv.InventoryId, out var curr))
                    _unregisterComponent(curr);
                _inventories[inv.InventoryId] = inv;
                inv.ContentsChanged += invChanged;
                InventoryChanged?.Invoke();
            };
            // ReSharper restore HeapView.ClosureAllocation HeapView.DelegateAllocation
        }

        public void OnAddedToContainer(MyEntityComponentContainer container)
        {
            _container = container;
            container.ComponentAdded += _registerComponent;
            container.ComponentRemoved += _unregisterComponent;
            foreach (var k in container)
                _registerComponent(k);
        }

        public void OnRemovedFromContainer()
        {
            _container.ComponentAdded -= _registerComponent;
            _container.ComponentRemoved -= _unregisterComponent;
            foreach (var k in _container)
                _unregisterComponent(k);
            _inventories.Clear();
            _container = null;
        }
    }
}