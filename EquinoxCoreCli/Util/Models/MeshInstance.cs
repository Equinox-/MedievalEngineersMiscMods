using System;
using SharpGLTF.Schema2;

namespace Equinox76561198048419394.Core.Cli.Util.Models
{
    public readonly struct MeshInstance : IEquatable<MeshInstance>
    {
        public readonly Node Node;
        public readonly MeshPrimitive Primitive;

        public MeshInstance(Node node, MeshPrimitive primitive)
        {
            Node = node;
            Primitive = primitive;
        }

        public bool Equals(MeshInstance other) => Node.Equals(other.Node) && Primitive.Equals(other.Primitive);

        public override bool Equals(object obj) => obj is MeshInstance other && Equals(other);

        public override int GetHashCode() => (Node.GetHashCode() * 397) ^ Primitive.GetHashCode();

        public override string ToString() => $"{Node.Name}[{Primitive.Material?.Name ?? "unknown"}]";
    }
}