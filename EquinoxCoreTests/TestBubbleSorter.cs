using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestBubbleSorter
    {
        public struct IntTester : BubbleSort.IBubbleSorter<int>
        {
            public bool ShouldSwap(in int a, in int b) => a > b;
        }

        private void RunTest(int a, int b, int c, int d)
        {
            var expected = new List<int> { a, b, c, d, };
            expected.Sort();
            BubbleSort.Sort(ref a, ref b, ref c, ref d, default(IntTester));
            Assert.AreEqual(new List<int> { a, b, c, d, }, expected);
        }

        [Test]
        public void Test()
        {
            // Already ordered
            RunTest(0, 1, 2, 3);
            // Reverse ordered
            RunTest(3, 2, 1, 0);
        }
    }
}