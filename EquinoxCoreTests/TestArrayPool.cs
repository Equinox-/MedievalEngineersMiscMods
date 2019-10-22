using Equinox76561198048419394.Core.Util;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestArrayPool
    {
        [Datapoints]
        public readonly int[] Points = {1, 2, 4, 6, 18, 509, 103, 193, 555, 103, 55, 4095, 4097, 3127, 18939, 55555, 2048};

        [Theory]
        public void TestRandomAccess(int size)
        {
            using (ArrayPool<int>.Get(size, out var array))
            {
                Assert.GreaterOrEqual(array.Length, size);
            }
        }
    }
}