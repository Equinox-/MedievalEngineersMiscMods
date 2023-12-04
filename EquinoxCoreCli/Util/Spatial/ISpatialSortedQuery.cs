using System;
using System.Collections.Generic;
using VRage.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Spatial
{
    public enum LeafUnsortedQueryResult
    {
        /// <summary>
        /// Visits the leaf in order.
        /// </summary>
        VisitLeaf,

        /// <summary>
        /// Skips the leaf.
        /// </summary>
        SkipLeaf,

        /// <summary>
        /// Immediately return from the query method.
        /// </summary>
        Terminate,
    }

    public interface ISpatialSortedQuery<TBox, TLeafData>
    {
        uint RootFlags { get; }

        NodeQueryResult VisitNode(ref TBox box, ref uint flags, out float score);

        LeafUnsortedQueryResult VisitLeafUnsorted(ref TBox box, ref TLeafData leafData, ref uint flags, out float score);

        LeafQueryResult VisitLeafSorted(ref TLeafData leafData, uint leafQueryFlags, float score);
    }

    public static class SpatialSortedQueryHeap<TNode>
    {
        [ThreadStatic]
        private static MyBinaryHeap<float, (TNode node, uint flags)> _heap;

        public static MyBinaryHeap<float, (TNode node, uint flags)> Instance
        {
            get
            {
                var result = _heap ?? (_heap = new MyBinaryHeap<float, (TNode node, uint flags)>());
                result.Clear();
                return result;
            }
        }
    }
}