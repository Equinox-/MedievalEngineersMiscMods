using System;
using Equinox76561198048419394.Core.Util;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace EquinoxCoreTests
{
    public class TestExtensions
    {
        [Test]
        public void Test()
        {
            ClassicAssert.True("foo".ContainsCommaSeparated("foo"));
            ClassicAssert.True("foo,asdf".ContainsCommaSeparated("foo"));
            ClassicAssert.True("asdf,foo,asdf".ContainsCommaSeparated("foo"));
            ClassicAssert.True("asdf,foo".ContainsCommaSeparated("foo"));

            ClassicAssert.True("foo".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            ClassicAssert.True("foo,asdf".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            ClassicAssert.True("asdf,foo,asdf".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));
            ClassicAssert.True("asdf,foo".ContainsCommaSeparated("FOO", StringComparison.OrdinalIgnoreCase));

            ClassicAssert.False("foo".ContainsCommaSeparated("fo"));
            ClassicAssert.False("foo,asdf".ContainsCommaSeparated("fo"));
            ClassicAssert.False("asdf,foo,asdf".ContainsCommaSeparated("fo"));
            ClassicAssert.False("asdf,foo".ContainsCommaSeparated("fo"));
            ClassicAssert.True("foo,fo".ContainsCommaSeparated("fo"));
            ClassicAssert.True("foo,fo,foo".ContainsCommaSeparated("fo"));
        }
    }
}