using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.ModelGenerator.ModelIO;
using Equinox76561198048419394.Core.Util.EqMath;
using NUnit.Framework;
using VRage.Library.Utils;
using VRage.Utils;
using VRageMath;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestMaterialBvh
    {
        [Test]
        public void Test()
        {
            var model = TestUtils.ReadModel(@"Models\Cubes\large\ArchStoneFullWall_V1.mwm");
            var bvh = MaterialBvh.Create(model);
            Verify(bvh, 0);
        }

        private static Vector3 GetRandomSurface()
        {
            var rv = MyUtils.GetRandomVector3();
            switch (MyUtils.GetRandomInt(3))
            {
                case 0:
                    rv.X = (float) Math.Round(rv.X);
                    break;
                case 1:
                    rv.Y = (float) Math.Round(rv.Y);
                    break;
                case 2:
                    rv.Z = (float) Math.Round(rv.Z);
                    break;
            }

            return rv;
        }

        private void Verify(MaterialBvh mtlBvh, int nodeId)
        {
            ref readonly var node = ref mtlBvh._bvh.GetNode(nodeId);
            if (node.IsLeaf)
            {
                foreach (var proxy in mtlBvh._bvh.GetProxies(nodeId))
                {
                    ref readonly var tri = ref mtlBvh.GetTriangle(proxy);
                    Assert.AreNotEqual(ContainmentType.Disjoint, node.Box.Contains(tri.A));
                    Assert.AreNotEqual(ContainmentType.Disjoint, node.Box.Contains(tri.B));
                    Assert.AreNotEqual(ContainmentType.Disjoint, node.Box.Contains(tri.C));
                }
            }
            else
            {
                ref readonly var lhs = ref mtlBvh._bvh.GetNode(node.Lhs);
                ref readonly var rhs = ref mtlBvh._bvh.GetNode(node.Rhs);
                Assert.AreNotEqual(ContainmentType.Disjoint, node.Box.Contains(lhs.Box));
                Assert.AreNotEqual(ContainmentType.Disjoint, node.Box.Contains(rhs.Box));
                Verify(mtlBvh, node.Lhs);
                Verify(mtlBvh, node.Rhs);
            }
        }

        private void Dump(PackedBvh bvh, int nodeId, string indent)
        {
            ref readonly var node = ref bvh.GetNode(nodeId);
            Console.WriteLine($"{indent} {node.Box} {(node.IsLeaf ? node.Count : -1)} SA: {node.Box.SurfaceArea()}");
            if (!node.IsLeaf)
            {
                var ni = indent + " |";
                Dump(bvh, node.Lhs, ni);
                Dump(bvh, node.Rhs, ni);
            }
        }
    }
}