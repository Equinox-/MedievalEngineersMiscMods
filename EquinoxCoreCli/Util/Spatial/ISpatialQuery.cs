using System;
using System.Collections.Generic;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Spatial
{
    public enum NodeQueryResult
    {
        /// <summary>
        /// Visit all children of this node recursively using <see cref="ISpatialQuery{T, T}.VisitNode"/>
        /// and <see cref="ISpatialQuery{T, T}.VisitLeaf"/> with skipFilter set to false.
        /// </summary>
        VisitChildren,

        /// <summary>
        /// Skips the children of this node and continues to process the tree.
        /// </summary>
        SkipChildren,

        /// <summary>
        /// Skips the children of this node and immediately returns from the query method.
        /// </summary>
        Terminate,
    }

    public enum LeafQueryResult
    {
        /// <summary>
        /// Continue processing tree.
        /// </summary>
        Continue,

        /// <summary>
        /// Immediately return from the query method.
        /// </summary>
        Terminate,
    }

    public interface ISpatialQuery<TBox, TLeafData>
    {
        uint RootFlags { get; }
        NodeQueryResult VisitNode(ref TBox box, ref uint flags);
        LeafQueryResult VisitLeaf(ref TBox box, ref TLeafData leafData, uint parentQueryFlags);
    }

    public static class SpatialQueryStack<TNode>
    {
        [ThreadStatic]
        private static Stack<(TNode node, uint flags)> _stack;

        public static Stack<(TNode node, uint flags)> Instance
        {
            get
            {
                var result = _stack ?? (_stack = new Stack<(TNode node, uint flags)>());
                result.Clear();
                return result;
            }
        }
    }

    public struct BoxSpatialQuery<TLeafData> : ISpatialQuery<BoundingBox, TLeafData>
    {
        private const uint FullyContained = 1;

        private BoundingBox _query;
        public readonly List<TLeafData> Hits;

        public BoxSpatialQuery(in BoundingBox query, List<TLeafData> hits = null)
        {
            _query = query;
            Hits = hits ?? new List<TLeafData>();
        }

        public uint RootFlags => 0;

        public NodeQueryResult VisitNode(ref BoundingBox box, ref uint flags)
        {
            if ((flags & FullyContained) != 0)
                return NodeQueryResult.VisitChildren;
            _query.Contains(ref box, out var containment);
            switch (containment)
            {
                case ContainmentType.Disjoint:
                    return NodeQueryResult.SkipChildren;
                case ContainmentType.Contains:
                    flags |= FullyContained;
                    return NodeQueryResult.VisitChildren;
                case ContainmentType.Intersects:
                    return NodeQueryResult.VisitChildren;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public LeafQueryResult VisitLeaf(ref BoundingBox box, ref TLeafData leafData, uint parentQueryFlags)
        {
            if ((parentQueryFlags & FullyContained) != 0 || _query.Intersects(ref box))
                Hits.Add(leafData);
            return LeafQueryResult.Continue;
        }
    }
}