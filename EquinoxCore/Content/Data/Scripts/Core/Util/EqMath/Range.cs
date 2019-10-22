using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public struct MutableRange<T>
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

    public struct ImmutableRange<T>
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

        public MutableRange<T> Mutable()
        {
            return new MutableRange<T>(Min, Max);
        }

        public override string ToString()
        {
            return $"Range[{Min} to {Max}]";
        }
    }
}