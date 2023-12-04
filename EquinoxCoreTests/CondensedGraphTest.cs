using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.Cli.Util.Graph;
using NUnit.Framework;

namespace EquinoxCoreTests
{
    [TestFixture]
    public class CondensedGraphTest
    {
        private static Dictionary<uint, uint[]> ComparableNeighbor(IEnumerable<Neighbor<CondensedGraph<ZeroSize>.CondensedEdgeView>> neighbors)
        {
            return neighbors
                .ToDictionary(
                    x => x.Node,
                    x => x.Data.Select(y => y.Node).ToArray());
        }

        private static ValueTuple<uint, uint>[] ComparableEdges<T>(IEnumerableGraph<T> edges) =>
            edges.Edges.Select(x => (Math.Min(x.Node1, x.Node2), Math.Max(x.Node1, x.Node2)))
                .OrderBy(a => a.Item1)
                .ToArray();

        [Test]
        public void TestInOrderChain()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [3u] = new[] { 1u, 2u, 3u }
                });
            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 3u) });
        }

        [Test]
        public void TestOutOfOrderChain()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(2u, 3u, default);
            condensed.AddEdge(1u, 2u, default);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [3u] = new[] { 1u, 2u, 3u }
                });
            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 3u) });
        }

        [Theory]
        public void TestSplitEdge(bool flippedSplit, bool toExistingEdge)
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            if (toExistingEdge)
                condensed.AddEdge(4u, 5u, default);

            if (flippedSplit)
                condensed.AddEdge(4u, 2u, default);
            else
                condensed.AddEdge(2u, 4u, default);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(2u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 1u, 0u },
                    [3u] = new[] { 3u },
                    [toExistingEdge ? 5u : 4u] = toExistingEdge ? new[] { 4u, 5u } : new[] { 4u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 2u), (2u, 3u), (2u, toExistingEdge ? 5u : 4u) });
        }

        [Test]
        public void TestSingleCondenseOnRemoval()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(1u, 3u, default);

            condensed.RemoveEdge(1u, 3u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [2u] = new[] { 1u, 2u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(2u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 2u) });
        }

        [Test]
        public void TestDualCondenseOnRemoval()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            condensed.RemoveEdge(1u, 2u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [1u] = new[] { 1u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(3u)),
                new Dictionary<uint, uint[]>
                {
                    [2u] = new[] { 2u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 1u), (2u, 3u) });
        }

        [Test]
        public void TestShrinkOnRemoval()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            condensed.RemoveEdge(2u, 3u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [2u] = new[] { 1u, 2u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(2u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 2u) });
        }

        [Test]
        public void TestNoopRemovalCondensed()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);

            condensed.RemoveEdge(0u, 2u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [2u] = new[] { 1u, 2u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(2u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 2u) });
        }

        [Test]
        public void TestNoopRemovalVapor()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);
            condensed.AddEdge(3u, 4u, default);

            condensed.RemoveEdge(1u, 3u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [4u] = new[] { 1u, 2u, 3u, 4u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(4u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 3u, 2u, 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 4u) });
        }

        [Theory]
        public void TestNoopRemovalCondensedAndVapor(bool flipped)
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            if (flipped)
                condensed.RemoveEdge(3u, 1u);
            else
                condensed.RemoveEdge(1u, 3u);

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [3u] = new[] { 1u, 2u, 3u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(3u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 2u, 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 3u) });
        }

        [Test]
        public void TestCondensedRemoval()
        {
            var condensed = new CondensedGraph<ZeroSize>();

            condensed.AddEdge(0u, 1u, default);
            condensed.AddEdge(1u, 2u, default);
            condensed.AddEdge(2u, 3u, default);

            condensed.AddEdge(1u, 4u, default);

            condensed.RemoveCondensedEdge(4u, condensed.Condensed.Neighbors(4u).First());


            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(0u)),
                new Dictionary<uint, uint[]>
                {
                    [3u] = new[] { 1u, 2u, 3u }
                });

            Assert.AreEqual(
                ComparableNeighbor(condensed.Condensed.Neighbors(3u)),
                new Dictionary<uint, uint[]>
                {
                    [0u] = new[] { 2u, 1u, 0u }
                });

            Assert.AreEqual(
                ComparableEdges(condensed.Condensed),
                new[] { (0u, 3u) });
        }
    }
}