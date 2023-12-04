using System.Collections.Generic;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public interface IEdgeSink<in TEdgeData>
    {
        void AddEdge(uint a, uint b, TEdgeData data);
    }

    public interface IGraph<in TEdgeData> : IEdgeSink<TEdgeData>
    {
        void RemoveEdge(uint a, uint b);

        void Clear();
    }

    public interface IEnumerableGraph<TEdgeData>
    {
        IEnumerable<Neighbor<TEdgeData>> Neighbors(uint v);

        IEnumerable<Edge<TEdgeData>> Edges { get; }

        IEnumerable<uint> Nodes { get; }
    }

    public static class GraphExt
    {
        public static void AddEdge<TEdgeData>(this IEdgeSink<TEdgeData> sink, Edge<TEdgeData> edge) => sink.AddEdge(edge.Node1, edge.Node2, edge.Data);

        public static float TotalWeight(this Graph<float> graph)
        {
            var weight = 0f;
            foreach (var edge in graph.Edges)
                weight += edge.Data;
            return weight;
        }
    }

    public readonly struct Neighbor<TEdgeData>
    {
        public readonly uint Node;
        public readonly TEdgeData Data;

        public Neighbor(uint node, TEdgeData data)
        {
            Node = node;
            Data = data;
        }

        public override string ToString() => $"-> {Node} @ {Data}";
    }

    public readonly struct Edge<TEdgeData>
    {
        public readonly uint Node1;
        public readonly uint Node2;
        public readonly TEdgeData Data;

        public Edge(uint node1, uint node2, TEdgeData data)
        {
            Node1 = node1;
            Node2 = node2;
            Data = data;
        }

        public PackedEdge Packed
        {
            get
            {
                PackedEdge.Primary(Node1, Node2, out var result);
                return result;
            }
        }

        public override string ToString() => $"{Node1} -> {Node2} @ {Data}";
    }
}