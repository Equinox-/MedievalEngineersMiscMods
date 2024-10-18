using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Medieval.Constants;
using Medieval.Definitions.Crafting;
using Medieval.Entities.Components.Crafting.Recipes;
using Sandbox.Game;
using VRage.Collections;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Crafting
{
    [MyComponent(typeof(MyObjectBuilder_EquiExtraInventoryProviderComponent))]
    [MyDefinitionRequired(typeof(EquiExtraInventoryProviderComponentDefinition))]
    public class EquiExtraInventoryProviderComponent : MyMultiComponent, IRecipeProvider
    {
        private EquiExtraInventoryProviderComponentDefinition _definition;

        private readonly HashSet<MyInventoryBase> _inventories = new HashSet<MyInventoryBase>();

        #region Init

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (EquiExtraInventoryProviderComponentDefinition)definition;
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            Container.ComponentAdded += ComponentAdded;
            Container.ComponentRemoved += ComponentRemoved;
            foreach (var comp in Container) ComponentAdded(comp);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            AddScheduledCallback(MovePrimaryInventoryToStart);
        }

        [Update(false)]
        private void MovePrimaryInventoryToStart(long _)
        {
            // Move the extra inventories to the end of the list so they aren't treated as the default inventory.
            // This must be done in an update method since otherwise the remove/add are queued, and the queue simplifies the add/remove operation into
            // a no-op.
            using (PoolManager.Get(out List<MyInventoryBase> inventories))
            using (PoolManager.Get(out List<int> addOrder))
            {
                var primaryInventory = Container.Get<MyInventory>(_definition.PrimaryInventory);
                if (primaryInventory == null || !primaryInventory.ShownInGUI) return;
                inventories.Add(primaryInventory);
                foreach (var comp in Container)
                    if (comp is MyInventory inv && inv.ShownInGUI && inv != primaryInventory)
                        inventories.Add(inv);

                if (inventories.Count <= 1) return;

                // The order of the component looks random, but is actually very predictable due to the inner BinarySearch.
                // Very specifically, adding N items of the same type will always use the same sequence of insertion indices.

                // Remove everything.
                foreach (var inv in inventories)
                    Container.Remove(inv);
                // Add everything, profiling the addition order.
                foreach (var inv in inventories)
                    Container.Add(inv);
                foreach (var inv in inventories)
                    addOrder.Add(Container.IndexOf<MyInventory>(inv.InventoryId));

                // Figure out what insertion index is needed to produce the earliest result in the component list.
                var orderForPrimary = IndexOfMin();
                // If the first (primary) inventory is already in the right spot we're done.
                if (orderForPrimary == 0) return;

                // Otherwise remove everything and try again.
                foreach (var inv in inventories)
                    Container.Remove(inv);

                // Insert "orderForPrimary" secondary inventories first.
                for (var i = 1; i <= orderForPrimary; i++)
                    Container.Add(inventories[i]);
                // Then insert the primary inventory, at the orderForPrimary.
                Container.Add(inventories[0]);
                // Then insert any remaining secondary inventories.
                for (var i = orderForPrimary + 1; i < inventories.Count; i++)
                    Container.Add(inventories[i]);

                var primaryIsAt = Container.IndexOf<MyInventory>(inventories[0].InventoryId);
                for (var i = 1; i < inventories.Count; i++)
                {
                    var secondaryIsAt = Container.IndexOf<MyInventory>(inventories[i].InventoryId);
                    if (secondaryIsAt < primaryIsAt)
                        this.GetLogger()
                            .Warning(
                                $"Failed to reorder secondary inventories to be after the primary inventory: {inventories[0]} at {primaryIsAt} is the primary, {inventories[i]} at {secondaryIsAt} is the secondary");
                }

                return;

                int IndexOfMin()
                {
                    var best = 0;
                    for (var i = 1; i < addOrder.Count; i++)
                        if (addOrder[i] < addOrder[best])
                            best = i;
                    return best;
                }
            }
        }

        public override void OnBeforeRemovedFromContainer()
        {
            foreach (var comp in Container) ComponentRemoved(comp);
            Container.ComponentAdded -= ComponentAdded;
            Container.ComponentRemoved -= ComponentRemoved;
            base.OnBeforeRemovedFromContainer();
        }

        private void ComponentAdded(MyEntityComponent comp)
        {
            if (comp is MyInventoryBase inv && _definition.Inventories.Contains(inv.InventoryId))
                _inventories.Add(inv);
        }

        private void ComponentRemoved(MyEntityComponent comp)
        {
            if (comp is MyInventoryBase inv)
                _inventories.Remove(inv);
        }

        #endregion Init

        #region IRecipeProvider

        public int NumberOfRecipes => 0;

        public IEnumerable<MyCraftingRecipeDefinition> Recipes => Enumerable.Empty<MyCraftingRecipeDefinition>();

        public IEnumerable<MyInventoryBase> AdditionalInventories => _inventories;

        public event Action<IRecipeProvider> OnRecipesChanged
        {
            add { }
            remove { }
        }

        public void CollectRecipesForConstraint(HashSet<MyDefinitionId> _1, HashSet<MyDefinitionId> _2)
        {
        }

        #endregion IRecipeProvider
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiExtraInventoryProviderComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiExtraInventoryProviderComponentDefinition))]
    public class EquiExtraInventoryProviderComponentDefinition : MyMultiComponentDefinition
    {
        public MyStringHash PrimaryInventory { get; private set; }
        public HashSetReader<MyStringHash> Inventories { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiExtraInventoryProviderComponentDefinition)def;
            PrimaryInventory = string.IsNullOrEmpty(ob.PrimaryInventory)
                ? MyCharacterConstants.MainInventory
                : MyStringHash.GetOrCompute(ob.PrimaryInventory);
            var inventories = new HashSet<MyStringHash>();
            if (ob.Inventories != null)
                foreach (var inv in ob.Inventories)
                    inventories.Add(MyStringHash.GetOrCompute(inv));
            Inventories = inventories;
        }
    }

    /// <summary>
    /// Component that will expose an extra inventory in the sidebar of the crafting screen.
    /// </summary>
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiExtraInventoryProviderComponentDefinition : MyObjectBuilder_MultiComponentDefinition
    {
        /// <summary>
        /// SubtypeID of the primary inventory that should be used in UIs.
        /// Defaults to the Character's MainInventory.
        /// </summary>
        [XmlElement]
        public string PrimaryInventory;

        /// <summary>
        /// SubtypeId of inventory to expose in the sidebar.
        /// </summary>
        [XmlElement("Inventory")]
        public string[] Inventories;
    }
}