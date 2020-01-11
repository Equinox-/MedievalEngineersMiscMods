// Copyright Keen Software House
// With modifications by Equinox

// Decompiled with JetBrains decompiler
// Type: VRageRender.Import.MyModelExporter
// Assembly: VRage.Render, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: A6F7F2B5-43B1-4DDD-8DD3-CA58C779F7FF

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Animations;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator.ModelIO
{
    public static class ModelExporter
    {
        public static bool USE_ORDERED_DICTIONARIES = false;

        private static IEnumerable<KeyValuePair<TK, TV>> AdaptDictionary<TK, TV>(Dictionary<TK, TV> input)
        {
            if (!USE_ORDERED_DICTIONARIES)
                return input;
            return input.OrderBy(x => x.Key);
        }

        /// <summary>
        /// Export 
        /// </summary>
        /// <param name="result"></param>
        /// <param name="tagData"></param>
        /// <returns>tag index</returns>
        public static Dictionary<string, int> ExportTags(BinaryWriter result, Dictionary<string, object> tagData)
        {
            int GetCachePosition()
            {
                return (int) result.BaseStream.Position;
            }

            var stream = new ModelExporterStream(result);

            var index = new Dictionary<string, int>();
            index.Add(MyImporterConstants.TAG_DUMMIES, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_DUMMIES, (Dictionary<string, MyModelDummy>) tagData[MyImporterConstants.TAG_DUMMIES]);
            index.Add(MyImporterConstants.TAG_VERTICES, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_VERTICES, (Vector3[]) tagData[MyImporterConstants.TAG_VERTICES]);
            index.Add(MyImporterConstants.TAG_NORMALS, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_NORMALS, (Byte4[]) tagData[MyImporterConstants.TAG_NORMALS]);
            index.Add(MyImporterConstants.TAG_TEXCOORDS0, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_TEXCOORDS0, (HalfVector2[]) tagData[MyImporterConstants.TAG_TEXCOORDS0]);
            index.Add(MyImporterConstants.TAG_BINORMALS, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_BINORMALS, (Byte4[]) tagData[MyImporterConstants.TAG_BINORMALS]);
            index.Add(MyImporterConstants.TAG_TANGENTS, GetCachePosition());
            stream.ExportData(MyImporterConstants.TAG_TANGENTS, (Byte4[]) tagData[MyImporterConstants.TAG_TANGENTS]);
            index.Add("TexCoords1", GetCachePosition());
            stream.ExportData("TexCoords1", (HalfVector2[]) tagData["TexCoords1"]);
            index.Add("RescaleFactor", GetCachePosition());
            stream.ExportFloat("RescaleFactor", (float) tagData["RescaleFactor"]);
            index.Add("UseChannelTextures", GetCachePosition());
            stream.ExportBool("UseChannelTextures", (bool) tagData["UseChannelTextures"]);
            index.Add("BoundingBox", GetCachePosition());
            stream.ExportData("BoundingBox", (BoundingBox) tagData["BoundingBox"]);
            index.Add("BoundingSphere", GetCachePosition());
            stream.ExportData("BoundingSphere", (BoundingSphere) tagData["BoundingSphere"]);
            index.Add("SwapWindingOrder", GetCachePosition());
            stream.ExportBool("SwapWindingOrder", (bool) tagData["SwapWindingOrder"]);
            index.Add("MeshParts", GetCachePosition());
            stream.ExportData("MeshParts", (List<MyMeshPartInfo>) tagData["MeshParts"]);
            index.Add("Sections", GetCachePosition());
            stream.ExportData("Sections", (List<MyMeshSectionInfo>) tagData["Sections"]);
            index.Add("ModelBvh", GetCachePosition());
            stream.ExportData("ModelBvh", (byte[]) tagData["ModelBvh"]); // GImpactQuantizedBVH as byte[]
            index.Add("ModelInfo", GetCachePosition());
            stream.ExportData("ModelInfo", (MyModelInfo) tagData["ModelInfo"]);
            index.Add("BlendIndices", GetCachePosition());
            stream.ExportData("BlendIndices", (Vector4I[]) tagData["BlendIndices"]);
            index.Add("BlendWeights", GetCachePosition());
            stream.ExportData("BlendWeights", (Vector4[]) tagData["BlendWeights"]);
            index.Add("Animations", GetCachePosition());
            stream.ExportData("Animations", (ModelAnimations) tagData["Animations"]);
            index.Add("Bones", GetCachePosition());
            stream.ExportData("Bones", (MyModelBone[]) tagData["Bones"]);
            index.Add("BoneMapping", GetCachePosition());
            stream.ExportData("BoneMapping", (Vector3I[]) tagData["BoneMapping"]);
            index.Add("HavokCollisionGeometry", GetCachePosition());
            stream.ExportData("HavokCollisionGeometry", (byte[]) tagData["HavokCollisionGeometry"]);
            index.Add("PatternScale", GetCachePosition());
            stream.ExportFloat("PatternScale", (float) tagData["PatternScale"]);
            index.Add("LODs", GetCachePosition());
            stream.ExportData("LODs", (MyLODDescriptor[]) tagData["LODs"]);
            if (tagData.ContainsKey("FBXHash"))
            {
                index.Add("FBXHash", GetCachePosition());
                stream.ExportData("FBXHash", (Hashing.Hash128) tagData["FBXHash"]);
            }

            if (tagData.ContainsKey("HKTHash"))
            {
                index.Add("HKTHash", GetCachePosition());
                stream.ExportData("HKTHash", (Hashing.Hash128) tagData["HKTHash"]);
            }

            if (tagData.ContainsKey("XMLHash"))
            {
                index.Add("XMLHash", GetCachePosition());
                stream.ExportData("XMLHash", (Hashing.Hash128) tagData["XMLHash"]);
            }

            return index;
        }

        public static void ExportModelData(BinaryWriter result, Dictionary<string, object> tagData)
        {
            var stream = new ModelExporterStream(result);
            var list = new List<string>((string[]) tagData["Debug"]);
            list.RemoveAll((string x) => x.Contains("Version:"));
            list.Add(string.Format("Version:{0}", 1157002));
            stream.ExportData("Debug", list.ToArray());

            using (var cacheStream = new Equinox76561198048419394.Core.Util.Memory.MemoryStream(1024 * 1024))
            using (var cacheWriter = new BinaryWriter(cacheStream))
            {
                stream.WriteIndexDictionary(ExportTags(cacheWriter, tagData));
                result.BaseStream.Write(cacheStream.GetBuffer(), 0, (int) cacheStream.Length);
            }
        }

        public struct ModelExporterStream
        {
            private readonly BinaryWriter _writer;


            public ModelExporterStream(BinaryWriter writer)
            {
                _writer = writer;
            }

            private int CalculateIndexSize(Dictionary<string, int> dict)
            {
                var num = 4;
                foreach (var keyValuePair in dict)
                {
                    num += Encoding.ASCII.GetByteCount(keyValuePair.Key) + 1;
                    num += 4;
                }

                return num;
            }

            public void WriteIndexDictionary(Dictionary<string, int> dict)
            {
                var num = (int) _writer.BaseStream.Position;
                var num2 = CalculateIndexSize(dict);
                _writer.Write(dict.Count);
                foreach (var keyValuePair in AdaptDictionary(dict))
                {
                    _writer.Write(keyValuePair.Key);
                    _writer.Write(keyValuePair.Value + num2 + num);
                }
            }

            private void WriteTag(string tagName)
            {
                _writer.Write(tagName);
            }

            private void WriteVector(Vector2 vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
            }

            private void WriteVector(Vector3 vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
                _writer.Write(vct.Z);
            }

            private void WriteVector(Vector4 vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
                _writer.Write(vct.Z);
                _writer.Write(vct.W);
            }

            private void WriteMatrix(Matrix matrix)
            {
                _writer.Write(matrix.M11);
                _writer.Write(matrix.M12);
                _writer.Write(matrix.M13);
                _writer.Write(matrix.M14);
                _writer.Write(matrix.M21);
                _writer.Write(matrix.M22);
                _writer.Write(matrix.M23);
                _writer.Write(matrix.M24);
                _writer.Write(matrix.M31);
                _writer.Write(matrix.M32);
                _writer.Write(matrix.M33);
                _writer.Write(matrix.M34);
                _writer.Write(matrix.M41);
                _writer.Write(matrix.M42);
                _writer.Write(matrix.M43);
                _writer.Write(matrix.M44);
            }

            private void WriteVector(Vector2I vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
            }

            private void WriteVector(Vector3I vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
                _writer.Write(vct.Z);
            }

            private void WriteVector(Vector4I vct)
            {
                _writer.Write(vct.X);
                _writer.Write(vct.Y);
                _writer.Write(vct.Z);
                _writer.Write(vct.W);
            }

            private void WriteVector(HalfVector4 val)
            {
                _writer.Write(val.PackedValue);
            }

            private void WriteVector(HalfVector2 val)
            {
                _writer.Write(val.PackedValue);
            }

            private void WriteByte4(Byte4 val)
            {
                _writer.Write(val.PackedValue);
            }

            public bool ExportDataPackedAsHV4(string tagName, Vector3[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var vector in vctArr)
                {
                    WriteVector(VF_Packer.PackPosition(vector));
                }

                return true;
            }

            public bool ExportData(string tagName, HalfVector4[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var val in vctArr)
                {
                    WriteVector(val);
                }

                return true;
            }

            public bool ExportData(string tagName, Byte4[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var val in vctArr)
                {
                    WriteByte4(val);
                }

                return true;
            }

            public bool ExportDataPackedAsB4(string tagName, Vector3[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var vector in vctArr)
                {
                    WriteByte4(new Byte4
                    {
                        PackedValue = VF_Packer.PackNormal(vector)
                    });
                }

                return true;
            }

            public bool ExportDataPackedAsHV2(string tagName, Vector2[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var vector in vctArr)
                {
                    var val = new HalfVector2(vector);
                    WriteVector(val);
                }

                return true;
            }

            public bool ExportData(string tagName, HalfVector2[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var val in vctArr)
                {
                    WriteVector(val);
                }

                return true;
            }

            public bool ExportData(string tagName, Vector3[] vctArr)
            {
                if (vctArr == null)
                {
                    return true;
                }

                WriteTag(tagName);
                _writer.Write(vctArr.Length);
                foreach (var vct in vctArr)
                {
                    WriteVector(vct);
                }

                return true;
            }

            public bool ExportData(string tagName, Vector3I[] vctArr)
            {
                if (vctArr == null)
                {
                    return true;
                }

                WriteTag(tagName);
                _writer.Write(vctArr.Length);
                foreach (var vct in vctArr)
                {
                    WriteVector(vct);
                }

                return true;
            }

            public bool ExportData(string tagName, Vector4I[] vctArr)
            {
                if (vctArr == null)
                {
                    return true;
                }

                WriteTag(tagName);
                _writer.Write(vctArr.Length);
                foreach (var vct in vctArr)
                {
                    WriteVector(vct);
                }

                return true;
            }

            public bool ExportData(string tagName, Matrix[] matArr)
            {
                if (matArr == null)
                {
                    return true;
                }

                WriteTag(tagName);
                _writer.Write(matArr.Length);
                foreach (var matrix in matArr)
                {
                    WriteMatrix(matrix);
                }

                return true;
            }

            public bool ExportData(string tagName, Vector2[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var vct in vctArr)
                {
                    WriteVector(vct);
                }

                return true;
            }

            public bool ExportData(string tagName, Vector4[] vctArr)
            {
                WriteTag(tagName);
                if (vctArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(vctArr.Length);
                foreach (var vct in vctArr)
                {
                    WriteVector(vct);
                }

                return true;
            }

            public bool ExportData(string tagName, string[] strArr)
            {
                WriteTag(tagName);
                if (strArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(strArr.Length);
                foreach (var value in strArr)
                {
                    _writer.Write(value);
                }

                return true;
            }

            public bool ExportData(string tagName, int[] intArr)
            {
                WriteTag(tagName);
                if (intArr == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(intArr.Length);
                foreach (var value in intArr)
                {
                    _writer.Write(value);
                }

                return true;
            }

            public bool ExportData(string tagName, byte[] byteArray)
            {
                WriteTag(tagName);
                if (byteArray == null)
                {
                    _writer.Write(0);
                    return true;
                }

                _writer.Write(byteArray.Length);
                _writer.Write(byteArray);
                return true;
            }

            public bool ExportData(string tagName, MyModelInfo modelInfo)
            {
                WriteTag(tagName);
                _writer.Write(modelInfo.TrianglesCount);
                _writer.Write(modelInfo.VerticesCount);
                WriteVector(modelInfo.BoundingBoxSize);
                return true;
            }

            public bool ExportData(string tagName, BoundingBox boundingBox)
            {
                WriteTag(tagName);
                WriteVector(boundingBox.Min);
                WriteVector(boundingBox.Max);
                return true;
            }

            public bool ExportData(string tagName, BoundingSphere boundingSphere)
            {
                WriteTag(tagName);
                WriteVector(boundingSphere.Center);
                _writer.Write(boundingSphere.Radius);
                return true;
            }

            public bool ExportData(string tagName, Dictionary<string, Matrix> dict)
            {
                WriteTag(tagName);
                _writer.Write(dict.Count);
                foreach (var keyValuePair in AdaptDictionary(dict))
                {
                    _writer.Write(keyValuePair.Key);
                    WriteMatrix(keyValuePair.Value);
                }

                return true;
            }

            public bool ExportData(string tagName, List<MyMeshPartInfo> list)
            {
                WriteTag(tagName);
                _writer.Write(list.Count);
                foreach (var myMeshPartInfo in list)
                {
                    myMeshPartInfo.Export(_writer);
                }

                return true;
            }

            public bool ExportData(string tagName, List<MyMeshSectionInfo> list)
            {
                WriteTag(tagName);
                _writer.Write(list.Count);
                foreach (var myMeshSectionInfo in list)
                {
                    myMeshSectionInfo.Export(_writer);
                }

                return true;
            }

            public bool ExportData(string tagName, Dictionary<string, MyModelDummy> dict)
            {
                WriteTag(tagName);
                _writer.Write(dict.Count);
                foreach (var keyValuePair in AdaptDictionary(dict))
                {
                    _writer.Write(keyValuePair.Key);
                    WriteMatrix(keyValuePair.Value.Matrix);
                    _writer.Write(keyValuePair.Value.CustomData.Count);
                    foreach (var keyValuePair2 in AdaptDictionary(keyValuePair.Value.CustomData))
                    {
                        _writer.Write(keyValuePair2.Key);
                        _writer.Write(keyValuePair2.Value.ToString());
                    }
                }

                return true;
            }

            public bool ExportFloat(string tagName, float value)
            {
                WriteTag(tagName);
                _writer.Write(value);
                return true;
            }

            public bool ExportBool(string tagName, bool value)
            {
                WriteTag(tagName);
                _writer.Write(value);
                return true;
            }

            public void Write(MyAnimationClip clip)
            {
                _writer.Write(clip.Name);
                _writer.Write(clip.Duration);
                _writer.Write(clip.Bones.Count);
                foreach (var bone in clip.Bones)
                {
                    _writer.Write(bone.Name);
                    _writer.Write(bone.Keyframes.Count);
                    foreach (var keyframe in bone.Keyframes)
                    {
                        _writer.Write(keyframe.Time);
                        WriteQuaternion(keyframe.Rotation);
                        WriteVector(keyframe.Translation);
                    }
                }
            }

            private void WriteQuaternion(Quaternion q)
            {
                _writer.Write(q.X);
                _writer.Write(q.Y);
                _writer.Write(q.Z);
                _writer.Write(q.W);
            }

            public bool ExportData(string tagName, ModelAnimations modelAnimations)
            {
                WriteTag(tagName);
                _writer.Write(modelAnimations.Clips.Count);
                foreach (var clip in modelAnimations.Clips)
                {
                    Write(clip);
                }

                _writer.Write(modelAnimations.Skeleton.Count);
                foreach (var value in modelAnimations.Skeleton)
                {
                    _writer.Write(value);
                }

                return true;
            }

            public bool ExportData(string tagName, MyModelBone[] bones)
            {
                WriteTag(tagName);
                _writer.Write(bones.Length);
                foreach (var myModelBone in bones)
                {
                    _writer.Write(myModelBone.Name);
                    _writer.Write(myModelBone.Parent);
                    WriteMatrix(myModelBone.Transform);
                }

                return true;
            }

            public bool ExportData(string tagName, MyLODDescriptor[] lodDescriptions)
            {
                WriteTag(tagName);
                _writer.Write(lodDescriptions.Length);
                for (var i = 0; i < lodDescriptions.Length; i++)
                {
                    lodDescriptions[i].Write(_writer);
                }

                return true;
            }

            public void ExportData(string tagName, Hashing.Hash128 hash)
            {
                WriteTag(tagName);
                _writer.Write(hash.V0);
                _writer.Write(hash.V1);
            }
        }
    }
}