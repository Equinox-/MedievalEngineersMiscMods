using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventoryVoidComponent : MyObjectBuilder_MultiComponent
    {
    }

    [MyComponent(typeof(MyObjectBuilder_MrzInventoryVoidComponent))]
    [MyDependency(typeof(MyInventoryBase))]
    [MyDefinitionRequired(typeof(MrzInventoryVoidComponentDefinition))]
    public class MrzInventoryVoidComponent : MyMultiComponent
    {
        #region Private

        private MrzInventoryVoidComponentDefinition _definition;

        private MyInventoryBase _inventory;

        #endregion

        #region Properties

        public override string DebugName => "Inventory Void";

        #endregion

        #region Lifecycle

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            _definition = definition as MrzInventoryVoidComponentDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (_definition == null || !MrzUtils.IsServer)
                return;

            _inventory = this.Get<MyInventoryBase>(_definition.Inventory) ?? this.Get<MyInventoryBase>();
            if (_inventory == null)
                return;

            _inventory.Clear();
            _inventory.AfterItemsAdded += OnAfterItemsAdded;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (_inventory == null)
                return;

            _inventory.AfterItemsAdded -= OnAfterItemsAdded;
        }

        #endregion

        #region Bindings

        private void OnAfterItemsAdded(MyInventoryItem item, int amount)
        {
            _inventory.RemoveItems(item.DefinitionId, amount);
        }

        #endregion
    }
}
