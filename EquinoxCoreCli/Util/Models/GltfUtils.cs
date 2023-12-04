using System;
using SharpGLTF.Schema2;

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
    }
}