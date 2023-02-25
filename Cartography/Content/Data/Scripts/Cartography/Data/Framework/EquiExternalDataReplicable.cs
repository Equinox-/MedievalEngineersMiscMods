using Sandbox.Game.Players;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.Session;

namespace Equinox76561198048419394.Cartography.Data.Framework
{
    public class EquiExternalDataReplicable<T> : MyComponentReplicable<T> where T : MyEntityComponent, IMyEventProxy
    {
        protected override IMyReplicable GetParent()
        {
            var parent = (MyEntityReplicable<MyEntity>)base.GetParent();
            parent.PriorityFunction = PriorityFunction;
            return parent;
        }

        protected virtual float GetAccessiblePriority(MyClientInfo client)
        {
            return 1;
        }

        private float PriorityFunction(IMyEntityReplicable replicable, MyClientInfo client)
        {
            var player = new MyPlayer.PlayerId(client.EndpointId.Value);
            var data = Instance.Entity.Id;
            var hasAccess = MySession.Static.Components.Get<EquiExternalItemDataManager>().PlayerHasAccess(player, data);
            return hasAccess ? GetAccessiblePriority(client) : -1;
        }
    }
}