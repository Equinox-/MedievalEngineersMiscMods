using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.Struct;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestPagedList
    {
        [Test]
        public void Test()
        {
            var list = new PagedFreeList<uint>();
            const uint count = 10_000u;

            var allocated = new List<uint>();
            for (var i = 0u; i < count; i++)
            {
                var index = list.AllocateIndex();
                allocated.Add(index);
                list[index] = index;
            }

            var rand = new Random(1234);
            for (var i = 0u; i < count / 3; i++)
            {
                var index = rand.Next(allocated.Count);
                list.Free(allocated[index]);
                allocated.RemoveAtFast(index);
            }

            var existing = new List<uint>();
            foreach (var handle in list)
                existing.Add(handle.Value);

            allocated.Sort();
            existing.Sort();

            Assert.That(existing, Is.EquivalentTo(allocated));
        }
    }
}