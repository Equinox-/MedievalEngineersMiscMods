using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.ModelGenerator.ModelIO;
using Equinox76561198048419394.Core.Util.EqMath;
using Havok;
using VRage.FileSystem;
using VRage.Library.Collections;
using VRage.Logging;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public class ModelAssetTranslator : DerivedModelManager
    {
        private readonly string _exportContentRoot;
        private readonly string _exportPrefix;

        public ModelAssetTranslator(string root, string prefix)
        {
            _exportContentRoot = root;
            _exportPrefix = prefix;


            HkBaseSystem.Init(new NamedLogger(NullLogger.Instance));
        }

        protected override void Save(string rawPath, Hashing.Hash128 hash, Dictionary<string, string> materialNameMapping, Dictionary<string, object> tags)
        {
            using (PoolManager.Get(out HashSet<string> shapeNames))
            {
                if (tags.TryGetValue(MyImporterConstants.TAG_HAVOK_DESTRUCTION, out var destructionBufferIn) ||
                    tags.TryGetValue(MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY, out destructionBufferIn))
                {
                    using (var hkWorld = new HkWorld(true, 50000, float.MaxValue, false, 4, 0.6f))
                    {
                        hkWorld.MarkForWrite();
                        hkWorld.DestructionWorld = new HkdWorld(hkWorld);
                        hkWorld.UnmarkForWrite();
                        using (var destructionStorage = new HkDestructionStorage(hkWorld.DestructionWorld))
                        {
                            var result = destructionStorage.LoadDestructionDataFromBuffer((byte[]) destructionBufferIn)[0];
                            CollectShapeNamesRecursive(result, shapeNames);
//                            RenameShapesRecursive(result, "_" + hash);
//                            var rawOutput = Path.GetTempFileName();
//                            destructionStorage.SaveDestructionData(result, rawOutput);
//                            var rawBytes = File.ReadAllBytes(rawOutput);
//                            FixupMaterialReferences(rawBytes, materialNameMapping);
//                            tags[MyImporterConstants.TAG_HAVOK_DESTRUCTION] = rawBytes;
//                            File.Delete(rawOutput);
                        }
                    }
                }

                if (tags.TryGetValue(MyImporterConstants.TAG_HAVOK_COLLISION_GEOMETRY, out var havokCollision))
                {
                    using (PoolManager.Get(out Dictionary<string, string> dict))
                    {
                        foreach (var kv in materialNameMapping)
                            dict[kv.Key] = kv.Value;
                        foreach (var name in shapeNames)
                        {
                            var hb = Hashing.Builder();
                            hb.Add(name);
                            hb.Add(hash);
                            dict[name] = hb.Build().ToLimitedString(name.Length);
                        }

                        FixupMaterialReferences((byte[]) havokCollision, dict);
                    }
                }
            }

            var fullPath = Path.Combine(_exportContentRoot, rawPath);
            using (var writer = new BinaryWriter(File.Open(fullPath, FileMode.Create)))
                ModelExporter.ExportModelData(writer, tags);
        }

        private static void FixupMaterialReferences(byte[] rawBytes, Dictionary<string, string> mtls)
        {
            foreach (var kv in mtls.OrderByDescending(x => x.Key.Length))
            {
                System.Diagnostics.Debug.Assert(kv.Key.Length == kv.Value.Length);
                ByteSequenceReplace(rawBytes, kv.Key, kv.Value);
            }
        }

        protected override string GenerateMaterialName(MyMaterialDescriptor desc)
        {
            return ComputeHash(desc).ToLimitedString(desc.MaterialName.Length);
        }

        protected override string ResolveGeneratedModel(string rawModel, Hashing.Hash128 key, out bool existing)
        {
            if (rawModel.StartsWith(MyFileSystem.ContentPath, StringComparison.OrdinalIgnoreCase))
                rawModel = rawModel.Substring(MyFileSystem.ContentPath.Length + 1);
            if (rawModel.StartsWith("Models", StringComparison.OrdinalIgnoreCase))
                rawModel = rawModel.Substring("Models".Length + 1);

            if (rawModel.EndsWith(".mwm"))
                rawModel = rawModel.Substring(0, rawModel.Length - 4);

            var contentPath = Path.Combine("Models", _exportPrefix, rawModel + "_" + key + ".mwm");
            var fullPath = Path.Combine(_exportContentRoot, contentPath);
            if (File.Exists(fullPath))
            {
                existing = true;
                return contentPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            existing = false;
            return contentPath;
        }

        private static void CollectShapeNamesRecursive(HkdBreakableShape shape, HashSet<string> shapeName)
        {
            shapeName.Add(shape.Name);
            using (PoolManager.Get(out List<HkdShapeInstanceInfo> shapes))
            {
                shape.GetChildren(shapes);
                foreach (var child in shapes)
                    CollectShapeNamesRecursive(child.Shape, shapeName);
            }
        }

        private static void RenameShapesRecursive(HkdBreakableShape shape, string shapeSuffix)
        {
            shape.Name += shapeSuffix;
            using (PoolManager.Get(out List<HkdShapeInstanceInfo> shapes))
            {
                shape.GetChildren(shapes);
                foreach (var child in shapes)
                    RenameShapesRecursive(child.Shape, shapeSuffix);
            }
        }
    }
}