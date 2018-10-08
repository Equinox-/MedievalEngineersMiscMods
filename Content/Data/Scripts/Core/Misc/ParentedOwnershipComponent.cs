using System.Xml.Serialization;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.EntityComponents;
using VRage.Game.ObjectBuilders.Components;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiParentedOwnershipComponent))]
    [MyDependency(typeof(MyHierarchyComponentBase))]
    public class EquiParentedOwnershipComponent : MyEntityOwnershipComponent
    {
        private MyHierarchyComponent _hierarchy;
        private MyEntityOwnershipComponent _parent;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _hierarchy = Entity.Hierarchy;
            if (_hierarchy != null)
                _hierarchy.OnParentChanged += OnParentChanged;
            OnParentChanged(Entity);
        }

        private void OnParentChanged(MyEntity obj)
        {
            var parent = _hierarchy?.Parent?.Entity?.Components.Get<MyEntityOwnershipComponent>();
            if (_parent == parent)
                return;
            if (_parent != null)
                _parent.OwnerChanged -= ParentOwnerChanged;
            if (parent != null)
                parent.OwnerChanged += ParentOwnerChanged;
            _parent = parent;
            ParentOwnerChanged(parent, 0, 0);
        }

        private void ParentOwnerChanged(MyEntityOwnershipComponent c, long arg1, long arg2)
        {
            OwnerId = _parent?.OwnerId ?? 0;
            ShareMode = _parent?.ShareMode ?? MyOwnershipShareModeEnum.None;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            OnParentChanged(null);
            if (_hierarchy != null)
                _hierarchy.OnParentChanged -= OnParentChanged;
            _hierarchy = null;
        }

        public override bool IsSerialized => false;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiParentedOwnershipComponent : MyObjectBuilder_EntityOwnershipComponent
    {
    }
}