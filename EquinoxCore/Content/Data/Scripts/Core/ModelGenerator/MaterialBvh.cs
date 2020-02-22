using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Equinox76561198048419394.Core.Util.Memory;
using VRage.Import;
using VRage.Library.Collections;
using VRageMath;
using VRageMath.PackedVector;
using VRageMath.Serialization;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public class MaterialBvh
    {
        public readonly PackedBvh _bvh;
        private readonly TriangleData[] _triangles;
        private readonly IReadOnlyCollection<string> _strings;

        private MaterialBvh(PackedBvh bvh, TriangleData[] tri, IReadOnlyCollection<string> table)
        {
            _bvh = bvh;
            _triangles = tri;
            _strings = table;
        }


        public static MaterialBvh Create(Dictionary<string, object> tags, int shapesPerNode = 8)
        {
            var verts = (Vector3[]) tags.GetValueOrDefault(MyImporterConstants.TAG_VERTICES);
            var normals = (Byte4[]) tags.GetValueOrDefault(MyImporterConstants.TAG_NORMALS);
            var box = (BoundingBox) tags.GetValueOrDefault(MyImporterConstants.TAG_BOUNDING_BOX);
            var parts = (List<MyMeshPartInfo>) tags.GetValueOrDefault(MyImporterConstants.TAG_MESH_PARTS);
            var sections = (List<MyMeshSectionInfo>) tags.GetValueOrDefault(MyImporterConstants.TAG_MESH_SECTIONS);

            var strings = new HashSet<string>();
            var triangles = new List<TriangleData>(parts.Sum(x => x.m_indices.Count / 3));

            void AddTriangle(string section, string material, int v0, int v1, int v2)
            {
                section = section ?? "";
                material = material ?? "";
                strings.Add(section);
                strings.Add(material);
                var normal = normals != null
                    ? (VF_Packer.UnpackNormal(normals[v0]) + VF_Packer.UnpackNormal(normals[v1]) + VF_Packer.UnpackNormal(normals[v2]))
                    : (Vector3?) null;
                triangles.Add(new TriangleData(new Triangle(in verts[v0], in verts[v1], in verts[v2], normal), section, material));
            }

            foreach (var part in parts)
            {
                var materialName = part.m_MaterialDesc?.MaterialName ?? "<<no material>>";
                using (PoolManager.Get(out List<KeyValuePair<string, MyMeshSectionMeshInfo>> mtlSections))
                {
                    foreach (var section in sections)
                    foreach (var mesh in section.Meshes)
                        if (mesh.MaterialName == materialName)
                            mtlSections.Add(new KeyValuePair<string, MyMeshSectionMeshInfo>(section.Name, mesh));

                    mtlSections.Sort((a, b) => a.Value.StartIndex.CompareTo(b.Value.StartIndex));

                    var processed = 0;
                    var idx = part.m_indices;
                    foreach (var section in mtlSections)
                    {
                        for (var i = processed; i < section.Value.StartIndex - 2; i += 3)
                            AddTriangle(null, materialName, idx[i], idx[i + 1], idx[i + 2]);
                        var endOfSection = section.Value.StartIndex + section.Value.IndexCount;
                        for (var i = section.Value.StartIndex; i < endOfSection - 2; i += 3)
                            AddTriangle(section.Key, materialName, idx[i], idx[i + 1], idx[i + 2]);
                        processed = endOfSection;
                    }

                    for (var i = processed; i < idx.Count - 2; i += 3)
                        AddTriangle(null, materialName, idx[i], idx[i + 1], idx[i + 2]);
                }
            }

            PackedBvh bvh;
            using (ArrayPool<BoundingBox>.Get(triangles.Count, out var array))
            {
                for (var i = 0; i < triangles.Count; i++)
                {
                    var tri = triangles[i].Triangle;
                    array[i].Min = Vector3.Min(Vector3.Min(tri.A, tri.B), tri.C);
                    array[i].Max = Vector3.Max(Vector3.Max(tri.A, tri.B), tri.C);
                }

                using (var builder = new SahBvhBuilder(shapesPerNode, new EqReadOnlySpan<BoundingBox>(array, 0, triangles.Count)))
                    bvh = builder.Build();
            }

            return new MaterialBvh(bvh, triangles.ToArray(), strings);
        }

        public ref readonly Triangle GetTriangle(int triangleId)
        {
            return ref _triangles[triangleId].Triangle;
        }
        
        public bool RayCast(in Ray ray, out string section, out string material, out double dist)
        {
            var bestTriId = -1;
            var bestTriDist = double.MaxValue;
            using (var itr = _bvh.IntersectRayOrdered(in ray))
            {
                while (itr.MoveNext())
                {
                    if (itr.CurrentDist > bestTriDist)
                        break;
                    foreach (var triId in _bvh.GetProxies(itr.Current))
                    {
                        ref var tri = ref _triangles[triId];
                        if (!tri.Triangle.Intersects(in ray, out var triDist) || triDist > bestTriDist)
                            continue;
                        bestTriId = triId;
                        bestTriDist = triDist;
                    }
                }
            }

            if (bestTriId >= 0)
            {
                section = _triangles[bestTriId].Section;
                material = _triangles[bestTriId].MaterialName;
                dist = bestTriDist;
                return true;
            }

            section = null;
            material = null;
            dist = double.MaxValue;
            return false;
        }

        private struct TriangleData
        {
            public readonly Triangle Triangle;
            public readonly string Section;
            public readonly string MaterialName;

            public TriangleData(Triangle tri, string section, string materialName)
            {
                Triangle = tri;
                Section = section;
                MaterialName = materialName;
            }
        }

        public static readonly ISerializer<MaterialBvh> Serializer = new MaterialBvhSerializer();

        private class MaterialBvhSerializer : ISerializer<MaterialBvh>
        {
            public void Write(BinaryWriter target, in MaterialBvh value)
            {
                using (PoolManager.Get(out Dictionary<string, int> stringLut))
                {
                    target.Write(value._strings.Count);
                    var i = 0;
                    foreach (var s in value._strings)
                    {
                        stringLut.Add(s, i++);
                        target.Write(s);
                    }

                    target.Write(value._triangles.Length);
                    foreach (var tri in value._triangles)
                    {
                        target.Write(tri.Triangle.A);
                        target.Write(tri.Triangle.B);
                        target.Write(tri.Triangle.C);
                        target.BaseStream.Write7BitEncodedInt(stringLut[tri.Section]);
                        target.BaseStream.Write7BitEncodedInt(stringLut[tri.MaterialName]);
                    }

                    PackedBvh.Serializer.Write(target, value._bvh);
                }
            }

            public void Read(BinaryReader source, out MaterialBvh value)
            {
                var lutSize = source.ReadInt32();
                var lut = new List<string>(lutSize);
                for (var i = 0; i < lutSize; i++)
                    lut.Add(source.ReadString());

                var tris = new TriangleData[source.ReadInt32()];
                for (var i = 0; i < tris.Length; i++)
                {
                    tris[i] = new TriangleData(new Triangle(
                            source.ReadVector3(),
                            source.ReadVector3(),
                            source.ReadVector3()
                        ),
                        lut[source.BaseStream.Read7BitEncodedInt()],
                        lut[source.BaseStream.Read7BitEncodedInt()]
                    );
                }

                PackedBvh.Serializer.Read(source, out var bvh);

                value = new MaterialBvh(bvh, tris, lut);
            }
        }
    }
}