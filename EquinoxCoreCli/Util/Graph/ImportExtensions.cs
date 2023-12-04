using System.Collections.Generic;
using System.Numerics;
using Equinox76561198048419394.Core.Cli.Util.Models;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public static class ImportExtensions
    {
        public static PrimitivesToGraph AsPrimitiveVisitor(this IEdgeSink<float> sink, IList<Vector3> vertices = null) => new PrimitivesToGraph(sink, vertices);
        
        public readonly struct PrimitivesToGraph : IPrimitiveVisitor
        {
            private readonly IEdgeSink<float> _sink;
            private readonly IList<Vector3> _vertices;

            public PrimitivesToGraph(IEdgeSink<float> sink, IList<Vector3> vertices)
            {
                _sink = sink;
                _vertices = vertices;
            }

            public void Point(uint a) => throw new System.NotImplementedException();

            public void Line(uint a, uint b)
            {
                if (_vertices == null)
                {
                    _sink.AddEdge(a, b, 0);
                    return;
                }

                var av = _vertices[(int)a];
                var bv = _vertices[(int)b];
                _sink.AddEdge(a, b, Vector3.Distance(av, bv));
            }

            public void Triangle(uint a, uint b, uint c)
            {
                if (_vertices == null)
                {
                    _sink.AddEdge(a, b, 0);
                    _sink.AddEdge(b, c, 0);
                    _sink.AddEdge(a, c, 0);
                    return;
                }

                var av = _vertices[(int)a];
                var bv = _vertices[(int)b];
                var cv = _vertices[(int)c];
                _sink.AddEdge(a, b, Vector3.Distance(av, bv));
                _sink.AddEdge(b, a, Vector3.Distance(bv, cv));
                _sink.AddEdge(c, a, Vector3.Distance(cv, av));
            }
        }
    }
}