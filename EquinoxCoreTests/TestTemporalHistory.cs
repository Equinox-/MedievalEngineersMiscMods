using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Equinox76561198048419394.Core.Util;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class TestTemporalHistory
    {
        public struct FloatHistogramBucket : ITemporalHistoryBucket<FloatHistogramBucket>
        {
            public float Value;
            public bool IsEmpty => Value == 0;
            public void MergeWith(in FloatHistogramBucket other) => Value += other.Value;

            public void MergeWith(in FloatHistogramBucket other, float fraction) => Value += other.Value * fraction;

            public bool DeserializeFrom(string part) => float.TryParse(part, out Value);

            public void SerializeTo(StringBuilder sb) => sb.Append(Value);

            public static implicit operator FloatHistogramBucket(float value) => new FloatHistogramBucket { Value = value };
            public static implicit operator float(FloatHistogramBucket value) => value.Value;

            public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        }

        [Test]
        public void Test()
        {
            var now = new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            var bucketWidth = TimeSpan.FromHours(6);
            var twiceBucketWidth = bucketWidth + bucketWidth;
            var fourBucketWidth = twiceBucketWidth + twiceBucketWidth;
            TemporalHistory<FloatHistogramBucket>.OverrideNow = now;
            var histogram = new TemporalHistory<FloatHistogramBucket>(4, bucketWidth);
            histogram.AddInstant(now, 5);
            histogram.AddInstant(now - twiceBucketWidth, 8);
            histogram.AddRange(now - fourBucketWidth, twiceBucketWidth + bucketWidth, 3);
            Console.WriteLine(histogram);
            var buckets = histogram.ToList();
            Assert.AreEqual(4, buckets.Count);
            Assert.AreEqual(1, buckets[0].Value.Value);
            Assert.AreEqual(9, buckets[1].Value.Value);
            Assert.AreEqual(0, buckets[2].Value.Value);
            Assert.AreEqual(5, buckets[3].Value.Value);
        }
    }
}