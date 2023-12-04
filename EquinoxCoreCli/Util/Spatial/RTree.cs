using System;
using System.Diagnostics;
using Equinox76561198048419394.Core.Util;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Spatial
{
    public sealed class RTree<T> where T : IBoxBounded
    {
        private readonly uint _minChildren;
        private readonly uint _maxChildren;

        private readonly PagedFreeList<T> _leaves = new PagedFreeList<T>();
        private readonly PagedFreeList<Node> _nodes = new PagedFreeList<Node>();
        private uint _root;

        public RTree(uint minChildren = 3, uint maxChildren = 8)
        {
            if (maxChildren > 60)
                throw new Exception("Max children can't be more than 60");

            _minChildren = minChildren;
            _maxChildren = maxChildren;
            _root = _nodes.Allocate();
            ref var root = ref _nodes[_root];
            root.Init(0);
        }

        public int LeafCount => _leaves.Count;

        public ref T GetLeaf(uint index) => ref _leaves[index];

        public void Search<TQuery>(ref TQuery query) where TQuery : ISpatialQuery<BoundingBox, T>
        {
            var stack = SpatialQueryStack<uint>.Instance;

            stack.Push((_root, query.RootFlags));
            while (stack.Count > 0)
            {
                var (nodeId, flags) = stack.Pop();
                ref var node = ref _nodes[nodeId];
                if (node.IsLeaf)
                {
                    for (var i = 0; i < node.EntryCount; i++)
                    {
                        ref var entry = ref node.Entries.Ref(i);
                        ref var leaf = ref _leaves[entry.LeafIndex];
                        switch (query.VisitLeaf(ref entry.Box, ref leaf, flags))
                        {
                            case LeafQueryResult.Continue:
                                break;
                            case LeafQueryResult.Terminate:
                                return;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < node.EntryCount; i++)
                    {
                        ref var entry = ref node.Entries.Ref(i);
                        var childFlags = flags;
                        switch (query.VisitNode(ref entry.Box, ref childFlags))
                        {
                            case NodeQueryResult.VisitChildren:
                                stack.Push((entry.NodeIndex, childFlags));
                                break;
                            case NodeQueryResult.SkipChildren:
                                break;
                            case NodeQueryResult.Terminate:
                                return;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }
        }

        public void SearchSorted<TQuery>(ref TQuery query) where TQuery : ISpatialSortedQuery<BoundingBox, T>
        {
            var stack = SpatialSortedQueryHeap<int>.Instance;

            stack.Insert(((int) _root, query.RootFlags), 0);
            while (stack.Count > 0)
            {
                var score = stack.MinKey();
                var (nodeId, flags) = stack.RemoveMin();
                if (nodeId < 0)
                {
                    // Leaf ID.
                    ref var leaf = ref _leaves[(uint)(-nodeId - 1)];
                    switch (query.VisitLeafSorted(ref leaf, flags, score))
                    {
                        case LeafQueryResult.Continue:
                            break;
                        case LeafQueryResult.Terminate:
                            return;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    continue;
                }

                ref var node = ref _nodes[(uint) nodeId];
                if (node.IsLeaf)
                {
                    for (var i = 0; i < node.EntryCount; i++)
                    {
                        ref var entry = ref node.Entries.Ref(i);
                        ref var leaf = ref _leaves[entry.LeafIndex];
                        var leafFlags = flags;
                        switch (query.VisitLeafUnsorted(ref entry.Box, ref leaf, ref leafFlags, out var leafScore))
                        {
                            case LeafUnsortedQueryResult.VisitLeaf:
                                stack.Insert(((int) (-entry.LeafIndex - 1), leafFlags), leafScore);
                                break;
                            case LeafUnsortedQueryResult.SkipLeaf:
                                break;
                            case LeafUnsortedQueryResult.Terminate:
                                return;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < node.EntryCount; i++)
                    {
                        ref var entry = ref node.Entries.Ref(i);
                        var childFlags = flags;
                        switch (query.VisitNode(ref entry.Box, ref childFlags, out var childScore))
                        {
                            case NodeQueryResult.VisitChildren:
                                stack.Insert(((int) entry.NodeIndex, childFlags), childScore);
                                break;
                            case NodeQueryResult.SkipChildren:
                                break;
                            case NodeQueryResult.Terminate:
                                return;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }
        }

        public uint Insert(in T childIn)
        {
            var leafIndex = _leaves.Allocate();
            ref var leaf = ref _leaves[leafIndex];
            leaf = childIn;

            Entry entry = default;
            entry.LeafIndex = leafIndex;
            leaf.GetBounds(ref entry.Box);

            ref var rootNode = ref _nodes[_root];
            InsertInternal(ref rootNode, ref entry, 0);

            if (rootNode.EntryCount <= _maxChildren)
            {
                CheckRecursive(ref rootNode);
                return leafIndex;
            }

            // Split the root node up.
            var newRootIndex = _nodes.Allocate();
            ref var newRoot = ref _nodes[newRootIndex];
            newRoot.Init(rootNode.Level + 1);
            ref var oldRootEntry = ref AppendEntry(ref newRoot);
            oldRootEntry.NodeIndex = _root;

            ref var peeledEntry = ref AppendEntry(ref newRoot);
            Split(ref oldRootEntry, ref rootNode, ref peeledEntry);

            _root = newRootIndex;
            CheckRecursive(ref newRoot);
            return leafIndex;
        }

        [Conditional("RTREE_CHECKS")]
        private void CheckRecursive(ref Node node)
        {
            node.CheckValid();
            if (node.IsLeaf) return;
            for (var i = 0; i < node.EntryCount; i++)
                CheckRecursive(ref _nodes[node.Entries.Ref(i).NodeIndex]);
        }

        private void InsertInternal(ref Node node, ref Entry item, uint level)
        {
            if (item.IsNode && node.Level == level)
            {
                AppendEntry(ref node) = item;
                CheckRecursive(ref node);
                return;
            }

            if (node.IsLeaf)
            {
                AppendEntry(ref node) = item;
                CheckRecursive(ref node);
                return;
            }

            // Go deeper
            var minEntryIndex = 0;
            var minEntryBox = default(BoundingBox);
            var minEntryDiff = float.PositiveInfinity;

            for (var i = 0; i < node.EntryCount; i++)
            {
                ref var entry = ref node.Entries.Ref(i);

                var expandedBox = entry.Box;
                expandedBox.Include(ref item.Box);

                var diff = Score(ref expandedBox) - Score(ref entry.Box);
                if (diff >= minEntryDiff) continue;
                minEntryIndex = i;
                minEntryBox = expandedBox;
                minEntryDiff = diff;
            }

            // Insert into the min entry node.
            ref var minEntry = ref node.Entries.Ref(minEntryIndex);
            minEntry.Box = minEntryBox;
            ref var minNode = ref _nodes[minEntry.NodeIndex];
            InsertInternal(ref minNode, ref item, level);

            if (minNode.EntryCount <= _maxChildren)
                return;
            // Split the child node up.
            ref var peeledEntry = ref AppendEntry(ref node);
            Split(ref minEntry, ref minNode, ref peeledEntry);
            CheckRecursive(ref node);
        }

        private void Split(ref Entry keptEntry, ref Node keptNode, ref Entry peeledEntry)
        {
            var peeledNodeIndex = _nodes.Allocate();
            peeledEntry.NodeIndex = peeledNodeIndex;
            ref var peeledNode = ref _nodes[peeledNodeIndex];
            peeledNode.Init(keptNode.Level);

            PickLinearSeeds(ref keptNode, out var keptSeed, out var peeledSeed);
            ref var keptBox = ref keptEntry.Box;
            keptBox = keptNode.Entries.Ref(keptSeed).Box;

            ref var peeledBox = ref peeledEntry.Box;
            peeledBox = keptNode.Entries.Ref(peeledSeed).Box;

            var originalEntriesCount = keptNode.EntryCount;
            keptNode.EntryCount = 0;

            for (var i = 0; i < originalEntriesCount; i++)
            {
                ref var entry = ref keptNode.Entries.Ref(i);
                if (i == keptSeed)
                {
                    // Seed boxes already included.
                    AppendEntry(ref keptNode) = entry;
                    continue;
                }

                if (i == peeledSeed)
                {
                    // Seed boxes already included.
                    AppendEntry(ref peeledNode) = entry;
                    continue;
                }

                var remaining = originalEntriesCount - i;
                if (peeledNode.EntryCount + remaining - (keptSeed > i ? 1 : 0) <= _minChildren)
                {
                    // Entry needs to be put into the peeled node to hit the min child count.
                    AppendEntry(ref peeledNode) = entry;
                    peeledBox.Include(ref entry.Box);
                    continue;
                }

                if (keptNode.EntryCount + remaining - (peeledSeed > i ? 1 : 0) <= _minChildren)
                {
                    // Entry needs to be put into the kept node to hit th emin child count;
                    AppendEntry(ref keptNode) = entry;
                    keptBox.Include(ref entry.Box);
                    continue;
                }

                // Just put it in the kept node -- this logic could be better.
                AppendEntry(ref keptNode) = entry;
                keptBox.Include(ref entry.Box);
            }

            for (var i = keptNode.EntryCount; i < originalEntriesCount; i++)
                keptNode.Entries[i] = default;

            System.Diagnostics.Debug.Assert(keptNode.EntryCount + peeledNode.EntryCount == originalEntriesCount, "Entries went missing");

            keptNode.CheckValid();
            peeledNode.CheckValid();
        }

        private static void PickLinearSeeds(ref Node node, out int first, out int second)
        {
            // See http://www-db.deis.unibo.it/courses/SI-LS/papers/Gut84.pdf 3.5.3 "A Linear-Cost Algorithm"

            // "Find extreme rectangles along all dimensions"
            var fullBox = BoundingBox.CreateInvalid();
            var maxLowSideIndex = Vector3I.Zero;
            var maxLowSide = new Vector3(float.NegativeInfinity);

            var minHighSideIndex = Vector3I.Zero;
            var minHighSide = new Vector3(float.PositiveInfinity);

            for (var i = 0; i < node.EntryCount; i++)
            {
                ref var entry = ref node.Entries.Ref(i);
                ref var box = ref entry.Box;
                fullBox.Include(ref box);
                UpdateSides(
                    i,
                    ref maxLowSideIndex.X, ref maxLowSide.X, box.Min.X,
                    ref minHighSideIndex.X, ref minHighSide.X, box.Max.X);
                UpdateSides(
                    i,
                    ref maxLowSideIndex.Y, ref maxLowSide.Y, box.Min.Y,
                    ref minHighSideIndex.Y, ref minHighSide.Y, box.Max.Y);
                UpdateSides(
                    i,
                    ref maxLowSideIndex.Z, ref maxLowSide.Z, box.Min.Z,
                    ref minHighSideIndex.Z, ref minHighSide.Z, box.Max.Z);
            }

            // "Adjust for shape of the rectangle cluster" 
            var normalizedSeparation = Vector3.Abs((maxLowSide - minHighSide) / fullBox.Extents);

            // "Select the most extreme pair"
            var mostExtreme = normalizedSeparation.X;
            first = maxLowSideIndex.X;
            second = minHighSideIndex.X;

            if (normalizedSeparation.Y > mostExtreme)
            {
                mostExtreme = normalizedSeparation.Y;
                first = maxLowSideIndex.Y;
                second = minHighSideIndex.Y;
            }

            if (normalizedSeparation.Z > mostExtreme)
            {
                mostExtreme = normalizedSeparation.Z;
                first = maxLowSideIndex.Z;
                second = minHighSideIndex.Z;
            }

            if (first > second)
                MyUtils.Swap(ref first, ref second);
            else if (first == second)
            {
                if (first == 0)
                    second = 1;
                else
                    first = 0;
            }

            return;

            void UpdateSides(
                int index,
                ref int maxLowIndex, ref float maxLow, float min,
                ref int minHighIndex, ref float minHigh, float max)
            {
                if (min > maxLow)
                {
                    maxLow = min;
                    maxLowIndex = index;
                }

                if (max < minHigh)
                {
                    minHigh = max;
                    minHighIndex = index;
                }
            }
        }

        private static float Score(ref BoundingBox box) => box.Volume();

        private struct Node
        {
            public uint Level;
            public int EntryCount;
            public StackArray<Entry> Entries;

            public bool IsLeaf => Level == 0;

            public void Free()
            {
                Entries = default;
            }

            public void Init(uint level)
            {
                Level = level;
                EntryCount = 0;
                Entries = default;
            }

            [Conditional("RTREE_CHECKS")]
            public void CheckValid()
            {
                for (var i = 0; i < EntryCount; i++)
                {
                    ref var entry = ref Entries.Ref(i);
                    if (IsLeaf)
                    {
                        if (!entry.IsLeaf)
                            Debugger.Break();
                    }
                    else
                    {
                        if (!entry.IsNode)
                            Debugger.Break();
                    }
                }
            }
        }

        private static ref Entry AppendEntry(ref Node node) => ref node.Entries.Ref(node.EntryCount++, true);

        private struct Entry
        {
            public BoundingBox Box;
            private int _index;

            public bool IsLeaf => _index < 0;
            public bool IsNode => _index > 0;

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public uint NodeIndex
            {
                get
                {
                    System.Diagnostics.Debug.Assert(_index > 0);
                    return (uint)(_index - 1);
                }
                set => _index = (int)(value + 1);
            }

            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public uint LeafIndex
            {
                get
                {
                    System.Diagnostics.Debug.Assert(_index < 0);
                    return (uint)(-_index - 1);
                }
                set => _index = (int)(-value - 1);
            }
        }
    }
}