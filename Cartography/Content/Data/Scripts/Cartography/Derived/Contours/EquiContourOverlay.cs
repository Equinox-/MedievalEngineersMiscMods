using System;
using System.Collections.Generic;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using VRage.Components.Entity.Camera;
using VRage.Library.Collections;
using VRageMath;
using VRageRender;
using MySession = VRage.Session.MySession;

namespace Equinox76561198048419394.Cartography.Derived.Contours
{
    public class EquiContourOverlay
    {
        internal static float FakeDepthBiasFactor = .05f;

        private readonly LRUCache<WorldSpaceContourKey, WorldSpaceContourVertices> _worldSpaceVertices =
            new LRUCache<WorldSpaceContourKey, WorldSpaceContourVertices>(16);

        public void RenderOverlay(EquiContourOptions options)
        {
            if (MyCameraComponent.ActiveCamera == null)
                return;
            var cameraPos = MyCameraComponent.ActiveCamera.GetPosition();
            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(cameraPos);
            if (planet == null)
                return;
            var areas = planet.Get<MyPlanetAreasComponent>();
            using (PoolManager.Get(out HashSet<long> rendered))
            {
                var useRegions = false;
                var renderDistance = Math.Max(options.OverlayMinorRenderDistance, options.OverlayMajorRenderDistance);
                var offsetInAreas = (int)Math.Ceiling(renderDistance / areas.ApproximateSectorSize);
                if (offsetInAreas >= 2)
                    useRegions = true;

                foreach (var offset in new BoundingBoxI(new Vector3I(-offsetInAreas), new Vector3I(offsetInAreas + 1)).EnumeratePoints())
                {
                    var pos = cameraPos + offset * areas.ApproximateSectorSize;
                    var spaceId = useRegions ? areas.GetRegion(pos) : areas.GetArea(pos);
                    if (rendered.Add(spaceId))
                        RenderRegionOverlay(planet, useRegions, spaceId, options);
                }
            }
        }

        private void RenderRegionOverlay(MyPlanet planet, bool regionBacked, long spaceId, EquiContourOptions options)
        {
            MyPlanetAreasComponent.UnpackAreaId(spaceId, out var face, out int regionX, out var regionY);
            var areas = planet.Get<MyPlanetAreasComponent>();
            var scalingCount = regionBacked ? areas.RegionCount : areas.AreaCount;
            var minTexCoord = (2 * new Vector2(regionX, regionY) - scalingCount) / scalingCount;
            var maxTexCoord = (2 * new Vector2(regionX + 1, regionY + 1) - scalingCount) / scalingCount;

            var rectangle = new RectangleF(minTexCoord, maxTexCoord - minTexCoord);

            var contourArgs = new ContourArgs(planet, face, rectangle, options.ContourInterval);
            var contours = MySession.Static.Components.Get<EquiCartography>()?.GetOrComputeContoursAsync(contourArgs);
            if (contours == null)
                return;
            var worldSpaceKey = new WorldSpaceContourKey(contours);
            if (!_worldSpaceVertices.TryRead(worldSpaceKey, out var worldSpaceContourVertices))
            {
                worldSpaceContourVertices = new WorldSpaceContourVertices(in contourArgs, contours);
                _worldSpaceVertices.Write(worldSpaceKey, worldSpaceContourVertices);
            }

            var cameraPosWorld = MyCameraComponent.ActiveCamera.GetPosition();
            var frustum = MyCameraComponent.ActiveCamera.GetCameraFrustum();
            var playerElevation = Vector3D.Distance(MyCameraComponent.ActiveCamera?.Entity?.GetPosition() ?? cameraPosWorld, planet.GetPosition()) -
                                  planet.AverageRadius;
            var highlightContour = (int)Math.Round(playerElevation / contourArgs.ContourInterval);

            using (var batchDepthTest = MyRenderProxy.DebugDrawLine3DOpenBatch(true))
            using (var batchDepthIgnore = MyRenderProxy.DebugDrawLine3DOpenBatch(false))
            {
                var worldOffset = worldSpaceContourVertices.Offset;
                var cameraPosLocal = (Vector3)(cameraPosWorld - worldOffset);
                var worldMatrix = MatrixD.CreateTranslation(worldOffset);
                batchDepthTest.WorldMatrix = worldMatrix;
                batchDepthIgnore.WorldMatrix = worldMatrix;

                for (var i = 0; i < contours.Lines.Count; i++)
                {
                    var contour = contours.Lines[i];
                    var major = options.MajorContourEvery > 0 && (contour.ContourId % options.MajorContourEvery) == 0;
                    var renderDistanceSq = major ? options.OverlayMajorRenderDistance : options.OverlayMinorRenderDistance;
                    renderDistanceSq *= renderDistanceSq;

                    // If the entire contour is far away skip it.
                    ref var contourBox = ref worldSpaceContourVertices.ContourBoxes[i];
                    if (contourBox.DistanceSquared(cameraPosLocal) > renderDistanceSq)
                        continue;

                    var worldBox = new BoundingBoxD(worldOffset + contourBox.Min, worldOffset + contourBox.Max);
                    if (!frustum.Intersects(worldBox))
                        continue;

                    var color = major ? options.MajorContourColor : options.MinorContourColor;
                    if (options.HighlightContourColor.HasValue && highlightContour == contour.ContourId)
                        color = options.HighlightContourColor.Value;

                    var depthTestDistanceSq = major ? options.OverlayMajorDepthTestDistanceSq : options.OverlayMinorDepthTestDistanceSq;

                    Vector3 ContourToWorld(int vertex, out bool visible, out bool useDepth)
                    {
                        var res = worldSpaceContourVertices.Vertices[vertex];
                        var cameraDistanceSq = Vector3.DistanceSquared(cameraPosLocal, res);
                        visible = cameraDistanceSq < renderDistanceSq;
                        useDepth = cameraDistanceSq >= depthTestDistanceSq;
                        return res;
                    }

                    var prev = ContourToWorld(contour.StartVertex, out var prevVisible, out var prevUseDepth);
                    for (var v = contour.StartVertex + 1; v <= contour.EndVertex; v++)
                    {
                        var curr = ContourToWorld(v, out var currVisible, out var currUseDepth);
                        if (prevVisible || currVisible)
                        {
                            if (prevUseDepth && currUseDepth)
                                batchDepthTest.AddLine(FakeDepthBias(prev), color, FakeDepthBias(curr), color);
                            else
                                batchDepthIgnore.AddLine(prev, color, curr, color);
                        }

                        prev = curr;
                        prevVisible = currVisible;
                        prevUseDepth = currUseDepth;
                    }
                }

                Vector3 FakeDepthBias(Vector3 pos) => Vector3.Lerp(pos, cameraPosLocal, FakeDepthBiasFactor);
            }
        }

        // LRUCache doesn't support reference type keys.
        private readonly struct WorldSpaceContourKey : IEquatable<WorldSpaceContourKey>
        {
            private readonly ContourData _data;

            public WorldSpaceContourKey(ContourData data) => _data = data;

            public bool Equals(WorldSpaceContourKey other) => Equals(_data, other._data);

            public override bool Equals(object obj) => obj is WorldSpaceContourKey other && Equals(other);

            public override int GetHashCode() => (_data != null ? _data.GetHashCode() : 0);
        }

        private sealed class WorldSpaceContourVertices
        {
            public readonly Vector3D Offset;
            public readonly Vector3[] Vertices;
            public readonly BoundingBox[] ContourBoxes;

            public WorldSpaceContourVertices(in ContourArgs args, ContourData data)
            {
                Vertices = new Vector3[data.VertexCount];
                ContourBoxes = new BoundingBox[data.Lines.Count];
                ref readonly var rectangle = ref args.Area;
                MyEnvironmentCubemapHelper.TexcoordToWorld(rectangle.Position + rectangle.Size / 2, args.Face, args.Planet.AverageRadius, out Offset);
                for (var i = 0; i < data.Lines.Count; i++)
                {
                    var contour = data.Lines[i];
                    ref var box = ref ContourBoxes[i];
                    box = BoundingBox.CreateInvalid();
                    for (var v = contour.StartVertex; v <= contour.EndVertex; v++)
                    {
                        var pt = data.GetVertex(v);
                        var texCoord = rectangle.Position + rectangle.Size * pt;
                        MyEnvironmentCubemapHelper.TexcoordToWorld(texCoord, args.Face, contour.ContourElevation + args.Planet.AverageRadius, out var pos);
                        ref var vertex = ref Vertices[v];
                        vertex = (Vector3)(pos - Offset);
                        box.Include(vertex);
                    }
                }
            }
        }
    }
}