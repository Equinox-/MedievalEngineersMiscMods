using System;
using System.Collections.Generic;
using System.IO;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Util.EqMath;
using NUnit.Framework;
using VRageMath;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class Tests
    {
        [Test]
        public void TestCli()
        {
            var model = TestUtils.ReadModel(@"Models\Cubes\small\Timber10.mwm");
            MaterialBvh.Serializer.Read(
                new BinaryReader(
                    File.Open(@"C:\Users\westin\AppData\Roaming\MedievalEngineers\Storage\modelBvh_Timber10_37A680A69CADF26A0C5F9A044D9446D3.ebvh",
                        FileMode.Open)),
                out var res);
            Console.WriteLine();
        }
    }
}