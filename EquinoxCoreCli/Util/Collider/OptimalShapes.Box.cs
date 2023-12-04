using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Collider
{
    public static partial class OptimalShapes
    {
        public static OrientedBoundingBox OrientedBox(EqReadOnlySpan<Vector3> points)
        {
            var firstGuess = RotatingCalipersAround(points, ApproxMaxAxis(points));

            var orientedExtents = Vector3.Transform(firstGuess.HalfExtent, firstGuess.Orientation);

            var optimalVolume = float.PositiveInfinity;
            OrientedBoundingBox optimalBox = default;
            const int steps = 10;
            for (var i = -steps; i <= steps; i++)
            for (var j = -steps; j <= steps; j++)
            {
                var guess = RotatingCalipersAround(points, new Vector3(
                    orientedExtents.X * i / steps, orientedExtents.Y * j / steps, orientedExtents.Z));
                var volume = guess.HalfExtent.Volume * 8;
                if (volume >= optimalVolume)
                    continue;
                optimalVolume = volume;
                optimalBox = guess;
            }

            var corners = new Vector3[8];
            optimalBox.GetCorners(corners, 0);
            return optimalBox;
        }

        private static Vector3 ApproxMaxAxis(EqReadOnlySpan<Vector3> points)
        {
            var x = ApproxAxis.Start;
            var y = ApproxAxis.Start;
            var z = ApproxAxis.Start;
            for (var i = 0; i < points.Length; i++)
            {
                ref readonly var pt = ref points[i];
                x.Observe(i, pt.X);
                y.Observe(i, pt.Y);
                z.Observe(i, pt.Z);
            }

            var best = x;
            if (y.Span > best.Span)
                best = y;
            if (z.Span > best.Span)
                best = z;
            return points[best.MaxIndex] - points[best.MinIndex];
        }

        private struct ApproxAxis
        {
            public int MinIndex;
            public float MinValue;
            public int MaxIndex;
            public float MaxValue;

            public float Span => MaxValue - MinValue;

            public static ApproxAxis Start => new ApproxAxis { MinValue = float.PositiveInfinity, MaxValue = float.NegativeInfinity };

            public void Observe(int index, float value)
            {
                if (value < MinValue)
                {
                    MinIndex = index;
                    MinValue = value;
                }

                if (value > MaxValue)
                {
                    MaxIndex = index;
                    MaxValue = value;
                }
            }
        }

        private static OrientedBoundingBox RotatingCalipersAround(EqReadOnlySpan<Vector3> points, Vector3 axis)
        {
            axis.Normalize();
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            var caliperTransform = Matrix.CreateWorld(Vector3.Zero, axis, Vector3.CalculatePerpendicularVector(axis));
            Matrix.Transpose(ref caliperTransform, out var caliperTransformInv);

            using (EqSpan<Vector2>.AllocateTemp(points.Length, out var planar))
            {
                for (var i = 0; i < points.Length; i++)
                {
                    ref readonly var pt = ref points[i];
                    var rotated = Vector3.TransformNormal(pt, ref caliperTransformInv);
                    ref var pt2 = ref planar[i];
                    pt2.X = rotated.X;
                    pt2.Y = rotated.Y;
                    if (rotated.Z < minZ)
                        minZ = rotated.Z;
                    if (rotated.Z > maxZ)
                        maxZ = rotated.Z;
                }

                RotatingCalipers(planar, out var y2D, out var box2D);
                var boxTransform = Matrix.CreateWorld(
                    Vector3.Zero,
                    axis,
                    Vector3.TransformNormal(new Vector3(y2D.X, y2D.Y, 0), ref caliperTransform));

                return new OrientedBoundingBox(
                    new BoundingBox(
                        new Vector3(box2D.Min, minZ),
                        new Vector3(box2D.Max, maxZ)
                    ),
                    boxTransform
                );
            }
        }

        private static void RotatingCalipers(EqSpan<Vector2> points, out Vector2 yAxis, out BoundingBox2 box)
        {
            var hullEdges = QuickHull(points);
            yAxis = default;
            box = default;
            var area = float.PositiveInfinity;
            for (var i = 0; i < hullEdges.Count - 1; i++)
            {
                ref var from = ref points[hullEdges[i]];
                ref var to = ref points[hullEdges[i + 1]];
                Vector2.Subtract(ref to, ref from, out var candidateX);
                candidateX.Normalize();
                var candidateY = new Vector2(-candidateX.Y, candidateX.X);

                var candidateBox = BoundingBox2.CreateInvalid();
                foreach (ref var pt in points)
                {
                    Vector2.Dot(ref candidateX, ref pt, out var x);
                    Vector2.Dot(ref candidateY, ref pt, out var y);
                    candidateBox.Include(new Vector2(x, y));
                }

                var candidateArea = candidateBox.Area();
                if (candidateArea >= area)
                    continue;
                yAxis = candidateY;
                area = candidateArea;
                box = candidateBox;
            }
        }

        private static List<int> QuickHull(EqSpan<Vector2> points, List<int> edges = null)
        {
            edges ??= new List<int>();
            MinMax(points, out var minI, out var maxI);
            edges.Add(minI);
            edges.Add(maxI);
            edges.Add(minI);

            var i = 0;
            while (i < edges.Count - 1)
            {
                var fromI = edges[i];
                var toI = edges[i + 1];
                ref var from = ref points[fromI];
                ref var to = ref points[toI];
                var outward = new Vector2(from.Y - to.Y, to.X - from.X);

                var farthestIndex = -1;
                var farthestDistance = float.Epsilon;
                for (var j = 0; j < points.Length; j++)
                {
                    if (j == fromI || j == toI) continue;
                    Vector2.Subtract(ref points[j], ref from, out var rel);
                    Vector2.Dot(ref outward, ref rel, out var dist);
                    if (dist <= farthestDistance) continue;
                    farthestIndex = j;
                    farthestDistance = dist;
                }

                if (farthestIndex < 0)
                {
                    i++;
                    continue;
                }

                if (edges.Contains(farthestIndex))
                {
                    continue;
                }

                edges.Insert(i + 1, farthestIndex);
            }

            return edges;
        }

        private static void MinMax(EqSpan<Vector2> points, out int min, out int max)
        {
            const float tolerance = 1e-10f;
            min = max = 0;
            for (var i = 0; i < points.Length; i++)
            {
                ref var pt = ref points[i];
                ref var minV = ref points[min];
                ref var maxV = ref points[max];
                if (pt.X < minV.X)
                    min = i;
                else if (pt.X < minV.X + tolerance && pt.Y < minV.Y)
                    min = i;

                if (pt.X > maxV.X)
                    max = i;
                else if (pt.X > maxV.X - tolerance && pt.Y > maxV.Y)
                    max = i;
            }
        }
    }
}