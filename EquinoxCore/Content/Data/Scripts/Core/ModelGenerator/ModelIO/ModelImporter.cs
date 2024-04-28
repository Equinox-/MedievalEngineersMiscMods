// Copyright Keen Software House
// With modifications by Equinox
// Decompiled with JetBrains decompiler
// Type: VRageRender.Import.ModelImporter
// Assembly: VRage.Render, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: A825C30A-738C-4918-85D1-2C0DD17577F8

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Equinox76561198048419394.Core.Util.EqMath;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Animations;
using VRageRender.Import;
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable InconsistentNaming
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable ConvertToConstant.Global
#pragma warning disable CS0169 // Field is never used

namespace Equinox76561198048419394.Core.ModelGenerator.ModelIO
{
    public class ModelImporter
    {
        private static Dictionary<string, ITagReader> TagReaders = new Dictionary<string, ITagReader>()
        {
            {
                "Vertices",
                new PositionReader()
            },
            {
                "Normals",
                new TagReader<Byte4[]>(ReadArrayOfByte4)
            },
            {
                "TexCoords0",
                new TagReader<HalfVector2[]>(
                    ReadArrayOfHalfVector2)
            },
            {
                "Binormals",
                new TagReader<Byte4[]>(ReadArrayOfByte4)
            },
            {
                "Tangents",
                new TagReader<Byte4[]>(ReadArrayOfByte4)
            },
            {
                MyImporterConstants.TAG_TEXCOORDS1,
                new TagReader<HalfVector2[]>(
                    ReadArrayOfHalfVector2)
            },
            {
                MyImporterConstants.TAG_USE_CHANNEL_TEXTURES,
                new TagReader<bool>(x => x.ReadBoolean())
            },
            {
                MyImporterConstants.TAG_BOUNDING_BOX,
                new TagReader<BoundingBox>(ReadBoundingBox)
            },
            {
                MyImporterConstants.TAG_BOUNDING_SPHERE,
                new TagReader<BoundingSphere>(
                    ReadBoundingSphere)
            },
            {
                MyImporterConstants.TAG_RESCALE_FACTOR,
                new TagReader<float>(x => x.ReadSingle())
            },
            {
                MyImporterConstants.TAG_SWAP_WINDING_ORDER,
                new TagReader<bool>(x => x.ReadBoolean())
            },
            {
                "Dummies",
                new TagReader<Dictionary<string, MyModelDummy>>(
                    ReadDummies)
            },
            {
                "MeshParts",
                new TagReader<List<MyMeshPartInfo>>(
                    ReadMeshParts)
            },
            {
                MyImporterConstants.TAG_MESH_SECTIONS,
                new TagReader<List<MyMeshSectionInfo>>(
                    ReadMeshSections)
            },
            {
                MyImporterConstants.TAG_MODEL_BVH,
                new TagReader<byte[]>(ReadArrayOfBytes) // Normalized GImpactQuantizedBvh
            },
            {
                MyImporterConstants.TAG_MODEL_INFO,
                new TagReader<MyModelInfo>(reader =>
                    new MyModelInfo(reader.ReadInt32(), reader.ReadInt32(), ImportVector3(reader)))
            },
            {
                MyImporterConstants.TAG_BLENDINDICES,
                new TagReader<Vector4I[]>(
                    ReadArrayOfVector4Int)
            },
            {
                MyImporterConstants.TAG_BLENDWEIGHTS,
                new TagReader<Vector4[]>(ReadArrayOfVector4)
            },
            {
                MyImporterConstants.TAG_ANIMATIONS,
                new TagReader<ModelAnimations>(
                    ReadAnimations)
            },
            {
                MyImporterConstants.TAG_BONES,
                new TagReader<MyModelBone[]>(ReadBones)
            },
            {
                MyImporterConstants.TAG_BONE_MAPPING,
                new TagReader<Vector3I[]>(
                    ReadArrayOfVector3Int)
            },
            {
                MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY,
                new TagReader<byte[]>(ReadArrayOfBytes)
            },
            {
                MyImporterConstants.TAG_PATTERN_SCALE,
                new TagReader<float>(x => x.ReadSingle())
            },
            {
                MyImporterConstants.TAG_LODS,
                new TagReader<MyLODDescriptor[]>(
                    ReadLODs)
            },
            {
                "HavokDestructionGeometry",
                new TagReader<byte[]>(ReadArrayOfBytes)
            },
            {
                "HavokDestruction",
                new TagReader<byte[]>(ReadArrayOfBytes)
            },
            {
                "FBXHash",
                new TagReader<Hashing.Hash128>(ReadHash)
            },
            {
                "HKTHash",
                new TagReader<Hashing.Hash128>(ReadHash)
            },
            {
                "XMLHash",
                new TagReader<Hashing.Hash128>(ReadHash)
            }
        };

        public static bool USE_LINEAR_KEYFRAME_REDUCTION = true;
        public static bool NORMALIZE_QUATERNIONS = true;
        private Dictionary<string, object> m_retTagData = new Dictionary<string, object>();
        private int m_version = 0;
        private static string m_debugAssetName;
        private const float TinyLength = 1E-08f;
        private const float TinyCosAngle = 0.9999999f;

        public int DataVersion => m_version;

        public Dictionary<string, object> GetTagData()
        {
            return m_retTagData;
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3()
            {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Z = reader.ReadSingle()
            };
        }

        private static HalfVector4 ReadHalfVector4(BinaryReader reader)
        {
            return new HalfVector4()
            {
                PackedValue = reader.ReadUInt64()
            };
        }

        private static HalfVector2 ReadHalfVector2(BinaryReader reader)
        {
            return new HalfVector2()
            {
                PackedValue = reader.ReadUInt32()
            };
        }

        private static Byte4 ReadByte4(BinaryReader reader)
        {
            return new Byte4()
            {
                PackedValue = reader.ReadUInt32()
            };
        }

        private static Vector3 ImportVector3(BinaryReader reader)
        {
            Vector3 vector3;
            vector3.X = reader.ReadSingle();
            vector3.Y = reader.ReadSingle();
            vector3.Z = reader.ReadSingle();
            return vector3;
        }

        private static Vector4 ImportVector4(BinaryReader reader)
        {
            Vector4 vector4;
            vector4.X = reader.ReadSingle();
            vector4.Y = reader.ReadSingle();
            vector4.Z = reader.ReadSingle();
            vector4.W = reader.ReadSingle();
            return vector4;
        }

        private static Quaternion ImportQuaternion(BinaryReader reader)
        {
            Quaternion quaternion;
            quaternion.X = reader.ReadSingle();
            quaternion.Y = reader.ReadSingle();
            quaternion.Z = reader.ReadSingle();
            quaternion.W = reader.ReadSingle();
            return quaternion;
        }

        private static Vector4I ImportVector4Int(BinaryReader reader)
        {
            Vector4I vector4I;
            vector4I.X = reader.ReadInt32();
            vector4I.Y = reader.ReadInt32();
            vector4I.Z = reader.ReadInt32();
            vector4I.W = reader.ReadInt32();
            return vector4I;
        }

        private static Vector3I ImportVector3Int(BinaryReader reader)
        {
            Vector3I vector3I;
            vector3I.X = reader.ReadInt32();
            vector3I.Y = reader.ReadInt32();
            vector3I.Z = reader.ReadInt32();
            return vector3I;
        }

        private static Vector2 ImportVector2(BinaryReader reader)
        {
            Vector2 vector2;
            vector2.X = reader.ReadSingle();
            vector2.Y = reader.ReadSingle();
            return vector2;
        }

        private static HalfVector4[] ReadArrayOfHalfVector4(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var halfVector4Array = new HalfVector4[length];
            for (var index = 0; index < length; ++index)
                halfVector4Array[index] = ReadHalfVector4(reader);
            return halfVector4Array;
        }

        private static Byte4[] ReadArrayOfByte4(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var byte4Array = new Byte4[length];
            for (var index = 0; index < length; ++index)
                byte4Array[index] = ReadByte4(reader);
            return byte4Array;
        }

        private static HalfVector2[] ReadArrayOfHalfVector2(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var halfVector2Array = new HalfVector2[length];
            for (var index = 0; index < length; ++index)
                halfVector2Array[index] = ReadHalfVector2(reader);
            return halfVector2Array;
        }

        private static Vector3[] ReadArrayOfVector3(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var vector3Array = new Vector3[length];
            for (var index = 0; index < length; ++index)
                vector3Array[index] = ImportVector3(reader);
            return vector3Array;
        }

        private static Vector4[] ReadArrayOfVector4(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var vector4Array = new Vector4[length];
            for (var index = 0; index < length; ++index)
                vector4Array[index] = ImportVector4(reader);
            return vector4Array;
        }

        private static Vector4I[] ReadArrayOfVector4Int(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var vector4IArray = new Vector4I[length];
            for (var index = 0; index < length; ++index)
                vector4IArray[index] = ImportVector4Int(reader);
            return vector4IArray;
        }

        private static Vector3I[] ReadArrayOfVector3Int(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var vector3IArray = new Vector3I[length];
            for (var index = 0; index < length; ++index)
                vector3IArray[index] = ImportVector3Int(reader);
            return vector3IArray;
        }

        private static Vector2[] ReadArrayOfVector2(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var vector2Array = new Vector2[length];
            for (var index = 0; index < length; ++index)
                vector2Array[index] = ImportVector2(reader);
            return vector2Array;
        }

        private static string[] ReadArrayOfString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var strArray = new string[length];
            for (var index = 0; index < length; ++index)
                strArray[index] = reader.BaseStream.ReadString(Encoding.ASCII);
            return strArray;
        }

        private static BoundingBox ReadBoundingBox(BinaryReader reader)
        {
            BoundingBox boundingBox;
            boundingBox.Min = ImportVector3(reader);
            boundingBox.Max = ImportVector3(reader);
            return boundingBox;
        }

        private static BoundingSphere ReadBoundingSphere(BinaryReader reader)
        {
            BoundingSphere boundingSphere;
            boundingSphere.Center = ImportVector3(reader);
            boundingSphere.Radius = reader.ReadSingle();
            return boundingSphere;
        }

        private static Matrix ReadMatrix(BinaryReader reader)
        {
            Matrix matrix;
            matrix.M11 = reader.ReadSingle();
            matrix.M12 = reader.ReadSingle();
            matrix.M13 = reader.ReadSingle();
            matrix.M14 = reader.ReadSingle();
            matrix.M21 = reader.ReadSingle();
            matrix.M22 = reader.ReadSingle();
            matrix.M23 = reader.ReadSingle();
            matrix.M24 = reader.ReadSingle();
            matrix.M31 = reader.ReadSingle();
            matrix.M32 = reader.ReadSingle();
            matrix.M33 = reader.ReadSingle();
            matrix.M34 = reader.ReadSingle();
            matrix.M41 = reader.ReadSingle();
            matrix.M42 = reader.ReadSingle();
            matrix.M43 = reader.ReadSingle();
            matrix.M44 = reader.ReadSingle();
            return matrix;
        }

        private static List<MyMeshPartInfo> ReadMeshParts(
            BinaryReader reader,
            int version)
        {
            var myMeshPartInfoList = new List<MyMeshPartInfo>();
            var num = reader.ReadInt32();
            for (var index = 0; index < num; ++index)
            {
                var myMeshPartInfo = new MyMeshPartInfo();
                myMeshPartInfo.Import(reader, version);
                myMeshPartInfoList.Add(myMeshPartInfo);
            }

            return myMeshPartInfoList;
        }

        private static List<MyMeshSectionInfo> ReadMeshSections(
            BinaryReader reader,
            int version)
        {
            var myMeshSectionInfoList = new List<MyMeshSectionInfo>();
            var num = reader.ReadInt32();
            for (var index = 0; index < num; ++index)
            {
                var myMeshSectionInfo = new MyMeshSectionInfo();
                myMeshSectionInfo.Import(reader, version);
                myMeshSectionInfoList.Add(myMeshSectionInfo);
            }

            return myMeshSectionInfoList;
        }

        private static Dictionary<string, MyModelDummy> ReadDummies(
            BinaryReader reader)
        {
            var dictionary = new Dictionary<string, MyModelDummy>();
            var num1 = reader.ReadInt32();
            for (var index1 = 0; index1 < num1; ++index1)
            {
                var key1 = reader.BaseStream.ReadString(Encoding.ASCII);
                var matrix = ReadMatrix(reader);
                var myModelDummy = new MyModelDummy();
                myModelDummy.Name = key1;
                myModelDummy.Matrix = matrix;
                myModelDummy.CustomData = new Dictionary<string, object>();
                var num2 = reader.ReadInt32();
                for (var index2 = 0; index2 < num2; ++index2)
                {
                    var key2 = reader.BaseStream.ReadString(Encoding.ASCII);
                    var str = reader.BaseStream.ReadString(Encoding.ASCII);
                    myModelDummy.CustomData.Add(key2, str);
                }

                dictionary.Add(key1, myModelDummy);
            }

            return dictionary;
        }

        private static int[] ReadArrayOfInt(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var numArray = new int[length];
            for (var index = 0; index < length; ++index)
                numArray[index] = reader.ReadInt32();
            return numArray;
        }

        private static byte[] ReadArrayOfBytes(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            return reader.ReadBytes(count);
        }

        private static Hashing.Hash128 ReadHash(BinaryReader reader)
        {
            return new Hashing.Hash128(reader.ReadUInt64(), reader.ReadUInt64());
        }

        private static MyAnimationClip ReadClip(BinaryReader reader)
        {
            var myAnimationClip = new MyAnimationClip();
            myAnimationClip.Name = reader.BaseStream.ReadString(Encoding.ASCII);
            myAnimationClip.Duration = reader.ReadDouble();
            var num1 = reader.ReadInt32();
            while (num1-- > 0)
            {
                var bone = new MyAnimationClip.Bone();
                bone.Name = reader.BaseStream.ReadString(Encoding.ASCII);
                var num2 = reader.ReadInt32();
                while (num2-- > 0)
                {
                    var keyframe = new MyAnimationClip.Keyframe();
                    keyframe.Time = reader.ReadDouble();
                    keyframe.Rotation = ImportQuaternion(reader);
                    if (NORMALIZE_QUATERNIONS)
                        keyframe.Rotation.Normalize();
                    keyframe.Translation = ImportVector3(reader);
                    bone.Keyframes.Add(keyframe);
                }

                myAnimationClip.Bones.Add(bone);
                var count = bone.Keyframes.Count;
                var num3 = 0;
                if (count > 3)
                {
                    if (USE_LINEAR_KEYFRAME_REDUCTION)
                    {
                        var linkedList = new LinkedList<MyAnimationClip.Keyframe>();
                        foreach (var keyframe in bone.Keyframes)
                            linkedList.AddLast(keyframe);
                        LinearKeyframeReduction(linkedList, 1E-08f, 0.9999999f);
                        bone.Keyframes.Clear();
                        bone.Keyframes.AddCollection<MyAnimationClip.Keyframe>(
                            linkedList.ToArray<MyAnimationClip.Keyframe>());
                        num3 = bone.Keyframes.Count;
                    }
                }

                CalculateKeyframeDeltas(bone.Keyframes);
            }

            return myAnimationClip;
        }

        private static void PercentageKeyframeReduction(
            LinkedList<MyAnimationClip.Keyframe> keyframes,
            float ratio)
        {
            if (keyframes.Count < 3)
                return;
            var num1 = 0.0f;
            var num2 = (int) (keyframes.Count * (double) ratio);
            if (num2 == 0)
                return;
            var num3 = num2 / (float) keyframes.Count;
            var node = keyframes.First.Next;
            while (true)
            {
                var next = node.Next;
                if (next != null)
                {
                    if (num1 >= 1.0)
                    {
                        for (; (double) num1 >= 1.0; --num1)
                        {
                            keyframes.Remove(node);
                            node = next;
                            next = node.Next;
                        }
                    }
                    else
                        node = next;

                    num1 += num3;
                }
                else
                    break;
            }
        }

        private static void LinearKeyframeReduction(
            LinkedList<MyAnimationClip.Keyframe> keyframes,
            float translationThreshold,
            float rotationThreshold)
        {
            if (keyframes.Count < 3)
                return;
            var node = keyframes.First.Next;
            while (true)
            {
                var next = node.Next;
                if (next != null)
                {
                    var keyframe1 = node.Previous.Value;
                    var keyframe2 = node.Value;
                    var keyframe3 = next.Value;
                    var amount = (float) ((node.Value.Time - node.Previous.Value.Time) / (next.Value.Time - node.Previous.Value.Time));
                    var vector3 = Vector3.Lerp(keyframe1.Translation, keyframe3.Translation, amount);
                    var quaternion1 = Quaternion.Slerp(keyframe1.Rotation, keyframe3.Rotation, amount);
                    if ((vector3 - keyframe2.Translation).LengthSquared() < (double) translationThreshold &&
                        Quaternion.Dot(quaternion1, keyframe2.Rotation) > (double) rotationThreshold)
                        keyframes.Remove(node);
                    node = next;
                }
                else
                    break;
            }
        }

        private static void CalculateKeyframeDeltas(List<MyAnimationClip.Keyframe> keyframes)
        {
            for (var index = 1; index < keyframes.Count; ++index)
            {
                var keyframe1 = keyframes[index - 1];
                var keyframe2 = keyframes[index];
                // Debug.Assert(keyframe1.Time < keyframe2.Time, "Incorrect keyframes timing!");
                keyframe2.InvTimeDiff = 1.0 / (keyframe2.Time - keyframe1.Time);
            }
        }

        private static ModelAnimations ReadAnimations(BinaryReader reader)
        {
            var num1 = reader.ReadInt32();
            var modelAnimations = new ModelAnimations();
            while (num1-- > 0)
            {
                var myAnimationClip = ReadClip(reader);
                modelAnimations.Clips.Add(myAnimationClip);
            }

            var num2 = reader.ReadInt32();
            while (num2-- > 0)
            {
                var num3 = reader.ReadInt32();
                modelAnimations.Skeleton.Add(num3);
            }

            return modelAnimations;
        }

        private static MyModelBone[] ReadBones(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            var myModelBoneArray = new MyModelBone[length];
            var index = 0;
            while (length-- > 0)
            {
                var myModelBone = new MyModelBone();
                myModelBoneArray[index] = myModelBone;
                myModelBone.Name = reader.BaseStream.ReadString(Encoding.ASCII);
                myModelBone.Index = index++;
                myModelBone.Parent = reader.ReadInt32();
                myModelBone.Transform = ReadMatrix(reader);
            }

            return myModelBoneArray;
        }

        private static MyLODDescriptor[] ReadLODs(BinaryReader reader, int version)
        {
            var length = reader.ReadInt32();
            var myLodDescriptorArray = new MyLODDescriptor[length];
            var num = 0;
            while (length-- > 0)
            {
                var myLodDescriptor = new MyLODDescriptor();
                myLodDescriptorArray[num++] = myLodDescriptor;
                myLodDescriptor.Read(reader);
            }

            return myLodDescriptorArray;
        }

        public void ImportData(BinaryReader reader, string[] tags = null)
        {
            Clear();
            LoadTagData(reader, tags);
        }

        public void Clear()
        {
            m_retTagData.Clear();
            m_version = 0;
        }

        private void LoadTagData(BinaryReader reader, string[] tags)
        {
            var key = reader.BaseStream.ReadString(Encoding.ASCII);
            var strArray = ReadArrayOfString(reader);
            m_retTagData.Add(key, strArray);
            var oldValue = "Version:";
            if (strArray.Length != 0 && strArray[0].Contains(oldValue))
                m_version = Convert.ToInt32(strArray[0].Replace(oldValue, ""));
            if (m_version >= 1066002)
            {
                var dictionary = ReadIndexDictionary(reader);
                if (tags == null)
                    tags = dictionary.Keys.ToArray<string>();
                foreach (var tag in tags)
                {
                    if (dictionary.ContainsKey(tag))
                    {
                        var num = dictionary[tag];
                        reader.BaseStream.Seek(num, SeekOrigin.Begin);
                        var str = reader.BaseStream.ReadString(Encoding.ASCII);
                        // Debug.Assert(tag == str, "Wrong model data (version mismatch?)");
                        if (TagReaders.ContainsKey(tag))
                            m_retTagData.Add(tag, TagReaders[tag].Read(reader, m_version));
                    }
                }
            }
            else
                LoadOldVersion(reader);
        }

        private Dictionary<string, int> ReadIndexDictionary(BinaryReader reader)
        {
            var dictionary = new Dictionary<string, int>();
            var num1 = reader.ReadInt32();
            for (var index = 0; index < num1; ++index)
            {
                var key = reader.BaseStream.ReadString(Encoding.ASCII);
                var num2 = reader.ReadInt32();
                dictionary.Add(key, num2);
            }

            return dictionary;
        }

        private void LoadOldVersion(BinaryReader reader)
        {
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadDummies(reader));
            var key1 = reader.BaseStream.ReadString(Encoding.ASCII);
            var halfVector4Array = ReadArrayOfHalfVector4(reader);
            var vector3Array = new Vector3[halfVector4Array.Length];
            for (var index = 0; index < vector3Array.Length; ++index)
                vector3Array[index] = (Vector3) halfVector4Array[index].ToVector4();
            m_retTagData.Add(key1, vector3Array);
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfByte4(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfHalfVector2(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfByte4(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfByte4(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfHalfVector2(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadBoolean());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadSingle());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadSingle());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadBoolean());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadBoolean());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadSingle());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadSingle());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadBoundingBox(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadBoundingSphere(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadBoolean());
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadMeshParts(reader, m_version));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfBytes(reader)); // GImpactQuantizedBvh
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII),
                new MyModelInfo(reader.ReadInt32(), reader.ReadInt32(), ImportVector3(reader)));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfVector4Int(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfVector4(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadAnimations(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadBones(reader));
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfVector3Int(reader));
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfBytes(reader));
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), reader.ReadSingle());
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadLODs(reader, 1066002));
            if (reader.BaseStream.Position < reader.BaseStream.Length)
                m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfBytes(reader));
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return;
            m_retTagData.Add(reader.BaseStream.ReadString(Encoding.ASCII), ReadArrayOfBytes(reader));
        }

        private interface ITagReader
        {
            object Read(BinaryReader reader, int version);
        }

        private struct TagReader<T> : ITagReader
        {
            private Func<BinaryReader, int, T> m_tagReader;

            public TagReader(Func<BinaryReader, T> tagReader)
            {
                m_tagReader = (x, y) => tagReader(x);
            }

            public TagReader(Func<BinaryReader, int, T> tagReader)
            {
                m_tagReader = tagReader;
            }

            private T ReadTag(BinaryReader reader, int version)
            {
                return m_tagReader(reader, version);
            }

            public object Read(BinaryReader reader, int version)
            {
                return ReadTag(reader, version);
            }
        }

        public struct ReductionInfo
        {
            public string BoneName;
            public int OriginalKeys;
            public int OptimizedKeys;
        }

        private class PositionReader : ITagReader
        {
            public object Read(BinaryReader reader, int version)
            {
                var length = reader.ReadInt32();
                var vector3Array = new Vector3[length];
                if (version >= 1157002)
                {
                    for (var index = 0; index < length; ++index)
                        vector3Array[index] = ImportVector3(reader);
                }
                else
                {
                    for (var index = 0; index < length; ++index)
                        vector3Array[index] = (Vector3) ReadHalfVector4(reader).ToVector4();
                }

                return vector3Array;
            }
        }
    }
}