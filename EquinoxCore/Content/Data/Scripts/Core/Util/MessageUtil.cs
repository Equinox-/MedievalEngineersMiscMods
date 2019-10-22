using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Network;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    [StaticEventOwner]
    public static class MessageUtil
    {
        public static void ShowNotification(this IMyPlayer player, string msg, int timeMs = 2000, MyStringHash? font = null, Vector4? color = null)
        {
            // ReSharper disable once PossibleUnintendedReferenceComparison
            if (player == MyAPIGateway.Session?.LocalHumanPlayer)
                DispatchNotification(msg, timeMs, font, color);
            else
                MyMultiplayerModApi.Static.RaiseStaticEvent(s => DispatchNotification, msg, timeMs, font, color, new EndpointId(player.SteamUserId));
        }

        [Event]
        [Client]
        private static void DispatchNotification(string msg, int timeMs, MyStringHash? font, Vector4? color)
        {
            MyAPIGateway.Utilities.ShowNotification(msg, timeMs, font, color);
        }
    }
}