using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Data.Cartographic
{
    public readonly struct EquiCartographicLocation
    {
        public readonly byte Face;
        public readonly Vector2D TexCoords;
        public readonly double ElevationFactor;

        public EquiCartographicLocation(byte face, Vector2D texCoords, double elevationFactor)
        {
            Face = face;
            TexCoords = texCoords;
            ElevationFactor = elevationFactor;
        }

        public Vector3D ToLocal(MyPlanet planet)
        {
            var radius = MathHelper.Lerp(planet.MinimumRadius, planet.MaximumRadius, ElevationFactor);
            MyEnvironmentCubemapHelper.TexcoordToWorld(TexCoords, Face, radius, out var localPosition);
            return localPosition;
        }

        public Vector3D ToWorld(MyPlanet planet)
        {
            var localPosition = ToLocal(planet);
            var worldMatrix = planet.PositionComp.WorldMatrix;
            return Vector3D.Transform(in localPosition, in worldMatrix);
        }

        public static EquiCartographicLocation FromLocal(MyPlanet planet, Vector3D localPos)
        {
            var radius = localPos.Length();
            var elevationFactor = (radius - planet.MinimumRadius) / (planet.MaximumRadius - planet.MinimumRadius);
            MyEnvironmentCubemapHelper.ProjectToCube(ref localPos, out var face, out var texCoords);
            return new EquiCartographicLocation((byte)face, texCoords, elevationFactor);
        }

        public static EquiCartographicLocation FromWorld(MyPlanet planet, in Vector3D worldPos)
        {
            var worldMatrixInv = planet.PositionComp.WorldMatrixInvScaled;
            var localPos = Vector3D.Transform(in worldPos, in worldMatrixInv);
            return FromLocal(planet, localPos);
        }
    }
}