using System;
using System.Collections.Generic;
using System.Threading;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Collections;
using VRage.Import;
using VRage.Library.Collections;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Messages;

namespace Equinox76561198048419394.Core.Mesh
{
    public static class EquiMeshHelpers
    {
        public struct LineData
        {
            public string Material;
            public Vector3 Pt0, Pt1;
            public Vector2 UvOffset, UvTangent, UvNormal;
            public float Width;
            public int Segments;
            public int HalfSideSegments;

            public bool UseNaturalGravity;
            public Vector3 Gravity;
            public float CatenaryLength;

            public PackedHsvShift ColorMask;
        }

        public static bool TrySolveCatenary(in LineData line, List<Vector3> points)
        {
            if (line.CatenaryLength <= 0) return false;
            var gravity = line.Gravity;
            var gravityMagnitude = gravity.LengthSquared();
            if (gravityMagnitude <= 0) return false;
            gravity /= -(float)Math.Sqrt(gravityMagnitude);

            var h1 = gravity.Dot(line.Pt0);
            var xy1 = line.Pt0 - h1 * gravity;
            var h2 = gravity.Dot(line.Pt1);
            var xy2 = line.Pt1 - h2 * gravity;

            var horizontalDist = Vector3.Distance(xy1, xy2);
            var heightDist = h2 - h1;

            if (!CatenarySolver.TrySolve(horizontalDist, heightDist, line.CatenaryLength, out var eqn))
                return false;

            for (var i = 0; i <= line.Segments; i++)
            {
                var f = i / (float)line.Segments;
                points.Add(Vector3.Lerp(xy1, xy2, f) + gravity * (h1 + eqn.Evaluate(horizontalDist * f)));
            }

            return true;
        }

        public static void BuildLine(in LineData line, MyModelData mesh)
        {
            var pointA = line.Pt0;
            var pointB = line.Pt1;
            using (PoolManager.Get<List<Vector3>>(out var points))
            {
                points.EnsureCapacity(line.Segments + 1);
                if (!TrySolveCatenary(in line, points))
                    for (var i = 0; i <= line.Segments; i++)
                        points.Add(Vector3.Lerp(pointA, pointB, i / (float)line.Segments));
                var indexOffset = mesh.Positions.Count;

                var sideSegments = line.HalfSideSegments * 2;
                var verticesPerRing = sideSegments + 1;
                var reserveVertexCapacity = indexOffset + verticesPerRing * points.Count;
                mesh.Positions.EnsureCapacity(reserveVertexCapacity);
                mesh.TexCoords.EnsureCapacity(reserveVertexCapacity);
                mesh.Normals.EnsureCapacity(reserveVertexCapacity);
                mesh.Tangents.EnsureCapacity(reserveVertexCapacity);
                var biasWithGravity = line.Gravity.LengthSquared() > 0;

                for (var i = 0; i < points.Count; ++i)
                {
                    var lineTangent = Vector3.Zero;
                    if (i > 0) lineTangent += Vector3.Normalize(points[i] - points[i - 1]);
                    if (i + 1 < points.Count) lineTangent += Vector3.Normalize(points[i + 1] - points[i]);
                    if (i > 0 && i + 1 < points.Count) lineTangent.Normalize();
                    var normal = Vector3.Zero;
                    if (biasWithGravity)
                    {
                        var biTangent = Vector3.Cross(line.Gravity, lineTangent);
                        normal = Vector3.Cross(biTangent, lineTangent);
                    }

                    if (normal.LengthSquared() < 1e-3)
                        lineTangent.CalculatePerpendicularVector(out normal);
                    normal.Normalize();
                    Vector3.Cross(ref lineTangent, ref normal, out var binormal);

                    var pt = points[i];
                    var fraction = i / (float)line.Segments;
                    var uv = line.UvOffset + fraction * line.UvTangent;

                    var halfWidth = line.Width / 2;
                    for (var j = 0; j < verticesPerRing; j++)
                    {
                        var theta = MathHelper.TwoPi * j / sideSegments;
                        var dir = (float)Math.Cos(theta) * normal + (float)Math.Sin(theta) * binormal; 

                        mesh.Positions.Add(pt + dir * halfWidth);
                        mesh.TexCoords.Add(uv + j * line.UvNormal / sideSegments);
                        mesh.Normals.Add(dir);
                        mesh.Tangents.Add(Vector3.Cross(lineTangent, dir));
                    }
                }

                mesh.Indices.EnsureCapacity(mesh.Indices.Count + 6 * sideSegments * (points.Count - 1) + 3 * (sideSegments - 2));
                for (var end = 0; end < 2; end++)
                {
                    var io = indexOffset + verticesPerRing * end * (points.Count - 1);
                    for (var i = 2; i < sideSegments; i++)
                    {
                        mesh.Indices.Add(io);
                        if (end == 0)
                        {
                            mesh.Indices.Add(io + i - 1);
                            mesh.Indices.Add(io + i);
                        }
                        else
                        {
                            mesh.Indices.Add(io + i);
                            mesh.Indices.Add(io + i - 1);
                        }
                    }
                }
                for (var ring = 0; ring < points.Count - 1; ring++)
                {
                    var io = indexOffset + verticesPerRing * ring;
                    for (var i = 0; i < sideSegments; i++)
                    {
                        var j = i + 1;
                        var v00 = io + i;
                        var v10 = io + verticesPerRing + i;
                        var v01 = io + j;
                        var v11 = io + verticesPerRing + j;

                        mesh.Indices.Add(v00);
                        mesh.Indices.Add(v10);
                        mesh.Indices.Add(v11);

                        mesh.Indices.Add(v00);
                        mesh.Indices.Add(v11);
                        mesh.Indices.Add(v01);
                    }
                }
            }
        }

        public struct VertexData
        {
            public Vector3 Position;
            public HalfVector2 Uv;
            // Normal should be aligned such that the vertices are in counter clockwise order when the normal is pointing to the camera.
            public uint Normal;
            public uint Tangent;
        }

        public struct SurfaceData
        {
            public string Material;
            public VertexData Pt0, Pt1, Pt2;

            public VertexData? Pt3;

            public bool FlipRearNormals;

            public PackedHsvShift ColorMask;

            public float Area
            {
                get
                {
                    var area = Vector3.Cross(Pt1.Position - Pt0.Position, Pt2.Position - Pt0.Position).Length() / 2;
                    if (Pt3.HasValue)
                        area += Vector3.Cross(Pt2.Position - Pt0.Position, Pt3.Value.Position - Pt0.Position).Length() / 2;
                    return area;
                }
            }
        }

        public static void SortSurfacePositions(Vector3 alignment, ref Vector3 a, ref Vector3 b, ref Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).Dot(alignment) < 0)
                MyUtils.Swap(ref b, ref c);
        }

        private static readonly MyConcurrentQueue<QuadSortKey[]> QuadSorting = new MyConcurrentQueue<QuadSortKey[]>();

        private readonly struct QuadSortKey
        {
            public readonly Vector3 Pos;
            public readonly float Key;

            public QuadSortKey(Vector3 pos, float key)
            {
                Pos = pos;
                Key = key;
            }

            public QuadSortKey(Vector3 pos, Vector3 dir0, Vector3 normal, Vector3 center)
            {
                Pos = pos;
                var dir = pos - center;
                if (dir.Normalize() < 1e-4)
                {
                    Key = normal.Dot(pos - center);
                    return;
                }

                var dot = dir0.Dot(dir);
                var cross = dir0.Cross(dir).Dot(normal);
                Key = (cross < 0 ? -1 : 1) * (1 - dot);
            }
        }

        private static Vector3 AlignedNormal(Vector3 alignment, Vector3 a, Vector3 b, Vector3 c)
        {
            var norm = (a - b).Cross(c - b);
            if (norm.Dot(alignment) < 0) norm = -norm;
            if (norm.Normalize() > 1e-3f)
                return norm;
            norm = alignment;
            if (norm.Normalize() < 1e-3f)
                norm = Vector3.Left;
            return norm;
        }

        public static void SortSurfacePositions(Vector3 alignment, ref Vector3 a, ref Vector3 b, ref Vector3 c, ref Vector3 d)
        {
            var center = (a + b + c + d) / 4;
            var norm = AlignedNormal(alignment, a, b, c) + AlignedNormal(alignment, b, c, d);
            if (norm.Normalize() < 1e-3f)
            {
                norm = alignment;
                if (norm.Normalize() < 1e-3f)
                    norm = Vector3.Left;
            }

            if (!QuadSorting.TryDequeue(out var temp))
                temp = new QuadSortKey[4];
            var dir0 = a - center;
            if (dir0.LengthSquared() < 1e-6f)
                dir0 = Vector3.Up;
            else
                dir0.Normalize();
            temp[0] = new QuadSortKey(a, 0);
            temp[1] = new QuadSortKey(b, dir0, norm, center);
            temp[2] = new QuadSortKey(c, dir0, norm, center);
            temp[3] = new QuadSortKey(d, dir0, norm, center);
            Array.Sort(temp, (k1, k2) => k1.Key.CompareTo(k2.Key));
            a = temp[0].Pos;
            b = temp[1].Pos;
            c = temp[2].Pos;
            d = temp[3].Pos;
            QuadSorting.Enqueue(temp);

            // AC should pass closer to the center than BD
            float ScoreLine(Vector3 pt1, Vector3 pt2)
            {
                var dir = pt2 - pt1;
                var nearestPoint = pt1 * (center - pt1).Dot(dir) / dir.LengthSquared() * dir;
                return Vector3.DistanceSquared(nearestPoint, center);
            }

            if (ScoreLine(a, c) < ScoreLine(b, d)) return;

            // Rotate a,b,c,d
            var tmpD = d;
            d = c;
            c = b;
            b = a;
            a = tmpD;
        }

        public static void BuildSurface(in SurfaceData tri, MyModelData mesh)
        {
            var repeats = tri.FlipRearNormals ? 2 : 1;
            var singleSideVertices = tri.Pt3.HasValue ? 4 : 3;
            var neededVertices = mesh.Positions.Count + singleSideVertices * repeats;
            mesh.Positions.EnsureCapacity(neededVertices);
            mesh.TexCoords.EnsureCapacity(neededVertices);
            mesh.Normals.EnsureCapacity(neededVertices);
            mesh.Tangents.EnsureCapacity(neededVertices);

            var vertexOffset = mesh.Positions.Count;
            for (var i = 0; i < repeats; i++)
            {
                void AddVertex(in VertexData vtx)
                {
                    var normal = VF_Packer.UnpackNormal(vtx.Normal);
                    var tangent = VF_Packer.UnpackNormal(vtx.Tangent);
                    if (i == 1) normal = -normal;
                    mesh.Positions.Add(vtx.Position);
                    mesh.TexCoords.Add(vtx.Uv.ToVector2());
                    mesh.Normals.Add(normal);
                    mesh.Tangents.Add(tangent);
                }

                AddVertex(in tri.Pt0);
                AddVertex(in tri.Pt1);
                AddVertex(in tri.Pt2);
                if (tri.Pt3.HasValue)
                    AddVertex(tri.Pt3.Value);
            }

            var indexOffset = mesh.Indices.Count;
            var singleSideIndices = tri.Pt3.HasValue ? 6 : 3;
            mesh.Indices.EnsureCapacity(indexOffset + singleSideIndices * 2);

            mesh.Indices.Add(vertexOffset);
            mesh.Indices.Add(vertexOffset + 2);
            mesh.Indices.Add(vertexOffset + 1);

            if (tri.Pt3.HasValue)
            {
                mesh.Indices.Add(vertexOffset);
                mesh.Indices.Add(vertexOffset + 3);
                mesh.Indices.Add(vertexOffset + 2);
            }

            var secondSideOffset = tri.FlipRearNormals ? singleSideVertices : 0;
            for (var i = indexOffset; i < indexOffset + singleSideIndices; i += 3)
            {
                var i0 = mesh.Indices[i] + secondSideOffset;
                var i1 = mesh.Indices[i + 1] + secondSideOffset;
                var i2 = mesh.Indices[i + 2] + secondSideOffset;

                mesh.Indices.Add(i0);
                mesh.Indices.Add(i2);
                mesh.Indices.Add(i1);
            }
        }

        public struct DecalData
        {
            public string Material;
            public HalfVector2 TopLeftUv;
            public HalfVector2 BottomRightUv;

            public Vector3 Position;
            public uint Normal;
            public HalfVector3 Up;
            public HalfVector3 Left;

            public PackedHsvShift ColorMask;
        }

        public static void BuildDecal(in DecalData data, MyModelData mesh)
        {
            var neededVertices = mesh.Positions.Count + 4;
            mesh.Positions.EnsureCapacity(neededVertices);
            mesh.TexCoords.EnsureCapacity(neededVertices);
            mesh.Normals.EnsureCapacity(neededVertices);
            mesh.Tangents.EnsureCapacity(neededVertices);

            var normal = VF_Packer.UnpackNormal(data.Normal);
            Vector3 up = data.Up;
            Vector3 left = data.Left;

            var upLeftUv = data.TopLeftUv.ToVector2();
            var downRightUv = data.BottomRightUv.ToVector2();

            var tangent = Math.Sign(downRightUv.X - upLeftUv.X) * left;
            tangent.Normalize();

            var vertexOffset = mesh.Positions.Count;
            mesh.Positions.Add(data.Position + up + left);
            mesh.TexCoords.Add(upLeftUv);
            mesh.Normals.Add(normal);
            mesh.Tangents.Add(tangent);

            mesh.Positions.Add(data.Position - up + left);
            mesh.TexCoords.Add(new Vector2(upLeftUv.X, downRightUv.Y));
            mesh.Normals.Add(normal);
            mesh.Tangents.Add(tangent);

            mesh.Positions.Add(data.Position - up - left);
            mesh.TexCoords.Add(downRightUv);
            mesh.Normals.Add(normal);
            mesh.Tangents.Add(tangent);

            mesh.Positions.Add(data.Position + up - left);
            mesh.TexCoords.Add(new Vector2(downRightUv.X, upLeftUv.Y));
            mesh.Normals.Add(normal);
            mesh.Tangents.Add(tangent);

            mesh.Indices.EnsureCapacity(mesh.Indices.Count + 6);
            mesh.Indices.Add(vertexOffset);
            mesh.Indices.Add(vertexOffset + 2);
            mesh.Indices.Add(vertexOffset + 1);
            mesh.Indices.Add(vertexOffset);
            mesh.Indices.Add(vertexOffset + 3);
            mesh.Indices.Add(vertexOffset + 2);
        }

        public static Vector3 ComputeTriangleTangent(Vector3 edgeDelta1, Vector2 uvDelta1, Vector3 edgeDelta2, Vector2 uvDelta2)
        {
            // uv(j, k) = uvDelta1 * j + uvDelta2 * k 

            var det = uvDelta1.Y * uvDelta2.X - uvDelta1.X * uvDelta2.Y;
            if (Math.Abs(det) < 1e-6f) return Vector3.Zero;
                
            // Solution for uv(j, k) == [1, 0]
            var j = -uvDelta2.Y / det;
            var k = uvDelta1.Y / det;

            // pos(j, k) = edgeDelta1 * j + edgeDelta2 * k
            return edgeDelta1 * j + edgeDelta2 * k;
        }

        public static Vector3 MakePerpendicular(Vector3 modify, Vector3 normal)
        {
            Vector3.Reject(ref modify, ref normal, out var result);
            if (result.LengthSquared() < 1e-6)
                normal.CalculatePerpendicularVector(out result);
            return result;
        }
    }
}