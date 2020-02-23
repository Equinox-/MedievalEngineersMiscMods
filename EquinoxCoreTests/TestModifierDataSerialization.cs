using System;
using Equinox76561198048419394.Core.Modifiers.Data;
using NUnit.Framework;
using VRageMath;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestModifierDataSerialization
    {
        [Test]
        public void TestDataColor()
        {
            foreach (var color in Color.AllNamedColors)
            {
                var data = new ModifierDataColor(color.ColorToHSV());
                var compared = ModifierDataColor.Deserialize(data.Serialize());
                Assert.AreEqual(data.Color, compared.Color);
            }
        }
    }
}