using System;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Components.Entity;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Library.Logging;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util
{
    public class MultiComponentReference<TComp> where TComp : MyEntityComponent
    {
        private readonly HashSet<TComp> _components = new HashSet<TComp>();
        private MyEntityComponentContainer _container;
        private MyHierarchyComponentBase _hierarchy;
        private MyModelAttachmentComponent _modelAttachment;

        public event Action<TComp> ComponentAdded;
        public event Action<TComp> ComponentRemoved;

        private bool _includeParent;
        private bool _includeChildren;

        public HashSetReader<TComp> Components => _components;

        public void AddToContainer(MyEntityComponentContainer container, bool includeParent, bool includeChildren)
        {
            _includeParent = includeParent;
            _includeChildren = includeChildren;

            _container = container;
            _container.ComponentAdded += OnComponentAdded;
            _container.ComponentRemoved += OnComponentAdded;
            foreach (var c in _container.GetComponents<TComp>())
                OnComponentAdded(c);

            if (_includeParent)
            {
                _hierarchy = container.Get<MyHierarchyComponent>();
                _hierarchy.OnParentChanged += ParentChanged;
                ParentChanged(_hierarchy.Parent?.Entity);
            }

            if (_includeChildren)
            {
                _modelAttachment = container.Get<MyModelAttachmentComponent>();
                if (_modelAttachment != null)
                {
                    _modelAttachment.OnEntityAttached += OnEntityAttached;
                    _modelAttachment.OnEntityDetached += OnEntityDetached;
                    var h = container.Get<MyHierarchyComponent>();
                    if (h != null)
                    {
                        foreach (var e in h.Children)
                            if (e.Entity != null && _modelAttachment.GetEntityAttachmentPoint(e.Entity) != MyStringHash.NullOrEmpty)
                                OnEntityAttached(_modelAttachment, e.Entity);
                    }
                }
            }
        }

        private void OnEntityDetached(MyModelAttachmentComponent modelattachmentcomponent, MyEntity entity)
        {
            UnwatchEntity(entity);
        }

        private void OnEntityAttached(MyModelAttachmentComponent modelattachmentcomponent, MyEntity entity)
        {
            WatchEntity(entity);
        }

        public void RemoveFromContainer()
        {
            if (_includeParent)
            {
                _hierarchy.OnParentChanged -= ParentChanged;
                ParentChanged(null);
                _hierarchy = null;
            }

            if (_modelAttachment != null)
            {
                _modelAttachment.OnEntityAttached -= OnEntityAttached;
                _modelAttachment.OnEntityDetached -= OnEntityDetached;
                var h = _container.Get<MyHierarchyComponent>();
                if (h != null)
                {
                    foreach (var e in h.Children)
                        if (e.Entity != null && _modelAttachment.GetEntityAttachmentPoint(e.Entity) != MyStringHash.NullOrEmpty)
                            OnEntityDetached(_modelAttachment, e.Entity);
                }

                _modelAttachment = null;
            }

            _container.ComponentAdded -= OnComponentAdded;
            _container.ComponentRemoved -= OnComponentAdded;
            foreach (var c in _components)
                ComponentRemoved?.Invoke(c);
            _components.Clear();
            _container = null;
        }

        private MyEntity _activeParent;

        private void ParentChanged(MyEntity obj)
        {
            var newParent = _hierarchy.Parent?.Entity;
            if (newParent == _activeParent)
                return;
            if (_activeParent != null)
                UnwatchEntity(_activeParent);

            _activeParent = newParent;
            if (_activeParent != null)
                WatchEntity(newParent);
        }

        private void WatchEntity(MyEntity obj)
        {
            foreach (var c in obj.Components.GetComponents<TComp>())
                OnComponentAdded(c);
            obj.Components.ComponentAdded += OnComponentAdded;
            obj.Components.ComponentRemoved += OnComponentRemoved;
        }

        private void UnwatchEntity(MyEntity obj)
        {
            obj.Components.ComponentAdded -= OnComponentAdded;
            obj.Components.ComponentRemoved -= OnComponentRemoved;
            foreach (var k in obj.Components)
                OnComponentRemoved(k);
        }

        private void OnComponentAdded(MyEntityComponent obj)
        {
            var cast = obj as TComp;
            if (cast == null)
                return;
            if (_components.Add(cast))
                ComponentAdded?.Invoke(cast);
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            var cast = obj as TComp;
            if (cast == null)
                return;
            if (_components.Remove(cast))
                ComponentRemoved?.Invoke(cast);
        }
    }
}