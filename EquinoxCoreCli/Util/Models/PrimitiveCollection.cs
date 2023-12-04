using System;
using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Cli.Util.Models
{
    public sealed class PrimitiveCollection : IPrimitiveVisitor
    {
        public readonly List<uint> Indices;
        public readonly PrimitiveRootType Type;

        public PrimitiveCollection(PrimitiveRootType type)
        {
            Indices = new List<uint>();
            Type = type;
        }

        public void Point(uint a)
        {
            System.Diagnostics.Debug.Assert(Type == PrimitiveRootType.Point);
            Indices.Add(a);
        }

        public void Line(uint a, uint b)
        {
            System.Diagnostics.Debug.Assert(Type == PrimitiveRootType.Line);
            Indices.Add(a);
            Indices.Add(b);
        }

        public void Triangle(uint a, uint b, uint c)
        {
            System.Diagnostics.Debug.Assert(Type == PrimitiveRootType.Triangle);
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        public void Visit<T>(T visitor, int? limit = 0) where T : IPrimitiveVisitor
        {
            var count = limit ?? Indices.Count;
            switch (Type)
            {
                case PrimitiveRootType.Point:
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (var i = 0; i < count; i++)
                        visitor.Point(Indices[i]);
                    break;
                case PrimitiveRootType.Line:
                    for (var i = 0; i < count; i += 2)
                        visitor.Line(Indices[i], Indices[i + 1]);
                    break;
                case PrimitiveRootType.Triangle:
                    for (var i = 0; i < count; i += 3)
                        visitor.Triangle(Indices[i], Indices[i + 1], Indices[i + 2]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}