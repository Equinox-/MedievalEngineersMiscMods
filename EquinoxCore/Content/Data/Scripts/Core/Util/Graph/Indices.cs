using Equinox76561198048419394.Core.Util.Columnar;

namespace Equinox76561198048419394.Core.Util.Graph
{
    public readonly struct NodeIndex : IColumnarStoreRow<NodeIndex>
    {
        public uint Value { get; }
        private NodeIndex(uint value) => Value = value;
        public bool Equals(NodeIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NodeIndex other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public int CompareTo(NodeIndex other) => Value.CompareTo(other.Value);
        public static explicit operator NodeIndex(uint v) => new NodeIndex(v);
        public static explicit operator uint(NodeIndex v) => v.Value;
        public static bool operator ==(NodeIndex a, NodeIndex b) => a.Value == b.Value;
        public static bool operator !=(NodeIndex a, NodeIndex b) => a.Value != b.Value;
        public NodeIndex Create(uint value) => (NodeIndex)value;
        public override string ToString() => $"n{Value}";
    }

    public readonly struct EdgeIndex : IColumnarStoreRow<EdgeIndex>
    {
        public uint Value { get; }
        private EdgeIndex(uint value) => Value = value;
        public bool Equals(EdgeIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EdgeIndex other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public int CompareTo(EdgeIndex other) => Value.CompareTo(other.Value);
        public static explicit operator EdgeIndex(uint v) => new EdgeIndex(v);
        public static explicit operator uint(EdgeIndex v) => v.Value;
        public static bool operator ==(EdgeIndex a, EdgeIndex b) => a.Value == b.Value;
        public static bool operator !=(EdgeIndex a, EdgeIndex b) => a.Value != b.Value;
        public EdgeIndex Create(uint value) => (EdgeIndex)value;
        public override string ToString() => $"e{Value}";
    }
}