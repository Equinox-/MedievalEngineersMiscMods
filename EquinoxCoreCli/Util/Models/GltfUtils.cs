using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpGLTF.Schema2;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Models
{
    public static class GltfUtils
    {
        public static Accessor FindVertexAccessor(this MeshPrimitive primitive, string name)
        {
            foreach (var accessor in primitive.VertexAccessors)
                if (accessor.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return accessor.Value;
            return null;
        }

        public static Accessor FindPositionAccessor(this MeshPrimitive primitive) => primitive.FindVertexAccessor("POSITION");

        public static Accessor FindNormalAccessor(this MeshPrimitive primitive) => primitive.FindVertexAccessor("NORMAL");

        public static IEnumerable<Node> FlattenHierarchy(this Node node)
        {
            yield return node;
            foreach (var child in node.VisualChildren)
            foreach (var descendant in child.FlattenHierarchy())
                yield return descendant;
        }

        public static Matrix ToKeen(this Matrix4x4 matrix) => Unsafe.As<Matrix4x4, Matrix>(ref matrix);
    }
}