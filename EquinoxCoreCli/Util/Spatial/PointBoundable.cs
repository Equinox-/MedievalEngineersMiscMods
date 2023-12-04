using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Spatial
{
    public struct PointBounded : IBoxBounded
    {
        public Vector3 Point;

        private PointBounded(Vector3 pt) => Point = pt;

        public void GetBounds(ref BoundingBox box)
        {
            box.Min = Point;
            box.Max = Point;
        }

        public static implicit operator PointBounded(Vector3 pt) => new PointBounded(pt);

        public static implicit operator Vector3(PointBounded pt) => pt.Point;
    }
}