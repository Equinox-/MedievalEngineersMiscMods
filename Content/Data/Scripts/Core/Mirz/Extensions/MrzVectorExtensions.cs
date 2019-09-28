using VRageMath;

namespace Equinox76561198048419394.Core.Mirz.Extensions
{
    public static class MrzVectorExtensions
    {
        /// <summary>
        /// Compares all components of the vector and returns true if all are smaller.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool IsLessThan(this Vector3 a, Vector3 b)
        {
            return a.X < b.X && a.Y < b.Y && a.Z < b.Z;
        }

        /// <summary>
        /// Compares all components of the vector and returns true if all are smaller or equal.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool IsLessOrEqual(this Vector3 a, Vector3 b)
        {
            return a.X <= b.X && a.Y <= b.Y && a.Z <= b.Z;
        }

        /// <summary>
        /// Compares all components of the vector and returns true if all are greater.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool IsGreaterThan(this Vector3 a, Vector3 b)
        {
            return a.X < b.X && a.Y < b.Y && a.Z < b.Z;
        }

        /// <summary>
        /// Compares all components of the vector and returns true if all are greater or equal.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool IsGreaterOrEqual(this Vector3 a, Vector3 b)
        {
            return a.X <= b.X && a.Y <= b.Y && a.Z <= b.Z;
        }

        public static Vector2 Xy(this Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        public static Vector2 Xz(this Vector3 v)
        {
            return new Vector2(v.X, v.Z);
        }

        public static Vector2 Yz(this Vector3 v)
        {
            return new Vector2(v.Y, v.Z);
        }
    }
}
