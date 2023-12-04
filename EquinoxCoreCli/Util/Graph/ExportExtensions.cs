using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Cli.Util.Writers;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public static class ExportExtensions
    {
        public static void CopyToWithData<TEdgeData>(this IEnumerableGraph<TEdgeData> source, IGraph<TEdgeData> target)
        {
            foreach (var edge in source.Edges)
                target.AddEdge(edge);
        }

        public static void CopyToWithoutData<TFromEdgeData, TToEdgeData>(
            this IEnumerableGraph<TFromEdgeData> source,
            IGraph<TToEdgeData> target,
            Func<TFromEdgeData, TToEdgeData> converter = null)
        {
            foreach (var edge in source.Edges)
                target.AddEdge(edge.Node1, edge.Node2, converter != null ? converter(edge.Data) : default);
        }

        public static void WriteGraph<TEdgeData>(this WavefrontObjWriter writer, IEnumerableGraph<TEdgeData> graph, Func<uint, Vector3> location)
            where TEdgeData : struct
        {
            var writtenNodes = new Dictionary<uint, int>();

            foreach (var edge in graph.Edges)
                writer.WriteLine(WrittenNode(edge.Node1), WrittenNode(edge.Node2), $"W = {edge.Data}");
            return;

            int WrittenNode(uint key)
            {
                if (writtenNodes.TryGetValue(key, out var id))
                    return id;
                var pt = location(key);
                id = writer.WriteVertex(pt);
                writtenNodes.Add(key, id);
                return id;
            }
        }

        public static void WriteWeightedGraph<T>(
            this GraphVizWriter writer,
            IEnumerableGraph<T> graph,
            string weightFormat = null,
            Func<Edge<T>, Color?> edgeColor = null) where T : struct, IFormattable
        {
            var writtenNodes = new Dictionary<uint, int>();
            foreach (var edge in graph.Edges)
                writer.WriteEdge(WrittenNode(edge.Node1), WrittenNode(edge.Node2),
                    weightFormat != null ? edge.Data.ToString(weightFormat, null) : "",
                    edgeColor?.Invoke(edge));
            return;

            int WrittenNode(uint key)
            {
                if (writtenNodes.TryGetValue(key, out var id))
                    return id;
                writtenNodes.Add(key, id = writer.WriteVertex());
                return id;
            }
        }
    }
}