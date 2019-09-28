using System.Linq;
using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Sandbox.Game.Inventory;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzEquipmentSpawnerComponent : MyObjectBuilder_EntityComponent
    {
        public bool Spawned;
    }

    /// <summary>
    /// Component that spawns equipment of the type specified.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_MrzEquipmentSpawnerComponent))]
    [MyDependency(typeof(MyInventoryBase))]
    public class MrzEquipmentSpawnerComponent : MyEntityComponent
    {
        #region Private

        private MrzEquipmentSpawnerDefinition _definition;
        private bool _spawned = false;

        #endregion

        #region Init

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            _definition = definition as MrzEquipmentSpawnerDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (!MrzUtils.IsServer)
                return;

            if (_definition == null || _spawned)
                return;

            AddScheduledCallback(TrySpawnEquipment, 32);
        }
        #endregion

        #region (De)Serialization

        public override bool IsSerialized => true;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = base.Serialize(copy) as MyObjectBuilder_MrzEquipmentSpawnerComponent;

            ob.Spawned = _spawned;

            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);

            var ob = builder as MyObjectBuilder_MrzEquipmentSpawnerComponent;

            _spawned = ob.Spawned;
        }

        #endregion

        #region Private Methods

        private void TrySpawnEquipment(long deltaFrames)
        {
            _spawned = true;

            MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::{Entity.EntityId}");

            var inventory = this.Get<MyInventoryBase>(_definition.Inventory) ?? this.Get<MyInventoryBase>();
            if (inventory == null)
                return;

            MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::Inventory {_definition.Inventory}");

            var loot = MyDefinitionManager.Get<MyLootTableDefinition>(_definition.LootTable);
            if (loot == null)
                return;

            MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::Loot table {_definition.LootTable}");
            var oldItems = inventory.Items.ToList<MyInventoryItem>();
            MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::Old count:{oldItems.Count}");
            inventory.GenerateContent(loot);
            var newItems = inventory.Items;

            MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::New count:{newItems.Count}");

            for (var i = 0; i < newItems.Count; i++)
            {
                var newItem = newItems.ItemAt(i);
                if (i >= oldItems.Count)
                {
                    MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::Activating {newItem.DefinitionId}");
                    MyItemActivateHelper.ActivateItem(Entity, inventory, newItem);
                    continue;
                }

                var oldItem = oldItems[i];
                if (newItem.Amount > oldItem.Amount)
                {
                    MrzUtils.ShowNotificationDebug($"TrySpawnEquipment::Activating {newItem.DefinitionId}");
                    MyItemActivateHelper.ActivateItem(Entity, inventory, newItem);
                }
            }
        }

        #endregion
    }
}
