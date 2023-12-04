using System;
using SharpGLTF.Schema2;

namespace Equinox76561198048419394.Core.Cli.Util.Models
{
    public enum PrimitiveRootType
    {
        Point,
        Line,
        Triangle
    }

    public static class PrimitiveUtils
    {
        public static PrimitiveRootType RootType(this PrimitiveType type)
        {
            switch (type)
            {
                case PrimitiveType.POINTS:
                    return PrimitiveRootType.Point;
                case PrimitiveType.LINES:
                case PrimitiveType.LINE_LOOP:
                case PrimitiveType.LINE_STRIP:
                    return PrimitiveRootType.Line;
                case PrimitiveType.TRIANGLES:
                case PrimitiveType.TRIANGLE_STRIP:
                case PrimitiveType.TRIANGLE_FAN:
                    return PrimitiveRootType.Triangle;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}