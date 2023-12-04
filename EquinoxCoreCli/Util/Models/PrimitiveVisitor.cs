using System;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace Equinox76561198048419394.Core.Cli.Util.Models
{
    public interface IPrimitiveVisitor
    {
        void Point(uint a);

        void Line(uint a, uint b);

        void Triangle(uint a, uint b, uint c);
    }

    public static class PrimitiveVisitor
    {
        public static void Visit<T>(this MeshPrimitive primitive, T visitor) where T : IPrimitiveVisitor
        {
            Visit(primitive.DrawPrimitiveType, primitive.IndexAccessor.AsIndicesArray(), visitor);
        }

        public static void Visit<T>(PrimitiveType type, IntegerArray indices, T visitor) where T : IPrimitiveVisitor
        {
            switch (type)
            {
                case PrimitiveType.POINTS:
                {
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (var i = 0; i < indices.Count; i++)
                        visitor.Point(indices[i]);
                    break;
                }
                case PrimitiveType.LINES:
                {
                    for (var i = 0; i < indices.Count - 1; i += 2)
                        visitor.Line(indices[i], indices[i + 1]);
                    break;
                }
                case PrimitiveType.LINE_LOOP:
                {
                    if (indices.Count < 2)
                        return;
                    var prev = indices[indices.Count - 1];
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (var i = 0; i < indices.Count; i++)
                    {
                        var curr = indices[i];
                        visitor.Line(prev, curr);
                        prev = curr;
                    }

                    break;
                }
                case PrimitiveType.LINE_STRIP:
                {
                    if (indices.Count < 2)
                        return;
                    var prev = indices[0];
                    for (var i = 1; i < indices.Count; i++)
                    {
                        var curr = indices[i];
                        visitor.Line(prev, curr);
                        prev = curr;
                    }

                    break;
                }
                case PrimitiveType.TRIANGLES:
                {
                    for (var i = 0; i < indices.Count - 2; i += 3)
                        visitor.Triangle(indices[i], indices[i + 1], indices[i + 2]);
                    break;
                }
                case PrimitiveType.TRIANGLE_STRIP:
                {
                    if (indices.Count < 3)
                        return;
                    var prevPrev = indices[0];
                    var prev = indices[1];
                    for (var i = 2; i < indices.Count; i++)
                    {
                        var curr = indices[i];
                        if ((i & 1) == 0)
                            visitor.Triangle(prevPrev, prev, curr);
                        else
                            visitor.Triangle(prevPrev, curr, prev);
                        prevPrev = prev;
                        prev = curr;
                    }

                    break;
                }
                case PrimitiveType.TRIANGLE_FAN:
                {
                    if (indices.Count < 3)
                        return;
                    var zero = indices[0];
                    var prev = indices[1];
                    for (var i = 2; i < indices.Count; i++)
                    {
                        var curr = indices[i];
                        visitor.Triangle(prev, curr, zero);
                        prev = curr;
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}