using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Equinox76561198048419394.Core.Util
{
    public static class Extensions
    {
        public static bool IsServer(this IMySession session)
        {
            return MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer;
        }
    }
}