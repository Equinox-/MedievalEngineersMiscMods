using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Equinox76561198048419394.Core.Controller;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.ModelGenerator.ModelIO;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.Library.Collections.Concurrent;
using VRage.Library.Threading;
using VRage.Logging;
using VRage.Session;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Animations;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    [MySessionComponent(AlwaysOn = true, AllowAutomaticCreation = true)]
    public class DerivedModelManager : MySessionComponent
    {
        private static NamedLogger _log = new NamedLogger(nameof(DerivedModelManager), MyLog.Default);
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        // ReSharper disable once ConvertToConstant.Local
        private static bool _useCaching = true;
        
        private const int DerivedVersion = 2;
        private const int BvhVersion = 1;
        private const int MaterialModelVersion = 2;
        
        private static readonly ConcurrentBag<byte[]> Buffers = new ConcurrentBag<byte[]>();
        private readonly FastResourceLock _lock = new FastResourceLock();
        private readonly Dictionary<Hashing.Hash128, string> _derivedModels = new Dictionary<Hashing.Hash128, string>();
        private readonly ConcurrentDictionary<string, Hashing.Hash128> _modelHash = new ConcurrentDictionary<string, Hashing.Hash128>();
        private readonly ConcurrentDictionary<string, MaterialBvh> _originalModelsBvh = new ConcurrentDictionary<string, MaterialBvh>();

        private readonly FastResourceLock _materialsByModelLock = new FastResourceLock();
        private readonly Dictionary<string, InterningBag<string>> _materialsByModel = new Dictionary<string, InterningBag<string>>();
        private readonly MyConcurrentPool<ModelImporter> _modelImporterPool = new MyConcurrentPool<ModelImporter>(0, (importer) => importer.Clear());

        public string CreateModel(string baseModel, MaterialEditsBuilder editor)
        {
            var finalHash = ComputeHash(baseModel, editor);
            if (!finalHash.HasValue)
                return baseModel;
            using (_lock.AcquireSharedUsing())
                if (_derivedModels.TryGetValue(finalHash.Value, out var modifiedModel))
                    return modifiedModel;
            using (_lock.AcquireExclusiveUsing())
                return CreateDerivedModelInternal(finalHash, baseModel, editor);
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            var readmeDerived = $"{DerivedModelPrefix}_0ReadMe.txt";
            var readmeBvh = $"{ModelBvhPrefix}_0ReadMe.txt";
            var readmeMtl = $"{MaterialModelPrefix}_0ReadMe.txt";
            var utils = (IMyUtilities) MyAPIUtilities.Static;
            using (var writer = utils.WriteFileInGlobalStorage(readmeDerived))
            {
                writer.WriteLine("== Equinox Core Model Generator Cache ==");
                writer.WriteLine("This folder contains all the models Equinox Core has generated at runtime with texture variants");
                writer.WriteLine($"and block modifiers.  All the files that start with {DerivedModelPrefix} can safely be deleted");
                writer.WriteLine("as any that are still in use will be regenerated on demand.");
                writer.WriteLine();
                writer.WriteLine("In some cases it may make sense for the mod author to include these inside their mod to reduce");
                writer.WriteLine("the amount of runtime generation required.  Simply copy the files relevant to your mod into");
                writer.WriteLine("Models/Generated inside your mod and they will no longer be automatically generated. NOTE: ");
                writer.WriteLine("you will have to copy these files again if you change the source model, as the hash will have changed");
                writer.WriteLine();
                writer.WriteLine($"The current version is {DerivedVersion}.  Anything that uses a lower version number can be deleted.");
            }

            using (var writer = utils.WriteFileInGlobalStorage(readmeBvh))
            {
                writer.WriteLine("== Equinox Core Model BVH Cache ==");
                writer.WriteLine("This folder contains all the model BVHs Equinox Core has generated at runtime");
                writer.WriteLine("to provide material-I-am-looking-at functionality");
                writer.WriteLine($"All the files that start with {ModelBvhPrefix} can be safely deleted and");
                writer.WriteLine("any that are still in use will be regenerated on demand.");
                writer.WriteLine();
                writer.WriteLine($"The current version is {BvhVersion}.  Anything that uses a lower version number can be deleted.");
            }

            using (var writer = utils.WriteFileInGlobalStorage(readmeMtl))
            {
                writer.WriteLine("== Equinox Core Model Material Cache ==");
                writer.WriteLine("This folder contains all the single material models Equinox Core has generated at runtime");
                writer.WriteLine("to allow for loading of arbitrary materials for usage with runtime models");
                writer.WriteLine($"All the files that start with {MaterialModelPrefix} can be safely deleted and");
                writer.WriteLine("any that are still in use will be regenerated on demand.");
                writer.WriteLine();
                writer.WriteLine($"The current version is {MaterialModelVersion}.  Anything that uses a lower version number can be deleted.");
            }
        }

        private Hashing.Hash128 GetModelHash(string model)
        {
            return _modelHash.GetOrAdd(model, (modelPath) =>
            {
                var hb = Hashing.Builder();
                if (!Buffers.TryTake(out var buffer))
                    buffer = new byte[1024];
                using (var stream = OpenModelReader(modelPath))
                {
                    while (true)
                    {
                        var count = stream.Read(buffer, 0, buffer.Length);
                        if (count <= 0)
                            break;
                        for (var i = 0; i < count; i++)
                            hb.Add(buffer[i]);
                    }
                }

                Buffers.Add(buffer);

                var hash = hb.Build();
                if (DebugFlags.Debug(typeof(DerivedModelManager)))
                    _log.Info($"Generated model hash for {model} = {hash}");
                return hash;
            });
        }

        private Hashing.Hash128? ComputeHash(string baseModel, MaterialEditsBuilder editor)
        {
            var hashBuilder = Hashing.Builder();
            hashBuilder.Add(GetModelHash(baseModel));
            using (PoolManager.Get(out List<MaterialEdit> edits))
            {
                foreach (var mtl in GetMaterialsForModel(baseModel))
                {
                    edits.Clear();
                    editor.Get(mtl, edits);
                    if (edits.Count <= 0)
                        continue;
                    hashBuilder.Add(mtl);
                    foreach (var edit in edits)
                        hashBuilder.Add(in edit.Hash);
                }
            }

            return hashBuilder.Build();
        }

        private string CreateDerivedModelInternal(Hashing.Hash128? key, string rawModel, MaterialEditsBuilder builder, bool isTranslatingLod = false)
        {
            var finalHash = key ?? ComputeHash(rawModel, builder);
            if (!finalHash.HasValue)
                return rawModel;

            if (_derivedModels.TryGetValue(finalHash.Value, out var modifiedModel))
                return modifiedModel;
            try
            {
                var resolvedPath = ResolveGeneratedModel(rawModel, finalHash.Value, out var existing);
                if (existing && _useCaching)
                {
                    if (DebugFlags.Debug(typeof(DerivedModelManager)))
                        _log.Info($"Loading derived model of {rawModel} with {builder} from {resolvedPath}");
                    return _derivedModels[finalHash.Value] = resolvedPath;
                }

                using (PoolManager.Get(out Dictionary<string, string> materialMapping))
                using (PoolManager.Get(out Dictionary<string, string> materialMappingInverse))
                {
                    using (ReadModel(rawModel, out var tags))
                    {
                        var meshParts = (List<MyMeshPartInfo>) tags[MyImporterConstants.TAG_MESH_PARTS];
                        using (PoolManager.Get(out List<MaterialEdit> edits))
                        using (PoolManager.Get(out HashSet<string> usedMaterials))
                        {
                            string SafeMaterialName(string baseName)
                            {
                                var safeName = baseName;
                                for (var i = 0; !usedMaterials.Add(safeName); i++)
                                {
                                    safeName = $"{baseName}`{i}";
                                }
                                return safeName;
                            }

                            foreach (var part in meshParts)
                            {
                                var originalMaterialName = part.m_MaterialDesc.MaterialName;
                                edits.Clear();
                                builder.Get(originalMaterialName, edits);
                                if (edits.Count == 0)
                                {
                                    var safeName = SafeMaterialName(part.m_MaterialDesc.MaterialName);
                                    if (safeName != part.m_MaterialDesc.MaterialName)
                                    {
                                        part.m_MaterialDesc = part.m_MaterialDesc.Clone(safeName);
                                        part.m_MaterialHash = part.m_MaterialDesc.MaterialName.GetHashCode();
                                    }
                                    continue;
                                }

                                foreach (var edit in edits)
                                    edit.ApplyTo(part.m_MaterialDesc);

                                var newMaterialName = SafeMaterialName(GenerateMaterialName(part.m_MaterialDesc));
                                materialMapping.Add(originalMaterialName, newMaterialName);
                                materialMappingInverse.Add(newMaterialName, originalMaterialName);
                                part.m_MaterialDesc = part.m_MaterialDesc.Clone(newMaterialName);
                                part.m_MaterialHash = part.m_MaterialDesc.MaterialName.GetHashCode();
                            }

                            foreach (var orphan in GetMaterialsForModelInternal(rawModel))
                                if (usedMaterials.Add(orphan))
                                {
                                    this.GetLogger().Info($"Including dummy mesh part for destruction material {orphan}");
                                    var tmp = new MyMaterialDescriptor(orphan);
                                    edits.Clear();
                                    builder.Get(orphan, edits);
                                    foreach (var e in edits)
                                        e.ApplyTo(tmp);
                                    var newMaterialName = GenerateMaterialName(tmp);
                                    if (!usedMaterials.Add(newMaterialName))
                                        continue;
                                    meshParts.Add(new MyMeshPartInfo
                                    {
                                        m_indices = new List<int> {0, 0, 0}, // single degenerate tris
                                        m_MaterialDesc = tmp.Clone(newMaterialName),
                                        m_MaterialHash = newMaterialName.GetHashCode(),
                                        Technique = MyMeshDrawTechnique.MESH
                                    });

                                    materialMapping[orphan] = newMaterialName;
                                }

                            if (!isTranslatingLod)
                            {
                                var lods = (MyLODDescriptor[]) tags[MyImporterConstants.TAG_LODS];
                                var resultLods = new List<MyLODDescriptor>();
                                foreach (var lod in lods)
                                {
                                    var res = CreateDerivedModelInternal(null, lod.Model, builder, true);
                                    if (!res.Equals(lod.Model) && !res.Equals(lod.Model + ".mwm"))
                                    {
                                        lod.Model = res;
                                        resultLods.Add(lod);
                                    }
                                }

                                tags[MyImporterConstants.TAG_LODS] = resultLods.ToArray();
                            }

                            Save(resolvedPath, finalHash.Value, materialMapping, tags);
                        }
                    }
                    if (DebugFlags.Debug(typeof(DerivedModelManager)))
                        _log.Info($"Generated derived model of {rawModel} with {builder} at {resolvedPath}");

                    return _derivedModels[finalHash.Value] = resolvedPath;
                }
            }
            catch (Exception e)
            {
                this.GetLogger().Error($"Failed to create derived model from {rawModel} {e}");
                _derivedModels[finalHash.Value] = rawModel;
                return rawModel;
            }
        }

        private const string DerivedModelPrefix = "derived";
        private const string ModelBvhPrefix = "bvh";
        private const string MaterialModelPrefix = "mtl";

        /// <summary>
        /// Determines the content-relative path of the given derived model. 
        /// </summary>
        protected virtual string ResolveGeneratedModel(string rawModel, Hashing.Hash128 key, out bool existing)
        {
            if (rawModel.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase))
                rawModel = rawModel.Substring(0, rawModel.Length - 4);
            var fileName = $"{DerivedModelPrefix}_v{DerivedVersion}_{TrimAndSanitize(rawModel)}_{key.ToString()}.mwm";

            var modCachedPath = Path.Combine("Models/Generated", fileName);
            if (MyAPIUtilities.Static.ContentFileExists(modCachedPath))
            {
                existing = true;
                return modCachedPath;
            }


            var api = (IMyUtilities) MyAPIUtilities.Static;
            var absolutePath = Path.Combine(((IMyGamePaths) MyAPIUtilities.Static).UserDataPath, "Storage", fileName);
            if (api.FileExistsInGlobalStorage(fileName))
            {
                existing = true;
                return absolutePath;
            }

            existing = false;
            return absolutePath;
        }

        protected virtual string GenerateMaterialName(MyMaterialDescriptor desc)
        {
            return ComputeHash(desc).ToString();
        }

        protected Hashing.Hash128 ComputeHash(MyMaterialDescriptor desc)
        {
            var hb = Hashing.Builder();
            hb.Add(desc.MaterialName);
            foreach (var tex in desc.Textures)
            {
                hb.Add(tex.Key);
                hb.Add(tex.Value);
            }

            foreach (var prop in desc.UserData)
            {
                hb.Add(prop.Key);
                hb.Add(prop.Value);
            }

            hb.Add(desc.Technique);
            hb.Add(desc.GlassCW);
            hb.Add(desc.GlassCCW);
            hb.Add(desc.GlassSmoothNormals ? 1L : 0L);
            return hb.Build();
        }

        protected virtual void Save(string rawPath, Hashing.Hash128 hash, Dictionary<string, string> materialNameMapping, Dictionary<string, object> tags)
        {
            var api = (IMyUtilities) MyAPIUtilities.Static;
            using (var writer = api.WriteBinaryFileInGlobalStorage(Path.GetFileName(rawPath)))
                ModelExporter.ExportModelData(writer, tags);
        }

        #region Triangle BVH

        public MaterialBvh GetMaterialBvh(string modelPath)
        {
            return _originalModelsBvh.GetOrAdd(modelPath, (rawPath) =>
            {
                var modelHash = GetModelHash(modelPath);
                var bvhName = $"{ModelBvhPrefix}_v{BvhVersion}_{TrimAndSanitize(modelPath)}_{modelHash}.ebvh";
                var utils = (IMyUtilities) MyAPIUtilities.Static;
                MaterialBvh bvh;

                var modCachedPath = Path.Combine("Models/Generated", bvhName);
                if (MyAPIUtilities.Static.ContentFileExists(modCachedPath) && _useCaching)
                {
                    try
                    {
                        using (var stream = utils.ReadContentFile(modCachedPath))
                        using (var reader = new BinaryReader(stream))
                        {
                            MaterialBvh.Serializer.Read(reader, out bvh);
                            if (DebugFlags.Debug(typeof(DerivedModelManager)))
                                _log.Info($"Loaded material BVH of {modelPath} from mod cache {modCachedPath}");
                            return bvh;
                        }
                    }
                    catch (Exception e)
                    {
                        this.GetLogger().Warning($"Failed to deserialize cached BVH at {bvhName} for {modelPath}, recreating.  {e}");
                    }
                }

                if (utils.FileExistsInGlobalStorage(bvhName) && _useCaching)
                {
                    try
                    {
                        using (var reader = utils.ReadBinaryFileInGlobalStorage(bvhName))
                        {
                            MaterialBvh.Serializer.Read(reader, out bvh);
                            if (DebugFlags.Debug(typeof(DerivedModelManager)))
                                _log.Info($"Loaded material BVH of {modelPath} from local cache {bvhName}");
                            return bvh;
                        }
                    }
                    catch (Exception e)
                    {
                        this.GetLogger().Warning($"Failed to deserialize cached BVH at {bvhName} for {modelPath}, recreating.  {e}");
                    }
                }

                using (ReadModel(rawPath, out var tags))
                    bvh = MaterialBvh.Create(tags);

                try
                {
                    using (var reader = utils.WriteBinaryFileInGlobalStorage(bvhName))
                    {
                        MaterialBvh.Serializer.Write(reader, in bvh);
                    }
                    if (DebugFlags.Debug(typeof(DerivedModelManager)))
                        _log.Info($"Generated material BVH of {modelPath} and stored in local cache {bvhName}");
                }
                catch (Exception e)
                {
                    this.GetLogger().Warning($"Failed to serialize cached BVH at {bvhName} for {modelPath}.  {e}");
                }

                return bvh;
            });
        }

        #endregion

        #region Single Material Models
        private readonly HashSet<string> _preparedMaterials = new HashSet<string>();
        public void PrepareMaterial(MyMaterialDescriptor descriptor)
        {
            var id = descriptor.MaterialName;
            if (_preparedMaterials.Contains(id))
                return;
            var path = ResolveMaterialModel(id, out var existing);
            if (!existing)
                GenerateMaterialModel(path, descriptor);

            MyRenderProxy.PreloadModel(path);
            MyRenderProxy.PreloadModel(path, forceOldPipeline: true);
            _preparedMaterials.Add(id);
        }

        private static readonly Byte4[] SingleMaterialModelNormals = { default, default, default };

        private readonly DictionaryReader<string, object> _singleMaterialModel = new Dictionary<string, object>
        {
            [MyImporterConstants.TAG_DEBUG] = Array.Empty<string>(),
            [MyImporterConstants.TAG_DUMMIES] = new Dictionary<string, MyModelDummy>(),
            [MyImporterConstants.TAG_VERTICES] = new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero },
            [MyImporterConstants.TAG_NORMALS] = SingleMaterialModelNormals,
            [MyImporterConstants.TAG_TEXCOORDS0] = new[] { default(HalfVector2), default, default },
            [MyImporterConstants.TAG_BINORMALS] = SingleMaterialModelNormals,
            [MyImporterConstants.TAG_TANGENTS] = SingleMaterialModelNormals,
            [MyImporterConstants.TAG_TEXCOORDS1] = Array.Empty<HalfVector2>(),
            [MyImporterConstants.TAG_RESCALE_FACTOR] = 1f,
            [MyImporterConstants.TAG_USE_CHANNEL_TEXTURES] = false,
            [MyImporterConstants.TAG_BOUNDING_BOX] = default(BoundingBox),
            [MyImporterConstants.TAG_BOUNDING_SPHERE] = default(BoundingSphere),
            [MyImporterConstants.TAG_SWAP_WINDING_ORDER] = false,
            // MeshParts
            [MyImporterConstants.TAG_MESH_SECTIONS] = new List<MyMeshSectionInfo>(),
            [MyImporterConstants.TAG_MODEL_BVH] = Array.Empty<byte>(),
            [MyImporterConstants.TAG_MODEL_INFO] = new MyModelInfo(1, 3, default),
            [MyImporterConstants.TAG_BLENDINDICES] = Array.Empty<Vector4I>(),
            [MyImporterConstants.TAG_BLENDWEIGHTS] = Array.Empty<Vector4>(),
            [MyImporterConstants.TAG_ANIMATIONS] = new ModelAnimations(),
            [MyImporterConstants.TAG_BONES] = Array.Empty<MyModelBone>(),
            [MyImporterConstants.TAG_BONE_MAPPING] = Array.Empty<Vector3I>(),
            [MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY] = Array.Empty<byte>(),
            [MyImporterConstants.TAG_PATTERN_SCALE] = 1f,
            [MyImporterConstants.TAG_LODS] = Array.Empty<MyLODDescriptor>(),
        };

        private void GenerateMaterialModel(string path, MyMaterialDescriptor material)
        {
            var tags = new Dictionary<string, object>(_singleMaterialModel.Count + 1);
            foreach (var kv in _singleMaterialModel)
                tags.Add(kv.Key, kv.Value);
            tags[MyImporterConstants.TAG_MESH_PARTS] = new List<MyMeshPartInfo>
            {
                new MyMeshPartInfo
                {
                    m_MaterialHash = material.MaterialName.GetHashCode(),
                    m_MaterialDesc = material,
                    m_indices = new List<int> { 0, 1, 2 },
                    Technique = material.TechniqueEnum,
                }
            };
            var api = (IMyUtilities)MyAPIUtilities.Static;
            using (var writer = api.WriteBinaryFileInGlobalStorage(Path.GetFileName(path)))
                ModelExporter.ExportModelData(writer, tags);
        }

        private string ResolveMaterialModel(string id, out bool existing)
        {
            var fileName = $"{MaterialModelPrefix}_v{MaterialModelVersion}_{TrimAndSanitize(id)}.mwm";
            var modCachedPath = Path.Combine("Models/Generated", fileName);
            if (MyAPIUtilities.Static.ContentFileExists(modCachedPath))
            {
                existing = true;
                return modCachedPath;
            }

            var api = (IMyUtilities) MyAPIUtilities.Static;
            var absolutePath = Path.Combine(((IMyGamePaths) MyAPIUtilities.Static).UserDataPath, "Storage", fileName);
            if (api.FileExistsInGlobalStorage(fileName))
            {
                existing = true;
                return absolutePath;
            }

            existing = false;
            return absolutePath;
        }
        
        #endregion

        #region Materials Per Model

        public InterningBag<string> GetMaterialsForModel(string model)
        {
            using (_materialsByModelLock.AcquireSharedUsing())
                if (_materialsByModel.TryGetValue(model, out var materials))
                    return materials;
            using (_materialsByModelLock.AcquireExclusiveUsing())
                return GetMaterialsForModelInternal(model);
        }

        // These materials are conditionally included in a model based on if the strings show up in the destruction data
        private static readonly HashSet<string> DestructionMaterials = new HashSet<string>
        {
            "LightWood_01_Edges",
            "DamagedCobblestones_V1",
            "DarkWood",
            "DarkWood_Edges",
            "DarkWood_01_Edges",
            "SemiRoughWood_Dark",
            "LightWood_01",
            "WoodEnding",
            "SemiRoughWood_Dark_Edges",
            "WoodEnding3",
            "Tree_Cutout",
            "WoodEnding4"
        };

        protected static bool ByteStringEquals(byte[] raw, int offset, string search)
        {
            if (offset + search.Length + 1 >= raw.Length)
                return false;
            for (var i = 0; i < search.Length; i++)
                if (raw[offset + i] != (byte) search[i])
                    return false;
            return raw[offset + search.Length] != '.' && raw[offset + search.Length] != '_';
        }


        private static readonly ConcurrentDictionary<string, BoyerMooreHelper> Helpers = new ConcurrentDictionary<string, BoyerMooreHelper>();

        protected struct BoyerMooreHelper
        {
            public readonly byte[] _pattern;
            public readonly byte[] _jumpTable;

            public BoyerMooreHelper(byte[] find)
            {
                _pattern = find;
                _jumpTable = new byte[256];
                var pl = (byte) find.Length;
                for (var index = 0; index < 256; index++)
                    _jumpTable[index] = pl;
                for (var index = 0; index < pl - 1; index++)
                    _jumpTable[_pattern[index]] = (byte) (pl - index - 1);
            }
        }

        protected static void ByteSequenceReplace(byte[] raw, string find, string replace)
        {
            ByteSequenceReplace(raw, Helpers.GetOrAdd(find, (str) => new BoyerMooreHelper(Encoding.ASCII.GetBytes(find))), Encoding.ASCII.GetBytes(replace));
        }

        protected static void ByteSequenceReplace(byte[] raw, BoyerMooreHelper find, byte[] replace)
        {
            var pl = find._pattern.Length;
            var index = 0;
            var limit = raw.Length - pl;
            var patternLengthMinusOne = pl - 1;
            while (index <= limit)
            {
                var j = patternLengthMinusOne;
                while (j >= 0 && find._pattern[j] == raw[index + j])
                    j--;
                if (j < 0)
                    Array.Copy(replace, 0, raw, index, replace.Length);
                index += Math.Max(find._jumpTable[raw[index + j]] - pl + 1 + j, 1);
            }
        }

        protected static bool ByteSequenceContains(byte[] raw, string find)
        {
            return ByteSequenceContains(raw, Helpers.GetOrAdd(find, (str) => new BoyerMooreHelper(Encoding.ASCII.GetBytes(find))));
        }

        protected static bool ByteSequenceContains(byte[] raw, BoyerMooreHelper find)
        {
            var pl = find._pattern.Length;
            var index = 0;
            var limit = raw.Length - pl;
            var patternLengthMinusOne = pl - 1;
            while (index <= limit)
            {
                var j = patternLengthMinusOne;
                while (j >= 0 && find._pattern[j] == raw[index + j])
                    j--;
                if (j < 0)
                    return true;
                index += Math.Max(find._jumpTable[raw[index + j]] - pl + 1 + j, 1);
            }

            return false;
        }

        protected static void WriteByteString(byte[] raw, int offset, string value)
        {
            for (var i = 0; i < value.Length; i++)
                raw[offset + i] = (byte) value[i];
        }

        private InterningBag<string> GetMaterialsForModelInternal(string model, bool isTranslatingLod = false)
        {
            if (_materialsByModel.TryGetValue(model, out var materialsCurrent))
                return materialsCurrent;
            using (PoolManager.Get(out HashSet<string> materials))
            using (PoolManager.Get(out HashSet<string> destructionMaterials))
            {
                try
                {
                    using (ReadModel(model, out var tags))
                    {
                        if (tags.TryGetValue(MyImporterConstants.TAG_MESH_PARTS, out var meshPartsRaw) && meshPartsRaw is List<MyMeshPartInfo> meshParts)
                        {
                            foreach (var part in meshParts)
                                materials.Add(part.GetMaterialName());
                        }

                        if (!isTranslatingLod && tags.TryGetValue(MyImporterConstants.TAG_LODS, out var lodsRaw) && lodsRaw is MyLODDescriptor[] lods)
                        {
                            foreach (var lod in lods)
                            foreach (var material in GetMaterialsForModelInternal(lod.Model, true))
                                materials.Add(material);
                        }

                        if (!isTranslatingLod && tags.TryGetValue(MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY, out var havokData))
                        {
                            var raw = (byte[]) havokData;
                            foreach (var str in DestructionMaterials)
                                if (ByteSequenceContains(raw, str))
                                {
                                    materials.Add(str);
                                    destructionMaterials.Add(str);
                                    break;
                                }
                        }
                    }
                }
                catch
                {
                    // Ignore
                }

                var result = InterningBag<string>.Of(materials);
                var resultDestruction = InterningBag<string>.Of(destructionMaterials);
                var k2 = !model.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase) ? (model + ".mwm") : model.Substring(0, model.Length - 4);
                _materialsByModel[k2] = result;
                _materialsByModel[model] = result;
                return result;
            }
        }

        private BinaryReader OpenModelReader(string model)
        {
            if (!model.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase))
                model += ".mwm";
            var utils = (IMyUtilities) MyAPIUtilities.Static;
            var paths = (IMyGamePaths) MyAPIUtilities.Static;
            if (!model.StartsWith(paths.UserDataPath))
                return new BinaryReader(utils.ReadContentFile(model));
            var strippedPath = model.Substring(paths.UserDataPath.Length + 1 + "Storage".Length + 1);
            return utils.ReadBinaryFileInGlobalStorage(strippedPath);
        }

        private ReturnHandle<ModelImporter> ReadModel(string model, out Dictionary<string, object> tags)
        {
            var handle = _modelImporterPool.GetHandle();
            using (var reader = OpenModelReader(model))
                handle.Handle.ImportData(reader);
            tags = handle.Handle.GetTagData();
            return handle;
        }

        private static string TrimAndSanitize(string modelName)
        {
            if (modelName.Length > 16)
                modelName = modelName.Substring(modelName.Length - 16, 16);
            return modelName.AsAlphaNumeric();
        }

        #endregion
    }
}