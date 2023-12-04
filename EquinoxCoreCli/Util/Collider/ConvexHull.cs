using System;
using System.Collections.Generic;
using System.Linq;
using Havok;
using MIConvexHull;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Collider
{
    public sealed class ConvexHull
    {
        public readonly Vector3[] Points;
        public readonly float Volume;

        private ConvexHull(Vector3[] points, float volume)
        {
            Points = points;
            Volume = volume;
        }

        public BoundingBox Box => BoundingBox.CreateFromPoints(Points);

        public BoundingSphere Sphere
        {
            get
            {
                var center = Box.Center;
                var maxRad2 = 0f;
                foreach (var pt in Points)
                {
                    var rad2 = Vector3.DistanceSquared(center, pt);
                    if (rad2 > maxRad2)
                        maxRad2 = rad2;
                }

                return new BoundingSphere(center, (float)Math.Sqrt(maxRad2));
            }
        }

        public HkConvexVerticesShape ToHavok(float convexRadius) => new HkConvexVerticesShape(Points, Points.Length, true, convexRadius);

        public static ConvexHull CreateFromPoints(IEnumerable<Vector3> points)
        {
            var hull = MIConvexHull.ConvexHull.Create(points
                .Select(pt => new DefaultVertex { Position = new double[] { pt.X, pt.Y, pt.Z } })
                .ToList()).Result;

            var volume = 0f;
            foreach (var face in hull.Faces)
            {
                var first = VRage(face.Vertices[0]);
                var prev = VRage(face.Vertices[1]);
                for (var i = 2; i < face.Vertices.Length; i++)
                {
                    var curr = VRage(face.Vertices[i]);

                    volume += Math.Abs(first.Dot(prev.Cross(curr))) / 6;

                    prev = curr;
                }
            }

            return new ConvexHull(hull.Points.Select(VRage).ToArray(), volume);
        }

        public static ConvexHull CreateFromSpheres(IEnumerable<BoundingSphere> spheres) => CreateFromPoints(spheres
            .SelectMany(sphere => Icosahedron.Select(pt => sphere.Center + sphere.Radius * pt)));

        private static readonly float Aspect = (1 + (float)Math.Sqrt(5)) / 2;
        private static readonly float Size1 = 1 / (float)Math.Sqrt(1 + Aspect * Aspect);
        private static readonly float Size2 = Aspect * Size1;

        private static readonly Vector3[] Icosahedron =
        {
            new Vector3(Size2, Size1, 0),
            new Vector3(Size2, -Size1, 0),
            new Vector3(-Size2, -Size1, 0),
            new Vector3(-Size2, Size1, 0),
            new Vector3(Size1, 0, Size2),
            new Vector3(-Size1, 0, Size2),
            new Vector3(-Size1, 0, -Size2),
            new Vector3(Size1, 0, -Size2),
            new Vector3(0, Size2, Size1),
            new Vector3(0, Size2, -Size1),
            new Vector3(0, -Size2, -Size1),
            new Vector3(0, -Size2, Size1),
        };

        private static Vector3 VRage(DefaultVertex vert)
        {
            var pos = vert.Position;
            return new Vector3(pos[0], pos[1], pos[2]);
        }
    }
}