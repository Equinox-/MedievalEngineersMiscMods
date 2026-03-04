using System;
using Equinox76561198048419394.Core.Util;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    public class TestExtensions
    {
        [Test]
        public void Test()
        {
            Assert.True("foo".ContainsCommaSeparated("foo"));
            Assert.True("foo,asdf".ContainsCommaSeparated("foo"));
            Assert.True("asdf,foo,asdf".ContainsCommaSeparated("foo"));
            Assert.True("asdf,foo".ContainsCommaSeparated("foo"));

            Assert.True("foo".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            Assert.True("foo,asdf".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            Assert.True("asdf,foo,asdf".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            Assert.True("asdf,foo".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));

            Assert.False("foo".ContainsCommaSeparated("fo"));
            Assert.False("foo,asdf".ContainsCommaSeparated("fo"));
            Assert.False("asdf,foo,asdf".ContainsCommaSeparated("fo"));
            Assert.False("asdf,foo".ContainsCommaSeparated("fo"));
            Assert.True("foo,fo".ContainsCommaSeparated("fo"));
            Assert.True("foo,fo,foo".ContainsCommaSeparated("fo"));
        }
    }
}