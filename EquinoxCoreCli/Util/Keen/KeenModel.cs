using System;
using System.Collections.Generic;
using BulletXNA.BulletCollision;
using BulletXNA.LinearMath;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Animations;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public class KeenModel
    {
        #region Vertices

        private Vector3[] _positions = Array.Empty<Vector3>();
        private Byte4[] _normals = Array.Empty<Byte4>();
        private Byte4[] _tangents = Array.Empty<Byte4>();
        private Byte4[] _bitangents = Array.Empty<Byte4>();
        private HalfVector2[] _texcoords = Array.Empty<HalfVector2>();

        private Vector4I[] _boneIndices = Array.Empty<Vector4I>();
        private Vector4[] _boneWeights = Array.Empty<Vector4>();

        private int _vertices;

        public ModelVertex Vertex(int index) => new ModelVertex(this, index);

        private void ExpandVertices()
        {
            var newSize = Math.Max(64, _positions?.Length ?? 0) * 2;
            Array.Resize(ref _positions, newSize);
            Array.Resize(ref _normals, newSize);
            Array.Resize(ref _tangents, newSize);
            Array.Resize(ref _bitangents, newSize);
            Array.Resize(ref _texcoords, newSize);
            if (_bones.Count > 0)
            {
                Array.Resize(ref _boneIndices, newSize);
                Array.Resize(ref _boneWeights, newSize);
            }
        }

        public ModelVertex AllocateVertex()
        {
            var index = _vertices++;
            while (_positions == null || index >= _positions.Length)
                ExpandVertices();
            return new ModelVertex(this, index);
        }

        public readonly struct ModelVertex
        {
            private readonly KeenModel _model;
            public readonly int Index;

            public ModelVertex(KeenModel model, int index)
            {
                _model = model;
                Index = index;
            }

            public ref Vector3 Position => ref _model._positions[Index];

            public ref Byte4 PackedNormal => ref _model._normals[Index];

            public Vector3 Normal
            {
                get => VF_Packer.UnpackNormal(PackedNormal);
                set => PackedNormal = VF_Packer.PackNormalB4(ref value);
            }

            public ref Byte4 PackedTangent => ref _model._tangents[Index];

            public Vector3 Tangent
            {
                get => VF_Packer.UnpackNormal(PackedTangent);
                set => PackedTangent = VF_Packer.PackNormalB4(ref value);
            }

            public ref Byte4 PackedBiTangent => ref _model._tangents[Index];

            public Vector3 BiTangent
            {
                get => VF_Packer.UnpackNormal(PackedBiTangent);
                set => PackedBiTangent = VF_Packer.PackNormalB4(ref value);
            }

            public ref HalfVector2 PackedTexCoord => ref _model._texcoords[Index];

            public Vector2 TexCoord
            {
                get => PackedTexCoord.ToVector2();
                set => PackedTexCoord = new HalfVector2(value);
            }

            public ref Vector4I BoneIndices => ref _model._boneIndices[Index];
            public ref Vector4 BoneWeights => ref _model._boneWeights[Index];
        }

        #endregion

        public readonly List<MyLODDescriptor> Lods = new List<MyLODDescriptor>();

        private readonly Dictionary<string, MyMeshPartInfo> _partsByMaterial = new Dictionary<string, MyMeshPartInfo>();
        private readonly List<MyMeshPartInfo> _parts = new List<MyMeshPartInfo>();

        private readonly List<MyMeshSectionInfo> _sections = new List<MyMeshSectionInfo>();
        private readonly Dictionary<string, MyModelDummy> _dummies = new Dictionary<string, MyModelDummy>();
        private readonly List<MyModelBone> _bones = new List<MyModelBone>();
        private readonly ModelAnimations _animations = new ModelAnimations();

        public byte[] CollisionGeometry = Array.Empty<byte>();

        public MyMeshPartInfo Part(KeenMaterial material)
        {
            if (!_partsByMaterial.TryGetValue(material.Name, out var part))
            {
                part = new MyMeshPartInfo
                {
                    m_MaterialHash = material.Name.GetHashCode(),
                    m_MaterialDesc = material.Descriptor,
                    Technique = material.Technique,
                };
                _parts.Add(part);
                _partsByMaterial.Add(material.Name, part);
            }
            else
                System.Diagnostics.Debug.Assert(
                    KeenMaterial.MaterialEquals.Equals(part.m_MaterialDesc, material.Descriptor),
                    "Redefining material...");

            return part;
        }

        private T[] PrunedVertexArray<T>(T[] raw)
        {
            Array.Resize(ref raw, _vertices);
            return raw;
        }

        public Dictionary<string, object> ExportTags()
        {
            var tags = new Dictionary<string, object>();

            tags.Add(MyImporterConstants.TAG_DEBUG, Array.Empty<string>());
            tags.Add(MyImporterConstants.TAG_RESCALE_FACTOR, 1f);
            tags.Add(MyImporterConstants.TAG_USE_CHANNEL_TEXTURES, false);
            tags.Add(MyImporterConstants.TAG_SWAP_WINDING_ORDER, false);
            tags.Add(MyImporterConstants.TAG_PATTERN_SCALE, 1f);
            tags.Add(MyImporterConstants.TAG_BONE_MAPPING, Array.Empty<Vector3I>());

            tags.Add(MyImporterConstants.TAG_LODS, Lods.ToArray());
            tags.Add(MyImporterConstants.TAG_MESH_PARTS, _parts);
            tags.Add(MyImporterConstants.TAG_DUMMIES, _dummies);
            tags.Add(MyImporterConstants.TAG_MESH_SECTIONS, _sections);

            tags.Add(MyImporterConstants.TAG_VERTICES, PrunedVertexArray(_positions));
            tags.Add(MyImporterConstants.TAG_NORMALS, PrunedVertexArray(_normals));
            tags.Add(MyImporterConstants.TAG_TANGENTS, PrunedVertexArray(_tangents));
            tags.Add(MyImporterConstants.TAG_BINORMALS, PrunedVertexArray(_bitangents));
            tags.Add(MyImporterConstants.TAG_TEXCOORDS0, PrunedVertexArray(_texcoords));
            tags.Add(MyImporterConstants.TAG_TEXCOORDS1, Array.Empty<HalfVector2>());

            tags.Add(MyImporterConstants.TAG_BONES, _bones.ToArray());
            tags.Add(MyImporterConstants.TAG_BLENDINDICES, _bones.Count == 0 ? Array.Empty<Vector4I>() : PrunedVertexArray(_boneIndices));
            tags.Add(MyImporterConstants.TAG_BLENDWEIGHTS, _bones.Count == 0 ? Array.Empty<Vector4>() : PrunedVertexArray(_boneWeights));
            tags.Add(MyImporterConstants.TAG_ANIMATIONS, _animations);

            // Compute bounding box.
            var boundingBox = BoundingBox.CreateInvalid();
            for (var i = 0; i < _vertices; i++)
                boundingBox.Include(ref _positions[i]);
            tags.Add(MyImporterConstants.TAG_BOUNDING_BOX, boundingBox);

            // Compute bounding sphere.
            var center = boundingBox.Center;
            var radiusSquared = 0f;
            for (var i = 0; i < _vertices; i++)
            {
                Vector3.DistanceSquared(ref center, ref _positions[i], out var r2);
                if (r2 > radiusSquared)
                    radiusSquared = r2;
            }

            tags.Add(MyImporterConstants.TAG_BOUNDING_SPHERE, new BoundingSphere(center, (float)Math.Sqrt(radiusSquared)));
            var triangles = 0;
            foreach (var part in _parts)
                triangles += part.m_indices.Count / 3;
            tags.Add(MyImporterConstants.TAG_MODEL_INFO, new MyModelInfo(triangles, _vertices, boundingBox.Size));

            // Compute BVH.
            var bvh = new GImpactQuantizedBvh(new PrimitiveManager(this));
            bvh.BuildSet();
            tags.Add(MyImporterConstants.TAG_MODEL_BVH, bvh);

            tags.Add(MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY, CollisionGeometry ?? Array.Empty<byte>());

            return tags;
        }

        private sealed class PrimitiveManager : IPrimitiveManagerBase
        {
            private readonly KeenModel _model;
            private readonly int[] _partOffsets;
            private readonly int _primitiveCount;

            public PrimitiveManager(KeenModel model)
            {
                _model = model;
                _partOffsets = new int[model._parts.Count];
                var offset = 0;
                for (var i = 0; i < model._parts.Count; i++)
                {
                    _partOffsets[i] = offset;
                    offset += model._parts[i].m_indices.Count / 3;
                }

                _primitiveCount = offset;
            }


            public void Cleanup()
            {
            }

            public bool IsTrimesh() => true;

            public int GetPrimitiveCount() => _primitiveCount;

            private void GetTriangle(int primIndex, out int a, out int b, out int c)
            {
                var partId = _partOffsets.AsSpan().BinarySearch(primIndex);
                if (partId < 0)
                    partId = ~partId - 1;
                var offset = (primIndex - _partOffsets[partId]) * 3;
                var part = _model._parts[partId].m_indices;
                a = part[offset];
                b = part[offset + 1];
                c = part[offset + 2];
            }

            private static void Convert(in Vector3 pt, out IndexedVector3 vec)
            {
                vec.X = pt.X;
                vec.Y = pt.Y;
                vec.Z = pt.Z;
            }

            public void GetPrimitiveBox(int primIndex, out AABB primbox)
            {
                GetTriangle(primIndex, out var a, out var b, out var c);
                var box = BoundingBox.CreateFromHalfExtent(_model._positions[a], 0);
                box.Include(ref _model._positions[b]);
                box.Include(ref _model._positions[c]);
                Convert(in box.Min, out primbox.m_min);
                Convert(in box.Min, out primbox.m_max);
            }

            public void GetPrimitiveTriangle(int primIndex, PrimitiveTriangle triangle)
            {
                GetTriangle(primIndex, out var a, out var b, out var c);
                Convert(in _model._positions[a], out triangle.m_vertices[0]);
                Convert(in _model._positions[b], out triangle.m_vertices[1]);
                Convert(in _model._positions[c], out triangle.m_vertices[2]);
            }
        }
    }
}