using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Util.Struct;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestPagedFreeBlockList
    {
        [Test]
        public void Test()
        {
            var list = new PagedFreeBlockList<uint>();
            const uint count = 10_000u;
            var rand = new Random(1234);

            var allocated = new List<(uint Offset, List<uint> Values)>();
            for (var op = 0u; op < count; op++)
            {
                switch (rand.Next(0, 5))
                {
                    // Add-new
                    case 0:
                    {
                        var allocationSize = (uint)rand.Next(1, 4);
                        var values = new List<uint>();
                        var offset = list.Allocate(allocationSize);
                        allocated.Add((offset, values));
                        for (var i = 0u; i < allocationSize; i++)
                        {
                            var val = (uint)rand.Next();
                            list[offset + i] = val;
                            values.Add(val);
                        }

                        break;
                    }
                    // Add-partial
                    case 1:
                    {
                        if (allocated.Count == 0) break;
                        var modify = ItemToModify();
                        var allocation = allocated[modify];
                        var allocationCount = (uint)allocation.Values.Count;
                        var expand = (uint)rand.Next(1, 4);
                        allocation.Offset = list.Reallocate(allocation.Offset, allocationCount, allocationCount + expand);
                        allocated[modify] = allocation;
                        // Add new values
                        for (var i = 0u; i < expand; i++)
                        {
                            var val = (uint)rand.Next();
                            list[allocation.Offset + allocationCount + i] = val;
                            allocation.Values.Add(val);
                        }

                        break;
                    }
                    // Remove-partial
                    case 2:
                    {
                        if (allocated.Count == 0) break;
                        var modify = ItemToModify();
                        var allocation = allocated[modify];
                        var shrink = (uint)rand.Next(1, 4);
                        if (allocation.Values.Count <= shrink) break;
                        var allocationCount = (uint)allocation.Values.Count;
                        allocation.Offset = list.Reallocate(allocation.Offset, allocationCount, allocationCount - shrink);
                        allocation.Values.RemoveRange((int)(allocationCount - shrink), (int)shrink);
                        allocated[modify] = allocation;
                        break;
                    }
                    // Remove-full
                    case 3:
                    {
                        if (allocated.Count == 0) break;
                        var modify = ItemToModify();
                        var allocation = allocated[modify];
                        allocated.RemoveAt(modify);
                        list.Free(allocation.Offset, (uint)allocation.Values.Count);
                        break;
                    }
                    // Compact
                    case 4:
                    {
                        using (var report = list.Compact())
                        {
                            for (var i = 0; i < allocated.Count; i++)
                            {
                                var allocation = allocated[i];
                                report.UpdateIndex(ref allocation.Offset);
                                allocated[i] = allocation;
                            }
                        }

                        break;
                    }
                }

                // Verify integrity
                foreach (var allocation in allocated)
                {
                    int i;
                    for (i = 0; i < allocation.Values.Count; i++)
                        Assert.That(list[(uint)(allocation.Offset + i)], Is.EqualTo(allocation.Values[i]));
                    i = 0;
                    foreach (var span in list.Range(allocation.Offset, (uint) allocation.Values.Count))
                        foreach (var item in span)
                            Assert.That(item, Is.EqualTo(allocation.Values[i++]));
                }

                var expected = allocated.OrderBy(x => x.Offset).SelectMany(x => x.Values).ToList();
                var expectedI = 0;
                foreach (var range in list.AllocatedItems)
                    foreach (var item in range)
                        Assert.That(expected[expectedI++], Is.EqualTo(item));
            }

            return;

            int ItemToModify()
            {
                if (rand.Next(0, 500) != 0)
                    return rand.Next(0, allocated.Count);
                // Small chance to pick the tail of the allocator, since that is special cased.
                var maxI = 0;
                for (var i = 0; i < allocated.Count; i++)
                    if (allocated[i].Offset > allocated[maxI].Offset)
                        maxI = i;
                return maxI;
            }
        }
    }
}