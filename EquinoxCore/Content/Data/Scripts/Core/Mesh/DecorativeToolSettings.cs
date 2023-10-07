using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    internal static class DecorativeToolSettings
    {
        public static UvProjectionMode UvProjection = UvProjectionMode.Bevel;
        public static UvBiasMode UvBias = UvBiasMode.XAxis;
        public static float UvScale = 1f;

        public static float LineCatenaryFactor;

        public static int DecalRotationDeg = 0;
        public static float DecalHeight = 0.125f;
        public static int DecalIndex = 0;

        public static Vector3? HsvShift = null;
    }
}