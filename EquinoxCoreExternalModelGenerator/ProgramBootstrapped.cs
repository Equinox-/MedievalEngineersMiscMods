using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Havok;
using Medieval.Definitions.Block;
using Medieval.ObjectBuilders.Definitions.Block;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Game.Localization;
using VRage.Collections;
using VRage.Collections.Concurrent;
using VRage.Components;
using VRage.Engine;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Input;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.Meta;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Definitions.Block;
using VRage.ParallelWorkers;
using VRage.Serialization.Xml;
using VRage.Systems;
using VRageRender;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public class ProgramBootstrapped
    {
        public static void MainBootstrap(string[] args)
        {
            var rootContentPath = args[0];
            MyLog.Default = new MyLog();
            MyFileSystem.Init(rootContentPath, "./");
            MyLanguage.Init();
            MyRenderProxy.Initialize(new MyNullRender());
            MyLog.Default.Init("converter.log", new StringBuilder());
            Workers.Init(new WorkerConfigurationFactory()
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Background,
                    Min = 1,
                    Priority = ThreadPriority.BelowNormal,
                    Ratio = .1f
                })
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Logic,
                    Min = 1,
                    Priority = ThreadPriority.Normal,
                    Ratio = .7f
                })
                .AddGroup(new WorkerConfigurationFactory.Group
                {
                    Id = WorkerGroup.Render,
                    Min = 1,
                    Priority = ThreadPriority.AboveNormal,
                    Ratio = .2f
                })
                .SetDefault(WorkerGroup.Logic)
                .Bake(32));

            MyMetadataSystem.LoadAssemblies(new[]
            {
                "VRage",
                "VRage.Game",
                "Sandbox.Graphics",
                "Sandbox.Game",
                "MedievalEngineers.ObjectBuilders",
                "MedievalEngineers.Game"
            }.Select(Assembly.Load));

            var config = (BlockVariantGeneratorConfig) new XmlSerializer(typeof(BlockVariantGeneratorConfig)).Deserialize(File.OpenText(args[1]));

            Console.WriteLine("Loading definitions...");

            MyDefinitionManagerSandbox.Static.LoadData(new ListReader<MyModContext>(new List<MyModContext>(config.ContentRoots.Select(contentPath =>
                new MyModContext(Path.GetFileNameWithoutExtension(contentPath), Path.GetFileNameWithoutExtension(contentPath), contentPath)))));
            MyFileSystem.SetAdditionalContentPaths(config.ContentRoots);
            Console.WriteLine("Processing definitions...");
            DefinitionObLoader.LoadObjectBuilders(MyDefinitionManagerSandbox.Static.DefinitionSet);

            Console.WriteLine("Translating definitions...");
            var translatedSet = new DefinitionObSet();
            var modelTranslator = new ModelAssetTranslator(config.OutputDirectory, config.VariantName);
            var changesDictionary = new Dictionary<string, MyObjectBuilder_EquiModifierChangeMaterialDefinition.MaterialModifier>();
            foreach (var edit in config.Changes)
                changesDictionary[edit.Name] = edit;

            string AssetTranslator(string asset)
            {
                if (string.IsNullOrWhiteSpace(asset)) return asset;
                if (config.AssetTranslations.TryGetValue(asset, out var assetTranslation)) return assetTranslation;
                if (asset.EndsWith(".mwm", StringComparison.OrdinalIgnoreCase) && config.Changes != null)
                {
                    if (asset.Contains("GeneratedStoneEdge")) Debugger.Break();
                    var modelMaterials = modelTranslator.GetMaterialsForModel(asset);
                    using (PoolManager.Get(out List<MyObjectBuilder_EquiModifierChangeMaterialDefinition.MaterialModifier> changes))
                    {
                        foreach (var material in modelMaterials)
                            if (changesDictionary.TryGetValue(material.Name, out var change))
                                changes.Add(change);
                        if (changes.Count > 0)
                        {
                            Console.WriteLine("Translating model " + asset);
                            using (var builder = MaterialEditsBuilder.Allocate())
                            {
                                foreach (var k in changes)
                                {
                                    var list = new List<MaterialEdit>();
                                    k.GetChanges(list);
                                    builder.Add(k.Name, list);
                                }

                                return modelTranslator.CreateModel(asset, builder);
                            }
                        }
                    }
                }

                return asset;
            }

            var translator = new DefinitionObTranslator(
                DefinitionObLoader.Loaded,
                translatedSet,
                AssetTranslator,
                (displayName) => $"{displayName} ({config.VariantName})",
                "_" + config.VariantName,
                config.Translations);
            foreach (var id in config.DefinitionsToTranslate)
            {
                if (id.SubtypeName == "**any**")
                {
                    // Find and translate ALL with the given type ID, not forcing
                    foreach (var k in DefinitionObLoader.Loaded.AllDefinitions)
                        if (k.Id.TypeId == id.TypeId)
                        {
                            translator.Translate(k);
                        }
                }
                else if (id.SubtypeName == "**any_translated_model**")
                {
                    var ids = new HashSet<MyDefinitionId>();
                    foreach (var k in DefinitionObLoader.Loaded.AllDefinitions)
                        if (k.Id.TypeId == id.TypeId && k is MyObjectBuilder_PhysicalModelDefinition physModel)
                        {
                            var originalModel = physModel.Model;
                            var translatedModel = AssetTranslator(originalModel);
                            if (!originalModel.Equals(translatedModel))
                            {
                                ids.Add(k.Id);
                            }
                        }

                    // Include variants that contain the blocks
                    foreach (var k in DefinitionObLoader.Loaded.AllDefinitions.OfType<MyObjectBuilder_BlockVariantsDefinition>())
                    {
                        var good = false;
                        foreach (var e in k.Blocks)
                            if (ids.Contains(e))
                            {
                                good = true;
                                break;
                            }

                        if (good)
                            ids.Add(k.Id);
                    }

                    foreach (var sid in ids)
                    foreach (var def in DefinitionObLoader.Loaded.GetDefinitions(sid))
                        translator.Translate(def, true);
                }
                else
                {
                    var list = DefinitionObLoader.Loaded.GetDefinitions(id);
                    if (list.Count == 0)
                        Console.WriteLine("Couldn't find any definitions with ID " + id);
                    foreach (var def in list)
                    {
                        translator.Translate(def, true);
                    }
                }
            }

            var definitionSet = new MyObjectBuilder_Definitions
            {
                Definitions = new MySerializableList<MyObjectBuilder_DefinitionBase>(translatedSet.AllDefinitions.OrderBy(x => x.TypeId.ToString())
                    .ThenBy(x => x.SubtypeName))
            };

            XDocument doc;
            using (var baseWriter = new StringWriter())
            {
                var serializer = new XmlSerializer(typeof(MyObjectBuilder_Definitions));
                using (var writer = new XmlTextWriter(baseWriter))
                    serializer.Serialize(writer, definitionSet);
                doc = XDocument.Load(new StringReader(baseWriter.ToString()));
            }

            NullCleaner.Clean(doc);
            var translatedSbcPath = Path.Combine(config.OutputDirectory, "Data/Translated.sbc");
            Directory.CreateDirectory(Path.GetDirectoryName(translatedSbcPath));
            using (var writer = new XmlTextWriter(translatedSbcPath, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Indentation = 2
            })

                doc.WriteTo(writer);

            MyLog.Default.Dispose();
        }
    }
}