using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Collider
{
    public static partial class OptimalShapes
    {
        public static void Of(EqReadOnlySpan<Vector3> points,
            out BoundingSphere sphere,
            out OrientedBoundingBox box,
            out Capsule capsule)
        {
            sphere = Sphere(points);
            box = OrientedBox(points);
            capsule = Capsule(points, in box);
        }

        private static Capsule Capsule(EqReadOnlySpan<Vector3> points, in OrientedBoundingBox box)
        {
            var axis = Vector3.Transform(Vector3.DominantAxisProjection(box.HalfExtent), box.Orientation);
            axis.Normalize();
            // Reject all components along the capsule's axis, then compute a bounding sphere.
            // This gives the radius and a point the capsule's axis passes through.
            var queue = new MyDeque<Vector3>(points.Length);
            foreach (var pt in points)
                queue.EnqueueBack(pt - axis * pt.Dot(axis));
            var sphere = Sphere(queue);
            // Compute the min/max extents along the axis.
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            foreach (var pt in points)
            {
                var shifted = pt - sphere.Center;
                var along = shifted.Dot(axis);
                var projected = along * axis;
                var rejected = shifted - projected;
                // The closer to the axis of the capsule the more "extra" length is at the ends of the capsule.
                var inflated = (float)Math.Sqrt(sphere.Radius * sphere.Radius - rejected.LengthSquared());
                var a1 = along + inflated;
                if (a1 < min)
                    min = a1;
                var a2 = along - inflated;
                if (a2 > max)
                    max = a2;
            }

            return new Capsule(sphere.Center + axis * min, sphere.Center + axis * max, sphere.Radius);
        }
    }
}