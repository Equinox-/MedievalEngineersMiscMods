using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public static class MiscMath
    {
        public static double PlanetaryWavePhaseFactor(Vector3D worldPos, double periodMeters)
        {
            var planetCenter = MyGamePruningStructureSandbox.GetClosestPlanet(worldPos)?.GetPosition() ?? Vector3D.Zero;
            var planetDir = worldPos - planetCenter;
            var currRadius = planetDir.Normalize();

            var angle = Math.Acos(planetDir.X);
            var surfaceDistance = angle * currRadius;
            var elevationDistance = currRadius;
            return (surfaceDistance + elevationDistance) / periodMeters;
        }

        public static float Volume(this in BoundingSphere sphere) => MathHelper.FourPi / 3 * sphere.Radius * sphere.Radius * sphere.Radius;

        public static float SurfaceAreaExt(this in BoundingBox box)
        {
            var size = box.Max - box.Min;
            return 2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
        }

        public static Vector3 ToDegrees(Vector3 vec) => vec * (180 / MathHelper.Pi);

        // r, theta (inc), phi (az)
        public static Vector3D ToSpherical(Vector3D world)
        {
            var r = world.Length();
            if (r <= 1e-3f)
                return new Vector3D(0, 0, r);
            var theta = Math.Acos(world.Z / r);
            var phi = Math.Atan2(world.Y, world.X);
            return new Vector3D(r, theta, phi);
        }

        public static Vector3D FromSpherical(Vector3D spherical)
        {
            var sinTheta = spherical.X * Math.Sin(spherical.Y);
            return new Vector3D(sinTheta * Math.Cos(spherical.Z), sinTheta * Math.Sin(spherical.Z),
                spherical.X * Math.Cos(spherical.Y));
        }

        public static double UnsignedModulo(double value, double modulus)
        {
            var result = value % modulus;
            if (result < 0)
                result += modulus;
            return result;
        }

        public static float UnsignedModulo(float value, float modulus)
        {
            var result = value % modulus;
            if (result < 0)
                result += modulus;
            return result;
        }

        public static int UnsignedModulo(int value, int modulus)
        {
            var result = value % modulus;
            if (result < 0)
                result += modulus;
            return result;
        }

        public static float SafeSign(float value) => value < 0 ? -1 : 1;
        public static double SafeSign(double value) => value < 0 ? -1 : 1;

        public static Vector3 SafeSign(in Vector3 vec) => new Vector3(SafeSign(vec.X), SafeSign(vec.Y), SafeSign(vec.Z));

        public static float DistanceSquared(this in Vector3 a, in Vector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static Vector3 ClosestPointOnLine(in Vector3 lineA, in Vector3 lineB, in Vector3 pt)
        {
            var aToBNorm = lineB - lineA;
            var aToBLength = aToBNorm.Normalize();
            if (aToBLength <= 1e-6f)
                return lineA;
            var aToPt = pt - lineA;
            var t = Vector3.Dot(aToPt, aToBNorm);
            if (t <= 0)
                return lineA;
            if (t >= aToBLength)
                return lineB;
            return lineA + aToBNorm * t;
        }

        public static float? CappedConeRayIntersection(
            Vector3 ro,
            Vector3 rd,
            Vector3 pa,
            Vector3 pb,
            float ra,
            float rb)
        {
            // https://iquilezles.org/articles/intersectors/
            Vector3.Subtract(ref pb, ref pa, out var ba);
            Vector3.Subtract(ref ro, ref pa, out var oa);
            Vector3.Subtract(ref ro, ref pb, out var ob);
            var m0 = ba.LengthSquared();
            Vector3.Dot(ref oa, ref ba, out var m1);
            Vector3.Dot(ref rd, ref ba, out var m2);
            Vector3.Dot(ref rd, ref oa, out var m3);
            var m5 = oa.LengthSquared();
            Vector3.Dot(ref ob, ref ba, out var m9);

            // caps
            if (m1 < 0.0)
            {
                if ((oa * m2 - rd * m1).LengthSquared() < (ra * ra * m2 * m2))
                    return -m1 / m2;
            }
            else if (m9 > 0.0)
            {
                var t1 = -m9 / m2;
                if ((ob + rd * t1).LengthSquared() < (rb * rb))
                    return t1;
            }

            // body
            var rr = ra - rb;
            var hy = m0 + rr * rr;
            var k2 = m0 * m0 - m2 * m2 * hy;
            var k1 = m0 * m0 * m3 - m1 * m2 * hy + m0 * ra * (rr * m2 * 1.0f);
            var k0 = m0 * m0 * m5 - m1 * m1 * hy + m0 * ra * (rr * m1 * 2.0f - m0 * ra);
            var h = k1 * k1 - k2 * k0;
            if (h < 0.0) return null; //no intersection
            var t = (-k1 - (float)Math.Sqrt(h)) / k2;
            var y = m1 + t * m2;
            if (y < 0.0 || y > m0) return null; //no intersection
            return t;
        }

        public static Line AsLine(this in Ray ray, float maxDistance = float.PositiveInfinity) => new Line
        {
            From = ray.Position,
            Direction = ray.Direction,
            Length = maxDistance,
            To = ray.Position + ray.Direction * (float.IsInfinity(maxDistance) ? 1e9f : maxDistance)
        };

        public static LineD AsLine(this in RayD ray, double maxDistance = double.PositiveInfinity) => new LineD
        {
            From = ray.Position,
            Direction = ray.Direction,
            Length = maxDistance,
            To = ray.Position + ray.Direction * (double.IsInfinity(maxDistance) ? 1e9 : maxDistance)
        };
    }
}