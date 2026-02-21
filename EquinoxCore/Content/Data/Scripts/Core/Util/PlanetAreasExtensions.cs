using System;
using System.Collections.Generic;
using Medieval.GameSystems;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.Players;
using VRage;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class PlanetAreasExtensions
    {
        public const int AdjacentAreaCount = 4;

        public static void UnpackAreaIdToNames(this MyPlanetAreasComponent areas, long areaId, out string kingdom, out string region, out string area)
        {
            var areasPerRegion = areas.AreasPerRegionCount;
            MyPlanetAreasComponent.UnpackAreaId(areaId, out int face, out var x, out var y);
            var regionX = x / areasPerRegion;
            var regionY = y / areasPerRegion;
            var areaX = x % areasPerRegion;
            var areaY = x % areasPerRegion;
            kingdom = MyTexts.GetString(MyPlanetAreasComponent.KingdomNames[face]);
            region = (char) ('A' + regionX) + (regionY + 1).ToString();
            area = (char) ('A' + areaX) + (areaY + 1).ToString();
        }

        public static void UnpackRegionIdToNames(this MyPlanetAreasComponent areas, long regionId, out string kingdom, out string region)
        {
            MyPlanetAreasComponent.UnpackAreaId(regionId, out int face, out var x, out var y);
            kingdom = MyTexts.GetString(MyPlanetAreasComponent.KingdomNames[face]);
            region = (char) ('A' + x) + (y + 1).ToString();
        }

        public static Vector3D CalculateRegionCenter(this MyPlanetAreasComponent areas, long regionId)
        {
            var regionCount = areas.RegionCount;
            var planetRadius = areas.Planet.AverageRadius;
            MyPlanetAreasComponent.UnpackAreaId(regionId, out int face, out var x, out var y);
            MyEnvironmentCubemapHelper.TexcoordToWorld(new Vector2D((2.0 * x - regionCount + 0.5) / regionCount, (2.0 * y - regionCount + 0.5) / regionCount),
                face, planetRadius, out var position);
            return position;
        }

        public static Vector3D CalculateKingdomCenter(this MyPlanetAreasComponent areas, int kingdomId)
        {
            var planetRadius = areas.Planet.AverageRadius;
            MyEnvironmentCubemapHelper.TexcoordToWorld(Vector2D.Zero, kingdomId, planetRadius, out var position);
            return position;
        }

        public static long GetAdjacentArea(this MyPlanetAreasComponent comp, long area, int neighbor)
        {
            var areasPerFace = comp.AreaCount;
            Vector2I coords;
            MyPlanetAreasComponent.UnpackAreaId(area, out var face, out coords.X, out coords.Y);
            switch (neighbor)
            {
                case 0:
                    coords.X--;
                    if (coords.X < 0)
                        MyEnvironmentCubemapHelper.TranslatePixelCoordsToNeighboringFace(coords, face, 0, areasPerFace, out face, out coords);
                    break;
                case 1:
                    coords.X++;
                    if (coords.X >= areasPerFace)
                        MyEnvironmentCubemapHelper.TranslatePixelCoordsToNeighboringFace(coords, face, 1, areasPerFace, out face, out coords);
                    break;
                case 2:
                    coords.Y--;
                    if (coords.Y < 0)
                        MyEnvironmentCubemapHelper.TranslatePixelCoordsToNeighboringFace(coords, face, 2, areasPerFace, out face, out coords);
                    break;
                case 3:
                    coords.Y++;
                    if (coords.Y >= areasPerFace)
                        MyEnvironmentCubemapHelper.TranslatePixelCoordsToNeighboringFace(coords, face, 3, areasPerFace, out face, out coords);
                    break;
                default:
                    throw new ArgumentException("Neighbor must be [0, 4)", nameof(neighbor));
            }

            return MyPlanetAreasComponent.PackAreaId(coords.X, coords.Y, face);
        }

        /// <summary>
        /// Visits an area.
        /// </summary>
        /// <param name="userData">user provided data</param>
        /// <param name="areaId">packed area ID</param>
        /// <returns>true if adjacent areas should be visited</returns>
        public delegate bool DelVisitArea<TUserData>(ref TUserData userData, long areaId);

        /// <summary>
        /// Flood fills areas based on a provided predicate.
        /// </summary>
        /// <param name="areas">area component</param>
        /// <param name="userData">user data</param>
        /// <param name="visit">visit method</param>
        /// <param name="startingArea">starting area ID</param>
        /// <param name="startingAreaIds">additional starting area IDs</param>
        public static void FloodFillAreas<TUserData>(
            this MyPlanetAreasComponent areas,
            ref TUserData userData,
            DelVisitArea<TUserData> visit,
            long? startingArea,
            long[] startingAreaIds = null)
        {
            using (PoolManager.Get(out Queue<long> explore))
            using (PoolManager.Get(out HashSet<long> explored))
            {
                if (startingArea.HasValue)
                    Queue(startingArea.Value);
                if (startingAreaIds != null)
                    foreach (var value in startingAreaIds)
                        Queue(value);
                while (explore.TryDequeue(out var areaId))
                {
                    if (!visit(ref userData, areaId)) continue;
                    for (var i = 0; i < AdjacentAreaCount; i++)
                        Queue(areas.GetAdjacentArea(areaId, i));
                }

                return;

                void Queue(long id)
                {
                    if (explored.Add(id)) explore.Enqueue(id);
                }
            }
        }
    }
}