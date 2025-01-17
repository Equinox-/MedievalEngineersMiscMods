using VRageMath;

namespace Equinox76561198048419394.Core.Mesh
{
    internal static class DecorativeToolSettings
    {
        public static UvProjectionMode UvProjection = UvProjectionMode.Bevel;
        public static UvBiasMode UvBias = UvBiasMode.XAxis;
        public static float UvScale = 1f;
        public static int MaterialIndex = 0;

        public static float LineCatenaryFactor;
        public static float LineWidthA = -1;
        public static float LineWidthB = -1;

        public static float DecalRotationDeg = 0;
        public static float DecalHeight = 0.125f;
        public static bool DecalMirrored = false;
        public static EquiDecorativeMeshComponent.DecalFlags DecalFlags => DecalMirrored ? EquiDecorativeMeshComponent.DecalFlags.Mirrored : 0;

        public static float ModelScale = 1;

        public static Vector3? HsvShift = null;

        /// <summary>
        /// How many divisions a small block is divided into for snapping.
        /// </summary>
        public static int SnapDivisions = 16;

        public static MeshSnappingType MeshSnapping = MeshSnappingType.None;

        public static float SnapSize => 0.25f / SnapDivisions;

        public enum MeshSnappingType
        {
            None,
            Vertex,
            Edge,
        }
    }
}