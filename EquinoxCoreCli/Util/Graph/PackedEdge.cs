using System;

namespace Equinox76561198048419394.Core.Cli.Util.Graph
{
    public readonly struct PackedEdge : IEquatable<PackedEdge>, IComparable<PackedEdge>
    {
        public readonly uint First;
        public readonly uint Second;

        public static void Primary(uint a, uint b, out PackedEdge primary) => primary = new PackedEdge(Math.Min(a, b), Math.Max(a, b));

        public static void PrimarySecondary(uint a, uint b, out PackedEdge primary, out PackedEdge secondary)
        {
            Primary(a, b, out primary);
            secondary = primary.Flip();
        }

        public PackedEdge(uint first, uint second)
        {
            First = first;
            Second = second;
        }

        public PackedEdge Flip() => new PackedEdge(Second, First);

        public int CompareTo(PackedEdge other)
        {
            var fc = First.CompareTo(other.First);
            return fc != 0 ? fc : Second.CompareTo(other.Second);
        }

        public bool Equals(PackedEdge other) => First == other.First && Second == other.Second;

        public override bool Equals(object obj) => obj is PackedEdge other && Equals(other);

        public override int GetHashCode() => ((int)First * 397) ^ (int)Second;
    }
}