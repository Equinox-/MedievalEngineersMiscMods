using Medieval.Entities.Components;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Network;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class NetworkTrust
    {
        /// <summary>
        /// Clients can't request to operate on something outside this distance.
        /// </summary>
        private const double TrustedDistance = 50d;

        public static bool IsTrusted(MyEntityComponent target, Vector3D? overrideLocation = null)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return true;
            if (MyEventContext.Current.IsLocallyInvoked)
                return true;
            if (target?.Entity == null)
                return true;
            if (MyAPIGateway.Session.IsAdminModeEnabled(MyEventContext.Current.Sender.Value))
                return true;
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(MyEventContext.Current.Sender.Value, 0));
            var playerEntity = player?.ControlledEntity;
            if (playerEntity == null)
                return false;

            var playerLoc = playerEntity.WorldMatrix.Translation;
            var worldAabb = target.Entity.PositionComp.WorldAABB;
            var loc = overrideLocation ?? Vector3D.Clamp(playerLoc, worldAabb.Min, worldAabb.Max);
            if (Vector3D.DistanceSquared(playerEntity.WorldMatrix.Translation, loc) > TrustedDistance * TrustedDistance)
                return false;

            var access = target.Container.Get<MyAccessPermissionComponent>();
            if (access != null && !access.Permissions.HasPermission(player.Identity.Id))
                return false;

            var areaOwnership = MySession.Static.Components.Get<MyAreaOwnershipSystem>()?.GetAreaPermissions(loc);
            if (areaOwnership.HasValue && !areaOwnership.Value.HasPermission(player.Identity.Id))
                return false;
            
            return true;
        }
    }
}