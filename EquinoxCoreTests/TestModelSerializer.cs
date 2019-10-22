using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.ModelGenerator.ModelIO;
using NUnit.Framework;
using NUnit.Framework.Internal;
using SharpDX.Win32;
using VRageMath;
using VRageRender.Import;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestModelSerializer
    {
        static TestModelSerializer()
        {
            ModelImporter.USE_LINEAR_KEYFRAME_REDUCTION = false;
            ModelImporter.NORMALIZE_QUATERNIONS = false;
            ModelExporter.USE_ORDERED_DICTIONARIES = true;
        }

        private static readonly string[] ModelsToTest =
        {
            @"Models\Cubes\large\ArchStoneFullWall_V1.mwm",
            @"Models\Cubes\large\BannerWorkstation.mwm",
            @"Models\Characters\Basic\ME_male.mwm",
            @"Models\Characters\Animations\Female\Female_attack01.mwm"
        };

        [Test]
        [TestCaseSource(nameof(ModelsToTest))]
        public void Test(string testModel)
        {
            var inputRaw = File.ReadAllBytes(Path.Combine(TestUtils.ContentRoot, testModel));
            var inputTags = Deserialize(inputRaw);
            var intermediateRaw = Serialize(inputTags, null, out var intermediateRawTags);
            var intermediateTags = Deserialize(intermediateRaw);

            // Validate tags are 1:1
            AssertTagsEqual(inputTags, intermediateTags, "{root}");

            // Validate binary representation is 1:1
            Serialize(intermediateTags, intermediateRawTags, out _);
        }

        private void AssertTagsEqual(object expected, object actual, string path)
        {
            if (expected == null)
            {
                Assert.True(actual == null, "Expected value was null, got \"{0}\" at {1}", actual, path);
                return;
            }

            Assert.True(actual != null, "Actual value was null, expecting \"{0}\" at {1}", expected, path);
            Assert.AreEqual(expected.GetType(), actual.GetType(), "Different types at {0}", path);
            if (expected is IDictionary eDict && actual is IDictionary aDict)
            {
                Assert.AreEqual(eDict.Count, aDict.Count, "Different counts at {0}", path);
                foreach (var key in eDict.Keys)
                    AssertTagsEqual(eDict[key], aDict[key], $"{path}.{key}");
            }
            else if (expected.GetType().IsArray && expected.GetType().GetElementType().IsValueType)
            {
                var arrayEquals = typeof(TestModelSerializer).GetMethod("AssetVTypeArraysEqual", (BindingFlags) (-1))
                    .MakeGenericMethod(expected.GetType().GetElementType());
                arrayEquals.Invoke(this, new[] {expected, actual, path});
            }
            else if (!(expected is string) && expected is IEnumerable eEnum && actual is IEnumerable aEnum)
            {
                var eItr = eEnum.GetEnumerator();
                var aItr = aEnum.GetEnumerator();
                var idx = 0;
                while (true)
                {
                    var nextE = eItr.MoveNext();
                    var nextA = aItr.MoveNext();
                    Assert.AreEqual(nextE, nextA, "Enumerable sequences have different lengths at {0}", path);
                    if (!nextE)
                        break;
                    AssertTagsEqual(eItr.Current, aItr.Current, $"{path}[{idx}]");
                    idx++;
                }
            }
            else if (typeof(IEquatable<>).MakeGenericType(expected.GetType()).IsInstanceOfType(expected) || expected.GetType().IsPrimitive)
            {
                Assert.AreEqual(expected, actual, "Different at {0}", path);
            }
            else
            {
                var fields = expected.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    var simpleName = field.Name;
                    var match = BackingFieldRegexp.Match(simpleName);
                    if (match.Success)
                        simpleName = match.Groups[1].Value;
                    AssertTagsEqual(field.GetValue(expected), field.GetValue(actual), $"{path}.{simpleName}");
                }
            }
        }

        private void AssetVTypeArraysEqual<T>(T[] expected, T[] actual, string path)
        {
            Assert.AreEqual(expected.Length, actual.Length, "Arrays have different lengths at {0}", path);
            var comparer = EqualityComparer<T>.Default;
            for (var i = 0; i < expected.Length; i++)
                if (!comparer.Equals(expected[i], actual[i]))
                    Assert.AreEqual(expected[i], actual[i], "Different at {0}[{1}]", path, i);
        }

        private Dictionary<string, object> Deserialize(byte[] raw)
        {
            var import = new ModelImporter();
            import.ImportData(new BinaryReader(new MemoryStream(raw)));
            var tags = import.GetTagData();
            tags["Debug"] = new string[0];
            return tags;
        }

        private byte[] Serialize(Dictionary<string, object> tagData, byte[] expectedTagData, out byte[] resultTagData)
        {
            using (var memStream = new MemoryStream(1024 * 1024))
            using (var outerWriter = new BinaryWriter(memStream))
            {
                var stream = new ModelExporter.ModelExporterStream(outerWriter);
                var list = new List<string>((string[]) tagData["Debug"]);
                list.RemoveAll((string x) => x.Contains("Version:"));
                list.Add(string.Format("Version:{0}", 1157002));
                stream.ExportData("Debug", list.ToArray());

                using (var cacheStream = new MemoryStream(1024 * 1024))
                using (var cacheWriter =
                    new BinaryWriter(expectedTagData != null ? new ValidatingStream(cacheStream, expectedTagData) : (Stream) cacheStream))
                {
                    stream.WriteIndexDictionary(ModelExporter.ExportTags(cacheWriter, tagData));
                    resultTagData = cacheStream.ToArray();
                    memStream.Write(cacheStream.GetBuffer(), 0, (int) cacheStream.Length);
                }

                return memStream.ToArray();
            }
        }

        private static readonly Regex BackingFieldRegexp = new Regex("<(.*?)>k__BackingField");

        private class ValidatingStream : Stream
        {
            private readonly Stream _del;
            private readonly byte[] _validate;

            public ValidatingStream(Stream del, byte[] validate)
            {
                _del = del;
                _validate = validate;
            }

            public override void Flush()
            {
                _del.Flush();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _del.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _del.SetLength(value);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _del.Read(buffer, offset, count);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                var pos = Position;
                for (var i = 0; i < count; i++)
                    if (_validate[i + pos] != buffer[offset + i])
                        throw new IOException($"Byte {i} is {buffer[offset + i]}, expected {_validate[i + pos]}");
                _del.Write(buffer, offset, count);
            }

            public override bool CanRead => _del.CanRead;

            public override bool CanSeek => _del.CanSeek;

            public override bool CanWrite => _del.CanWrite;

            public override long Length => _del.Length;

            public override long Position
            {
                get => _del.Position;
                set => _del.Position = value;
            }
        }
    }
}