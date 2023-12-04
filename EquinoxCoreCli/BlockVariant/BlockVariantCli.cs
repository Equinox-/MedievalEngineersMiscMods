using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using CommandLine;
using Equinox76561198048419394.Core.Cli.Def;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using VRage.Game;
using VRage.Library.Collections;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Definitions.Block;

namespace Equinox76561198048419394.Core.Cli.BlockVariant
{
    public static class BlockVariantCli
    {
        [Verb("gen-block-variants", HelpText = "Generate additional block variants using material substitution")]
        public class Options : SharedOptions
        {
            [Option]
            public string ConfigFile { get; set; }
            
            public override int Run() => BlockVariantCli.Run(this);
        }

        public static int Run(Options options)
        {
            var configFile = options.ConfigFile;
            if (configFile == null && !FileFinder.TryFindInParents("setup.xml", out configFile))
            {
                Console.WriteLine("Can't find setup.xml file.");
                return 1;
            }
            
            var config = (BlockVariantGeneratorConfig) new XmlSerializer(typeof(BlockVariantGeneratorConfig)).Deserialize(File.OpenText(configFile));

            var definitions = DefinitionObLoader.Load(config.ContentRoots);

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
                definitions,
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
                    foreach (var k in definitions.AllDefinitions)
                        if (k.Id.TypeId == id.TypeId)
                        {
                            translator.Translate(k);
                        }
                }
                else if (id.SubtypeName == "**any_translated_model**")
                {
                    var ids = new HashSet<MyDefinitionId>();
                    foreach (var k in definitions.AllDefinitions)
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
                    foreach (var k in definitions.AllDefinitions.OfType<MyObjectBuilder_BlockVariantsDefinition>())
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
                    foreach (var def in definitions.GetDefinitions(sid))
                        translator.Translate(def, true);
                }
                else
                {
                    var list = definitions.GetDefinitions(id);
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

            DefinitionWriter.Write(definitionSet, Path.Combine(config.OutputDirectory, "Data/Translated.sbc"));
            return 0;
        }
    }
}