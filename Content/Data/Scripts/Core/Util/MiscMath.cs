using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class MiscMath
    {
        public static uint InterleavedBits(uint x)
        {
            x &= 0x000003FFu;
            x = (x ^ (x << 16)) & 0xFF0000FFu;
            x = (x ^ (x << 8)) & 0x0300F00Fu;
            x = (x ^ (x << 4)) & 0x030C30C3u;
            x = (x ^ (x << 2)) & 0x09249249u;
            return x;
        }

        private static uint SignedInterleaveNorton(int x)
        {
            var raw = InterleavedBits((uint) Math.Abs(x));
            return (uint) ((raw << 3) | (x < 0 ? 1 : 0));
        }

        public static uint MortonCode(Vector3I v)
        {
            var x = SignedInterleaveNorton(v.X);
            var y = SignedInterleaveNorton(v.Y);
            var z = SignedInterleaveNorton(v.Z);
            return (uint) ((x << 2) | (y << 1) | (z));
        }

        public static double PlanetaryWavePhaseFactor(Vector3D worldPos, double periodMeters)
        {
            var planetCenter = MyGamePruningStructure.GetClosestPlanet(worldPos)?.GetPosition() ?? Vector3D.Zero;
            var planetDir = worldPos - planetCenter;
            var currRadius = planetDir.Normalize();

            var angle = Math.Acos(planetDir.X);
            var surfaceDistance = angle * currRadius;
            var elevationDistance = currRadius;
            return (surfaceDistance + elevationDistance) / periodMeters;
        }
        
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
    }
}