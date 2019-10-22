using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Equinox76561198048419394.Core.Util;
using NUnit.Framework;
using VRage.Game;
using VRage.ObjectBuilders;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestIdBag
    {
        private static readonly Random rand = new Random();

        private static readonly MyDefinitionId[] _ids = Enumerable.Range(0, 32)
            .Select(x => new MyDefinitionId(typeof(MyObjectBuilder_Base), rand.NextDouble().ToString())).ToArray();

        [Test]
        public void Test()
        {
            var a = InterningBag<MyDefinitionId>.Empty.With(_ids[0]).With(_ids[1]);
            var b = InterningBag<MyDefinitionId>.Empty.With(_ids[1]).With(_ids[0]);
            var c = InterningBag<MyDefinitionId>.Of(_ids[0], _ids[1]);
            Assert.True(ReferenceEquals(InterningBag<MyDefinitionId>.Of(), InterningBag<MyDefinitionId>.Empty));
            Assert.True(ReferenceEquals(a, b));
            Assert.True(ReferenceEquals(a, c));
            Assert.True(ReferenceEquals(a.Without(_ids[0]), b.Without(_ids[0])));
            Assert.True(ReferenceEquals(a.Without(_ids[1]), b.Without(_ids[1])));
            Assert.True(ReferenceEquals(a.Without(_ids[0]), c.Without(_ids[0])));
            Assert.True(ReferenceEquals(a.Without(_ids[1]), c.Without(_ids[1])));
            
            Assert.True(a.Contains(_ids[0]));
            Assert.False(a.Contains(_ids[2]));
            
            Assert.False(a.Without(_ids[0]).Contains(_ids[0]));
            
            Assert.True(ReferenceEquals(a, a.With(_ids[0])));
            Assert.True(ReferenceEquals(a, a.Without(_ids[2])));
            Assert.True(ReferenceEquals(a.Without(_ids[0]).Without(_ids[1]), InterningBag<MyDefinitionId>.Empty));
            Assert.True(ReferenceEquals(a.Without(_ids[0]).Without(_ids[2]), InterningBag<MyDefinitionId>.Of(_ids[1])));

            var copied = InterningBag<MyDefinitionId>.Of(a.ToArray());
            Assert.True(ReferenceEquals(copied, a));
        }
    }
}