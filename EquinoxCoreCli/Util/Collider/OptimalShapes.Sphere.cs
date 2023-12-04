using System;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Collider
{
    public static partial class OptimalShapes
    {
        public static BoundingSphere Sphere(EqReadOnlySpan<Vector3> points)
        {
            var queue = new MyDeque<Vector3>(points.Length);
            foreach (var point in points)
                queue.EnqueueBack(point);
            return Sphere(queue);
        }

        private static BoundingSphere Sphere(MyDeque<Vector3> points)
        {
            var supports = default(StackArray<Vector3>);
            return OfRecursive(points, 0, ref supports);
        }

        // https://people.inf.ethz.ch/emo/PublFiles/SmallEnclDisk_LNCS555_91.pdf
        private static BoundingSphere OfRecursive(
            MyDeque<Vector3> points,
            int supportCount,
            ref StackArray<Vector3> supports)
        {
            if (points.Count == 0 || supportCount >= 4)
                return OfSurfacePoints(supportCount, ref supports);
            var point = points.DequeueBack();
            var candidate = OfRecursive(points, supportCount, ref supports);
            if (candidate.Radius > 0 && candidate.Contains(point) != ContainmentType.Disjoint)
            {
                points.EnqueueBack(point);
                return candidate;
            }

            supports.Ref(supportCount) = point;
            candidate = OfRecursive(points, supportCount + 1, ref supports);
            points.EnqueueFront(supports[supportCount]);
            return candidate;
        }

        private static BoundingSphere OfSurfacePoints(int count, ref StackArray<Vector3> supports)
        {
            switch (count)
            {
                case 0:
                    return BoundingSphere.CreateInvalid();
                case 1:
                    return new BoundingSphere(supports[0], 0);
                case 2:
                {
                    ref var a = ref supports.Ref(0);
                    ref var b = ref supports.Ref(1);
                    Vector3.Distance(ref a, ref b, out var twiceRadius);
                    Vector3.Add(ref a, ref b, out var twiceCenter);
                    return new BoundingSphere(twiceCenter / 2, twiceRadius / 2);
                }
                case 3:
                {
                    // https://en.wikipedia.org/wiki/Circumcircle#Higher_dimensions
                    ref var vA = ref supports.Ref(0);
                    ref var vB = ref supports.Ref(1);
                    ref var vC = ref supports.Ref(2);
                    Vector3.Subtract(ref vA, ref vC, out var a);
                    Vector3.Subtract(ref vB, ref vC, out var b);
                    Vector3.DistanceSquared(ref a, ref b, out var abLen2);
                    Vector3.Cross(ref a, ref b, out var abCross);
                    var aLen2 = a.LengthSquared();
                    var bLen2 = b.LengthSquared();
                    var abCrossLen2 = abCross.LengthSquared();
                    if (abCrossLen2 <= float.Epsilon)
                        return BoundingSphere.CreateInvalid();
                    var radius = (float) Math.Sqrt(aLen2 * bLen2 * abLen2 / (4 * abCrossLen2));
                    var center = vC + Vector3.Cross(aLen2 * b - bLen2 * a, abCross) / (2 * abCrossLen2);
                    return new BoundingSphere(center, radius);
                }
                case 4:
                {
                    ref var vA = ref supports.Ref(0);
                    ref var vB = ref supports.Ref(1);
                    ref var vC = ref supports.Ref(2);
                    ref var vD = ref supports.Ref(3);

                    var mat = new Matrix(
                        vA.X, vA.Y, vA.Z, 1,
                        vB.X, vB.Y, vB.Z, 1,
                        vC.X, vC.Y, vC.Z, 1,
                        vD.X, vD.Y, vD.Z, 1);
                    var t = mat.Determinant();
                    if (Math.Abs(t) < 1e-10f)
                        return BoundingSphere.CreateInvalid();

                    var t1 = -vA.LengthSquared();
                    var t2 = -vB.LengthSquared();
                    var t3 = -vC.LengthSquared();
                    var t4 = -vD.LengthSquared();

                    var d = DetWithColumn(in mat, 0, t1, t2, t3, t4) / t;
                    var e = DetWithColumn(in mat, 1, t1, t2, t3, t4) / t;
                    var f = DetWithColumn(in mat, 2, t1, t2, t3, t4) / t;
                    var g = DetWithColumn(in mat, 3, t1, t2, t3, t4) / t;

                    return new BoundingSphere(
                        new Vector3(-d/2,-e/2,-f/2),
                        (float) Math.Sqrt(d*d+e*e+f*f-4*g) / 2
                    );
                }
                default:
                    throw new Exception($"Invalid support count {count}");
            }
        }

        private static float DetWithColumn(in Matrix matrix, int column, float t1, float t2, float t3, float t4)
        {
            var copy = matrix;
            copy[0, column] = t1;
            copy[1, column] = t2;
            copy[2, column] = t3;
            copy[3, column] = t4;
            return copy.Determinant();
        }
    }
}