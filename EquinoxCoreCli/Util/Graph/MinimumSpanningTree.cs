using System.Collections.Generic;
using VRage.Algorithms;
using VRage.Collections;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public sealed class MstCalculator : IEdgeSink<float>
    {
        private readonly HashSet<PackedEdge> _edgeDeduplication;
        private readonly Dictionary<uint, int> _ufCoding;
        private readonly MyBinaryHeap<float, PackedEdge> _edgeHeap;
        private readonly MyUnionFind _uf;

        public MstCalculator(bool deduplicateEdges = false)
        {
            _edgeDeduplication = deduplicateEdges ? new HashSet<PackedEdge>() : null;
            _ufCoding = new Dictionary<uint, int>();
            _uf = new MyUnionFind();
            _edgeHeap = new MyBinaryHeap<float, PackedEdge>();
        }

        public void AddEdge(uint a, uint b, float data)
        {
            PackedEdge.Primary(a, b, out var primary);
            if (_edgeDeduplication != null && !_edgeDeduplication.Add(primary))
                return;
            _edgeHeap.Insert(primary, data);
            if (!_ufCoding.ContainsKey(primary.First))
                _ufCoding.Add(primary.First, _ufCoding.Count);
            if (!_ufCoding.ContainsKey(primary.Second))
                _ufCoding.Add(primary.Second, _ufCoding.Count);
        }

        public Graph<float> ComputeAndReset() => ComputeAndReset<Graph<float>>();

        public T ComputeAndReset<T>(T target = null) where T : class, IGraph<float>, new()
        {
            if (target == null)
                target = new T();
            else
                target.Clear();

            _uf.Resize(_ufCoding.Count);
            while (_edgeHeap.Count > 0)
            {
                var minWeight = _edgeHeap.MinKey();
                var min = _edgeHeap.RemoveMin();

                var n1Tree = _uf.Find(_ufCoding[min.First]);
                var n2Tree = _uf.Find(_ufCoding[min.Second]);
                if (n1Tree != n2Tree)
                {
                    _uf.Union(n1Tree, n2Tree);
                    target.AddEdge(min.First, min.Second, minWeight);
                }
            }

            _ufCoding.Clear();
            _uf.Clear();
            _edgeHeap.Clear();
            _edgeDeduplication?.Clear();

            return target;
        }
    }

    public static class MstExtensions
    {
        public static T MinimumSpanningTree<T>(this Graph<float> graph, T target = null) where T : class, IGraph<float>, new()
        {
            var calculator = new MstCalculator();
            foreach (var edge in graph.Edges)
                calculator.AddEdge(edge);
            return calculator.ComputeAndReset(target);
        }
    }
}