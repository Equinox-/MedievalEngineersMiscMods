using Equinox76561198048419394.Core.Mirz.Extensions;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Components;
using VRage.Game.Entity.EntityComponents;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzItemOwnershipComponent : MyObjectBuilder_EntityComponent
    {
    }
    
    /// <summary>
    /// A component that assigns correct ownership for an equipped item.
    /// Requires ownership component and inventory item component to function.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_MrzItemOwnershipComponent))]
    [MyDependency(typeof(MyEntityOwnershipComponent))]
    [MyDependency(typeof(MyInventoryItemComponent))]
    public class MrzItemOwnershipComponent: MyEntityComponent
    {
        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            AddScheduledCallback(UpdateOwner, 16);
        }

        private void UpdateOwner(long delta)
        {
            if (MyAPIGateway.Players == null)
                return;

            var ownership = this.Get<MyEntityOwnershipComponent>();
            var inventoryItem = this.Get<MyInventoryItemComponent>();

            if (ownership == null || inventoryItem?.ItemContainer?.Entity == null)
                return;

            var player = MyAPIGateway.Players.GetPlayerControllingEntity(inventoryItem.ItemContainer.Entity);
            if (player == null)
                return;

            ownership.OwnerId = player.IdentityId;
        }
    }
}
