using System.Collections.Generic;
using Equinox76561198048419394.Core.Mirz.Extensions;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity.CubeGrid;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventoryPullComponent : MyObjectBuilder_EntityComponent
    {

    }

    /// <summary>
    /// A server-side component that pulls items from an adjacent inventory and puts them into the specified inventory on this entity.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_MrzInventoryPullComponent))]
    [MyDependency(typeof(MyInventoryBase))]
    public class MrzInventoryPullComponent : MyEntityComponent
    {
        #region Private

        private MyBlockComponent _block;
        
        private MyInventoryBase _sourceInventory;
        private MyInventoryBase _destInventory;
        private bool _subscribed = false;
        
        private readonly MyStringHash tmp_destInventoryName = MyStringHash.GetOrCompute("Input");

        #endregion

        #region Lifecycle

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (!MrzUtils.IsServer)
                return;

            _block = this.Get<MyBlockComponent>();
            if (_block == null)
            {
                MrzUtils.CreateNotificationDebug("MrzInventoryPullComponent can only be used on blocks.");
                return;
            }
            
            _destInventory = this.Get<MyInventoryBase>(tmp_destInventoryName) ?? this.Get<MyInventoryBase>();
            if (_destInventory == null)
                return;

            FindSource();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            if (_sourceInventory != null)
            {
                _sourceInventory.ContentsChanged -= OnSourceItemsChanged;
                _sourceInventory.BeforeRemovedFromContainer -= OnBeforeRemovedFromContainer;
                _sourceInventory = null;
            }
        }

        [Update(false)]
        private void Update(long deltaFrames)
        {
            var items = _sourceInventory.Items;
            foreach (var item in items)
            {
                if (_destInventory.CanAddItems(item.DefinitionId, 1) && _destInventory.TransferItemsFrom(_sourceInventory, item, 1))
                    return;
            }
        }

        #endregion

        #region Helpers

        private bool FindSource()
        {
            // don't look for a source if we already have one
            if (_sourceInventory != null)
                return true;

            var grid = _block.GridData;
            if (grid == null)
                return false;

            var hierarchy = grid.Get<MyGridHierarchyComponent>();
            if (hierarchy == null)
                return false;

            var neighbours = new List<MyBlock>();
            grid.GetBlocksInRange(_block.Block.Position - Vector3I.One, _block.Block.Position + Vector3I.One, neighbours);
            if (neighbours.Count == 0)
                return false;

            MyEntity sourceBlock = null;

            foreach (var entry in neighbours)
            {
                sourceBlock = hierarchy.GetBlockEntity(entry.Id);
                if (sourceBlock == null)
                    continue;

                break;
            }

            if (sourceBlock == null)
                return false;

            _sourceInventory = sourceBlock.Components.Get<MyInventoryBase>();
            if (_sourceInventory == null)
                return false;

            _sourceInventory.BeforeRemovedFromContainer += OnBeforeRemovedFromContainer;
            _sourceInventory.ContentsChanged += OnSourceItemsChanged;

            return true;
        }

        #endregion

        #region Bindings

        private void OnBeforeRemovedFromContainer(MyEntityComponent component)
        {
            if (component != _sourceInventory)
                return;

            _sourceInventory.ContentsChanged -= OnSourceItemsChanged;
            _sourceInventory.BeforeRemovedFromContainer -= OnBeforeRemovedFromContainer;
            _sourceInventory = null;
        }

        private void OnSourceItemsChanged(MyInventoryBase inventory)
        {
            if (inventory.ItemCount == 0)
            {
                RemoveScheduledUpdate(Update);
            }
            else if (!_subscribed)
            {
                _subscribed = true;
                AddScheduledUpdate(Update, 1000);
            }
        }
        #endregion
    }
}
