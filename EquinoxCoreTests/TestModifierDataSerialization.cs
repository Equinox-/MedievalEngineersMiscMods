using System;
using System.Diagnostics;
using Equinox76561198048419394.Core.Modifiers.Data;
using NUnit.Framework;
using ObjectBuilders.Definitions.GUI;
using VRage.Library.Utils;
using VRageMath;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestModifierDataSerialization
    {
        [Test]
        public void TestDataColor()
        {
            for (var seed = 0; seed< 10_000; seed++)
            {
                var rand = new Random(seed);
                var data = new ModifierDataColor(new ColorDefinitionHSV
                {
                    H = rand.Next(360),
                    S = rand.Next(-100, 101),
                    V = rand.Next(-100, 101)
                });
                var compared = ModifierDataColor.Deserialize(data.Serialize());
                Assert.AreEqual(data.Color, compared.Color);
            }
        }
    }
}