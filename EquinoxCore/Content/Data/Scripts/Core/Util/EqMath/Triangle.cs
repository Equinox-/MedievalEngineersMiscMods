using System.Diagnostics.Contracts;
using System.Numerics;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public struct Triangle
    {
        public readonly Vector3 A, B, C;

        public Triangle(in Vector3 a, in Vector3 b, in Vector3 c, in Vector3? desiredNorm = null)
        {
            A = a;
            B = b;
            C = c;
            if (!desiredNorm.HasValue || RawNormal.Dot(desiredNorm.Value) > 0)
                return;
            B = c;
            C = b;
        }

        public Vector3 RawNormal => Vector3.Cross(B - A, C - A);

        public Vector3 Normal => Vector3.Normalize(RawNormal);

        [Pure]
        public bool Intersects(in BoundingBox box)
        {
            return box.IntersectsTriangle(A, B, C);
        }

        /// <summary>
        /// Moller-Trumbore Intersection
        /// </summary>
        /// <param name="ray"></param>
        /// <param name="t"></param>
        /// <returns></returns>
        [Pure]
        public bool Intersects(in Ray ray, out float t)
        {
            var edge2 = C - A;
            var edge1 = B - A;
            
            t = float.NaN;
            const float epsilon = 0.0000001f;
            var h = ray.Direction.Cross(edge2);
            var a = h.Dot(ref edge1);
            if (a > -epsilon && a < epsilon)
                return false;
            var f = 1 / a;
            var s = ray.Position - A;
            var u = f * (s.Dot(ref h));
            if (u < 0.0 || u > 1.0)
                return false;
            var q = s.Cross(edge1);
            var v = f * ray.Direction.Dot(ref q);
            if (v < 0.0 || u + v > 1.0)
                return false;
            t = f * q.Dot(ref edge2);
            return t > epsilon;
        }
    }
}