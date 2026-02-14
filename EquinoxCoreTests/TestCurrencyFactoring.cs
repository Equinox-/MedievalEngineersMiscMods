using System.Collections.Generic;
using Equinox76561198048419394.Core.Misc;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    public class TestCurrencyFactoring
    {
        [Test]
        public void Test()
        {
            var info = new TestCurrencyInfo();
            info.Add("7p", 7);
            info.Add("5p", 5);

            var counts = new ulong[info.Count];
            var taken = EquiCurrencySystemDefinition.CurrencyToItemAmounts(ref info, 8, true, counts);
            Assert.AreEqual(12L, taken);
            Assert.AreEqual(new ulong[] { 1, 1 }, counts);
        }

        private sealed class TestCurrencyInfo : EquiCurrencySystemDefinition.ICurrencyInfo
        {
            private readonly List<(string name, ulong value, ulong limit)> _list = new List<(string name, ulong value, ulong limit)>();

            public void Add(string name, ulong value, ulong limit = ulong.MaxValue) => _list.Add((name, value, limit));

            public int Count => _list.Count;

            public ulong Value(int ix) => _list[ix].value;

            public ulong Limit(int ix) => _list[ix].limit;
        }
    }
}