using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Sandbox.Game.Entities.Inventory;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventorySwapComponent : MyObjectBuilder_MultiComponent
    {
    }

    /// <summary>
    /// Component the watches items added to an inventory. If any of those items is specified for being replaced by another item, it will do so 1:1.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_MrzInventorySwapComponent))]
    [MyDependency(typeof(MyInventoryBase))]
    [MyDefinitionRequired(typeof(MrzInventorySwapComponentDefinition))]
    public class MrzInventorySwapComponent : MyMultiComponent
    {
        #region Private

        private MrzInventorySwapComponentDefinition _definition;

        private MyInventoryBase _inventory;

        #endregion

        #region Properties

        public override string DebugName => "Inventory Swap";

        #endregion

        #region Lifecycle

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            _definition = definition as MrzInventorySwapComponentDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            
            if (_definition?.Swaps == null || _definition.Swaps.Count == 0 || !MrzUtils.IsServer)
                return;

            _inventory = this.Get<MyInventoryBase>(_definition.Inventory) ?? this.Get<MyInventoryBase>();
            if (_inventory == null)
                return;

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
            if (item == null || amount == 0)
                return;

            MyDefinitionId outputId;
            if (_definition.Swaps.TryGetValue(item.DefinitionId, out outputId))
            {
                SwapItems(item.DefinitionId, outputId, amount);
                return;
            }
            
            var itemDef = item.GetDefinition();
            if (itemDef.Tags == null || itemDef.Tags.Count == 0)
                return;
            
            foreach (var tag in itemDef.Tags)
            {
                if (!_definition.Swaps.TryGetValue(new MyDefinitionId(typeof(MyObjectBuilder_ItemTagDefinition), tag), out outputId))
                    continue;

                SwapItems(item.DefinitionId, outputId, amount);
                return;
            }
        }

        #endregion

        #region Helpers

        private void SwapItems(MyDefinitionId input, MyDefinitionId output, int amount)
        {
            MrzUtils.ShowNotificationDebug($"Swapping {amount}x {input} for {amount}x {output}.");
            if (_inventory.RemoveItems(input, amount))
                _inventory.AddItemsFuzzy(output, amount);
        }

        #endregion
    }
}
