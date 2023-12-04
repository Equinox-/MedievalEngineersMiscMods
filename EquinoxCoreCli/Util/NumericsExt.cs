using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class NumericsExt
    {
        public static Vector3 ToKeen(this in System.Numerics.Vector3 val) => new Vector3(val.X, val.Y, val.Z);
    }
}