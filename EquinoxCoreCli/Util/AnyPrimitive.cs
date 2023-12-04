using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Equinox76561198048419394.Core.Cli.Util
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct AnyPrimitive : IEquatable<AnyPrimitive>
    {
        [FieldOffset(0)]
        public readonly int Int;

        [FieldOffset(0)]
        public readonly long Long;

        [FieldOffset(0)]
        public readonly float Float;

        [FieldOffset(0)]
        public readonly double Double;

        [FieldOffset(8)]
        public readonly PrimitiveType Type;


        public AnyPrimitive(int value)
        {
            Int = default;
            Long = default;
            Float = default;
            Double = default;

            Type = PrimitiveType.Int;
            Int = value;
        }


        public AnyPrimitive(long value)
        {
            Int = default;
            Long = default;
            Float = default;
            Double = default;

            Type = PrimitiveType.Long;
            Long = value;
        }

        public AnyPrimitive(float value)
        {
            Int = default;
            Long = default;
            Float = default;
            Double = default;

            Type = PrimitiveType.Float;
            Float = value;
        }

        public AnyPrimitive(double value)
        {
            Int = default;
            Long = default;
            Float = default;
            Double = default;

            Type = PrimitiveType.Double;
            Double = value;
        }

        public static implicit operator AnyPrimitive(int value) => new AnyPrimitive(value);
        public static implicit operator AnyPrimitive(long value) => new AnyPrimitive(value);
        public static implicit operator AnyPrimitive(float value) => new AnyPrimitive(value);
        public static implicit operator AnyPrimitive(double value) => new AnyPrimitive(value);

        public bool Equals(AnyPrimitive other) => Type == other.Type && Long == other.Long;

        public override bool Equals(object obj) => obj is AnyPrimitive other && Equals(other);

        public override int GetHashCode() => ((int)Type * 397) ^ Long.GetHashCode();

        public override string ToString()
        {
            switch (Type)
            {
                case PrimitiveType.Int:
                    return Int.ToString();
                case PrimitiveType.Long:
                    return Long.ToString();
                case PrimitiveType.Float:
                    return Float.ToString(CultureInfo.InvariantCulture);
                case PrimitiveType.Double:
                    return Double.ToString(CultureInfo.InvariantCulture);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public enum PrimitiveType : byte
        {
            Int,
            Long,
            Float,
            Double
        }
    }
}