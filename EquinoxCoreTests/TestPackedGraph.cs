using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Equinox76561198048419394.Core.Util.Graph;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    public class TestPackedGraph
    {
        [Test]
        public void Test()
        {
            var graph = new PackedGraph();
            var n0 = graph.AddNode();
            var n1 = graph.AddNode();

            Assert.That(Edges(n0), Is.Empty);
            Assert.That(Edges(n1), Is.Empty);
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(0));

            var e0 = graph.AddEdge(n0, n1);
            Assert.That(Edges(n0), Is.EquivalentTo(new[] { (e0, n1) }));
            Assert.That(Edges(n1), Is.EquivalentTo(new[] { (e0, n0) }));
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(1));

            var e1 = graph.AddEdge(n0, n1);
            Assert.That(Edges(n0), Is.EquivalentTo(new[] { (e0, n1), (e1, n1) }));
            Assert.That(Edges(n1), Is.EquivalentTo(new[] { (e0, n0), (e1, n0) }));
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(2));

            graph.RemoveEdge(e0);
            Assert.That(Edges(n0), Is.EquivalentTo(new[] { (e1, n1) }));
            Assert.That(Edges(n1), Is.EquivalentTo(new[] { (e1, n0) }));
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(1));

            var n2 = graph.AddNode();
            var e2 = graph.AddEdge(n1, n2);
            Assert.That(Edges(n1), Is.EquivalentTo(new[] { (e2, n2), (e1, n0) }));
            Assert.That(Edges(n2), Is.EquivalentTo(new[] { (e2, n1) }));
            Assert.That(graph.NodeCount, Is.EqualTo(3));
            Assert.That(graph.EdgeCount, Is.EqualTo(2));

            graph.RemoveNode(n0);
            Assert.That(Edges(n1), Is.EquivalentTo(new[] { (e2, n2) }));
            Assert.That(Edges(n2), Is.EquivalentTo(new[] { (e2, n1) }));
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(1));

            var e3 = graph.AddEdge(n2, n2);
            Assert.That(Edges(n2), Is.EquivalentTo(new[] { (e2, n1), (e3, n2) }));
            Assert.That(graph.NodeCount, Is.EqualTo(2));
            Assert.That(graph.EdgeCount, Is.EqualTo(2));
            return;

            List<(EdgeIndex Edge, NodeIndex Node)> Edges(NodeIndex n)
            {
                var neighbors = graph.Neighbors(n);
                var dest = new List<(EdgeIndex Edge, NodeIndex Node)>((int)neighbors.Count);
                foreach (var span in neighbors)
                    for (var i = 0; i < span.Length; i++)
                        dest.Add((span[i].EdgeIndex, span[i].NodeIndex));
                dest.Sort((a, b) => a.Edge.CompareTo(b.Edge));
                return dest;
            }
        }

        [Test]
        public void TestFuzz()
        {
            var graph = new PackedGraph();
            var nodes = new List<NodeIndex>();
            var edges = new List<(EdgeIndex Index, NodeIndex From, NodeIndex To)>();
            var random = new Random(1234);

            for (var i = 0; i < 1_000; i++)
            {
                switch (nodes.Count < 2 ? 0 : random.Next(0, 5))
                {
                    // Add node
                    case 0:
                    {
                        var node = graph.AddNode();
                        nodes.Add(node);
                        Debug.WriteLine($"Add node {node}");
                        break;
                    }
                    // Add edge
                    case 1:
                    {
                        if (nodes.Count == 0) break;
                        var from = SampleNode();
                        var to = SampleNode();
                        var edge = graph.AddEdge(from, to);
                        Debug.WriteLine($"Add edge {edge} ({from} -> {to})");
                        edges.Add((edge, from, to));
                        break;
                    }
                    // Remove node
                    case 2:
                    {
                        if (nodes.Count == 0) break;
                        var nodeIx = random.Next(0, nodes.Count);
                        var node = nodes[nodeIx];
                        Debug.WriteLine($"Remove node {node}");
                        nodes.RemoveAtFast(nodeIx);
                        graph.RemoveNode(node);
                        edges.RemoveAll(x => x.From == node || x.To == node);
                        break;
                    }
                    // Remove edge
                    case 3:
                    {
                        if (edges.Count == 0) break;
                        var edgeIx = random.Next(0, edges.Count);
                        var edge = edges[edgeIx].Index;
                        Debug.WriteLine($"Remove edge {edge}");
                        graph.RemoveEdge(edge);
                        edges.RemoveAt(edgeIx);
                        break;
                    }
                    // Compaction
                    case 4:
                    {
                        Debug.WriteLine("Compact");
                        using (var report = graph.Compact())
                        {
                            for (var j = 0; j < nodes.Count; j++)
                                nodes[j] = report.Nodes.UpdateRef(nodes[j]);
                            for (var j = 0; j < edges.Count; j++)
                            {
                                var edge = edges[j];
                                report.Edges.UpdateRef(ref edge.Index);
                                report.Nodes.UpdateRef(ref edge.From);
                                report.Nodes.UpdateRef(ref edge.To);
                                edges[j] = edge;
                            }
                        }

                        break;
                    }
                }

                // Validate.
                Assert.That(graph.NodeCount, Is.EqualTo(nodes.Count));
                Assert.That(graph.EdgeCount, Is.EqualTo(edges.Count));
                foreach (var nodeIx in nodes)
                {
                    var actualNeighbors = new Dictionary<EdgeIndex, NodeIndex>();
                    foreach (var span in graph.Neighbors(nodeIx))
                        foreach (var item in span)
                            actualNeighbors.Add(item.EdgeIndex, item.NodeIndex);
                    var expectedNeighbors = edges
                        .Where(x => x.From == nodeIx || x.To == nodeIx)
                        .ToDictionary(x => x.Index, x => x.From == nodeIx ? x.To : x.From);
                    Assert.That(actualNeighbors, Is.EquivalentTo(expectedNeighbors));
                }

                foreach (var edge in edges)
                {
                    var actual = graph.Edge(edge.Index);
                    Assert.That(actual.NodeFrom, Is.EqualTo(edge.From));
                    Assert.That(actual.NodeTo, Is.EqualTo(edge.To));
                }
            }

            return;

            NodeIndex SampleNode() => nodes[random.Next(0, nodes.Count)];
        }
    }
}