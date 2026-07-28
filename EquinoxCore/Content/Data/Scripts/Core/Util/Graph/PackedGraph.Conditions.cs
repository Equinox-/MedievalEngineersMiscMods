namespace Equinox76561198048419394.Core.Util.Graph
{
    public static class PackedGraphConditions
    {
        public interface INode
        {
            bool TestNode(PackedGraph graph, NodeIndex ix);
        }

        public interface IEdge
        {
            bool TestEdge(PackedGraph graph, EdgeIndex ix);
        }

        public interface IGraph : INode, IEdge
        {
        }
    }
}