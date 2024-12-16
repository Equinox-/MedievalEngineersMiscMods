using System;
using System.Collections.Generic;
using Medieval.GameSystems;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.Players;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public static class PlanetAreasExtensions
    {
        public const int AdjacentAreaCount = 4;

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