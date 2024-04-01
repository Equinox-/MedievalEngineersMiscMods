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
    }
}