using Equinox76561198048419394.Core.Util;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestBoundedVec30
    {
        private static readonly PackedBoundedVec Packing = new PackedBoundedVec(-0.5f, 1.5f, 10);

        private static readonly uint[] Values =
        {
            0,
            0x00FF00,
            0xFF00FF,
            0x3FFFFFFF,
        };

        [Test]
        [TestCaseSource(nameof(Values))]
        public void Test(uint packed)
        {
            var unpacked = Packing.Unpack(packed);
            var repacked = Packing.Pack(unpacked);
            Assert.AreEqual(packed, repacked, $"Repacking {packed} produces {repacked}, vector is {unpacked}");
        }
    }
}