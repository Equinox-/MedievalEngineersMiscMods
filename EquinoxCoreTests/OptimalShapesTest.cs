using System;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.Cli.Util.Collider;
using Equinox76561198048419394.Core.Util.Memory;
using NUnit.Framework;
using VRageMath;

namespace EquinoxCoreTests
{
    public class OptimalShapesTest
    {
        [Test]
        public static void Test()
        {
            var points = new[]
            {
                new Vector3(-.244f, 0, -1.715f),
                new Vector3(.378f, -1.566f, -.637f),
                new Vector3(-5.176f, 1.33f, 3.07f),
                new Vector3(-4.554f, -.232f, 4.148f),
                new Vector3(1.031f, 1.185f, -.73f),
                new Vector3(1.653f, -.381f, .348f),
                new Vector3(-3.9f, 2.519f, 4.055f),
                new Vector3(-3.278f, .953f, 5.132f)
            };
            OptimalShapes.Of(points.AsEqSpan(), out var sphere, out var box, out var capsule);
            Console.WriteLine(sphere + "\t" + sphere.Volume());
            Console.WriteLine(box + "\t" + box.Volume());
            Console.WriteLine($"{capsule.P0} {capsule.P1} {capsule.Radius}\t{capsule.Volume()}");
        }
    }
}