using System;
using System.Xml.Serialization;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public struct MutableRange<T> where T : IComparable<T>
    {
        [XmlAttribute]
        public T Min;

        [XmlAttribute]
        public T Max;

        public MutableRange(T min, T max)
        {
            Min = min;
            Max = max;
        }

        public ImmutableRange<T> Immutable()
        {
            return new ImmutableRange<T>(this);
        }

        public override string ToString()
        {
            return $"Range[{Min} to {Max}]";
        }
    }

    public readonly struct ImmutableRange<T> where T : IComparable<T>
    {
        public readonly T Min;
        public readonly T Max;

        public ImmutableRange(T min, T max)
        {
            Min = min;
            Max = max;
        }

        public ImmutableRange(MutableRange<T> r)
        {
            Min = r.Min;
            Max = r.Max;
        }

        public T Clamp(T value)
        {
            if (value.CompareTo(Min) < 0)
                return Min;
            if (value.CompareTo(Max) > 0)
                return Max;
            return value;
        }

        public MutableRange<T> Mutable()
        {
            return new MutableRange<T>(Min, Max);
        }

        public override string ToString()
        {
            return $"Range[{Min} to {Max}]";
        }
    }

    public static class RangeExt
    {
        public static float ToRatio(this ImmutableRange<float> range, float value, bool clamped = false)
        {
            var width = range.Max - range.Min;
            if (Math.Abs(width) < 1e-6f)
                return 0;
            var ratio = (value - range.Min) / width;
            if (clamped)
                ratio = MathHelper.Clamp(ratio, 0, 1);
            return ratio;
        }

        public static float FromRatio(this ImmutableRange<float> range, float ratio, bool clamped = false)
        {
            if (clamped)
                ratio = MathHelper.Clamp(ratio, 0, 1);
            return range.Min + ratio * (range.Max - range.Min);
        }

        public static bool IsZeroWidth(this in ImmutableRange<float> range) => Math.Abs(range.Max - range.Min) <= 1e-30f;
    }
}