using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Equinox76561198048419394.Core.Cli.Def;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using Equinox76561198048419394.Core.Cli.Util.SpeedTree;
using Equinox76561198048419394.Core.Cli.Util.Tasks;
using Sandbox.Game.WorldEnvironment.ObjectBuilders;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.ObjectBuilders.Inventory;
using VRageMath;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Cli.Tree
{
    public interface ISpeedTreeOptions
    {
        [Option("physics-min-radius", HelpText = "Minimum bone radius in meters for physics", Default = 0f)]
        public float PhysicsMinRadius { get; set; }

        [Option("physics-snap-angle", HelpText = "Consider bones within this angle tolerance to parallel as part of the same structure", Default = 10)]
        public float PhysicsSnapAngle { get; set; }

        [Option("fracture-length", HelpText = "Desired length of a fracture piece in meters", Default = 2.5f)]
        public float FractureLength { get; set; }

        [Option("fracture-stump-length", HelpText = "Desired length of the stump fracture piece in meters", Default = .1f)]
        public float FractureStumpLength { get; set; }
    }

    public sealed class SpeedTreeTask : ModTask
    {
        /// <summary>
        /// Input speed tree XML file path.
        /// </summary>
        [InputFile]
        public PathProperty SpeedTreeFile { get; }

        /// <summary>
        /// Generated tree subtype, the Speed Tree file name by default.
        /// </summary>
        [Input]
        public Property<string> TreeSubtype { get; }

        [Input(ForcedInterface = typeof(ISpeedTreeOptions))]
        public Property<ISpeedTreeOptions> Options { get; }

        /// <summary>
        /// Output file path for LOD zero, defaults to Models/Environment/TreeSubtype.mwm
        /// </summary>
        public PathProperty ModelFile { get; }

        [OutputFile]
        public IProvider<string[]> ModelFiles { get; }

        /// <summary>
        /// Output file path for generated definition, defaults to Data/Environment/TreeSubtype.sbc
        /// </summary>
        [OutputFile]
        public PathProperty DefinitionFile { get; }
        
        private readonly IProvider<SpeedTreeRaw> _speedTreeRaw;

        protected override string TaskNameOverride => "SpeedTree " + TreeSubtype.Value;

        public SpeedTreeTask(KeenMod mod) : base(mod)
        {
            SpeedTreeFile = mod.PathProperty();
            TreeSubtype = mod.Property<string>().Convention(SpeedTreeFile.Map(Path.GetFileNameWithoutExtension));
            Options = mod.Property<ISpeedTreeOptions>().Convention((ISpeedTreeOptions)null);

            ModelFile = mod.PathProperty().Convention(TreeSubtype
                .Map(treeSubtype => Path.Combine(mod.Content, "Models/Environment", $"{treeSubtype}.mwm")));
            DefinitionFile = mod.PathProperty().Convention(TreeSubtype
                .Map(treeSubtype => Path.Combine(mod.Content, "Data/Environment", $"{treeSubtype}.sbc")));

            _speedTreeRaw = SpeedTreeFile.MapFile(SpeedTreeRaw.Read);
            ModelFiles = _speedTreeRaw.Map(ModelFile, (tree, lodZeroName) =>
            {
                var maxLod = 0;
                foreach (var obj in tree.Objects.Object)
                    if (obj.TryGetDirectLod(out var lod) && lod > maxLod)
                        maxLod = lod;

                var modelPaths = new string[maxLod + 1];
                modelPaths[0] = lodZeroName;
                var lodZeroWithoutExtension = lodZeroName.Substring(0, lodZeroName.LastIndexOf('.'));
                for (var i = 1; i < modelPaths.Length; i++)
                    modelPaths[i] = $"{lodZeroWithoutExtension}_LOD{i}.mwm";
                return modelPaths;
            });
        }

        protected override void ExecuteInternal()
        {
            var scene = _speedTreeRaw.Value;
            var speedTreeName = TreeSubtype.Value;
            var modelPaths = ModelFiles.Value;

            var tree = new SpeedTree();
            tree.AddFromScene(scene, Options.Value, Matrix.CreateRotationX(-MathHelper.PiOver2));

            // Generate parts from materials.
            var materials = scene.Materials.Material.ToDictionary(
                x => x.ID, x => ConvertMaterial(x, Path.GetDirectoryName(SpeedTreeFile.Value)));
            foreach (var branch in tree.BoneToBranch.Values)
            foreach (var lod in branch.Lods)
            foreach (var primitive in lod.Value.Primitives)
            {
                var part = tree.LevelsOfDetail[lod.Key].Part(materials[primitive.Key]);
                part.m_indices.AddCollection(primitive.Value.Indices);
            }

            // Convert textures.
            Mod.ProcessMaterials(materials.Values,
                cmFlags: KeenProcessingFlags.FullyOpaqueToFullyTransparent | KeenProcessingFlags.AssumeInputSrgb,
                addFlags: KeenProcessingFlags.FullyOpaqueToFullyTransparent | KeenProcessingFlags.AssumeInputSrgb,
                alphaMaskFlags: KeenProcessingFlags.AlphaMaskOneThird);


            // Bind levels of detail onto lod zero.
            var lodZero = tree.LevelsOfDetail[0];
            var lodDistance = scene.Objects.LodNear;
            var lodMultiplier = scene.Objects.LodFar / lodDistance;
            for (var i = 1; i < tree.LevelsOfDetail.Length; i++)
            {
                var lod = tree.LevelsOfDetail[i];
                if (lod == null) continue;
                lodZero.Lods.Add(new MyLODDescriptor
                {
                    Model = modelPaths[i],
                    Distance = lodDistance / 4,
                });
                lodDistance *= lodMultiplier;
            }

            lodZero.CollisionGeometry = SpeedTreePhysics.CreateRootShape(tree, speedTreeName, mtl => materials[mtl].Name);

            for (var i = 0; i < tree.LevelsOfDetail.Length; i++)
            {
                var tags = tree.LevelsOfDetail[i].ExportTags();
                var file = modelPaths[i];
                var dir = Path.GetDirectoryName(file);
                if (dir != null)
                    Directory.CreateDirectory(dir);
                MyModelExporter.ExportModelData(file, tags);
            }

            var definitions = new MyObjectBuilder_Definitions();

            definitions.Definitions.Add(new MyObjectBuilder_PhysicalModelCollectionDefinition
            {
                Id = new SerializableDefinitionId(typeof(MyObjectBuilder_PhysicalModelCollectionDefinition), speedTreeName),
                Items = new[]
                {
                    new MyPhysicalModelItem { TypeId = "TreeDefinition", SubtypeId = speedTreeName, Weight = 1 }
                }
            });

            definitions.Definitions.Add(new MyObjectBuilder_TreeDefinition
            {
                Id = new SerializableDefinitionId(typeof(MyObjectBuilder_TreeDefinition), speedTreeName),
                Model = modelPaths[0],
                PhysicalMaterial = "TreeShort",
                CutEffect = "ParticleToolWoodCutting",
                FallSound = "ImpTreeShortFallImpact",
                BreakSound = "ImpTreeShortFallSqueak",
            });

            definitions.Definitions.Add(new MyObjectBuilder_FarmableEnvironmentItemDefinition
            {
                Id = new SerializableDefinitionId(typeof(MyObjectBuilder_FarmableEnvironmentItemDefinition), speedTreeName),
                DeadState = "Dead",
                MaxSlope = 80,
                GrowthSteps = new[]
                {
                    new MyObjectBuilder_GrowableEnvironmentItemDefinition.GrowthStepDef
                    {
                        Name = "Living",
                        StartingProbability = 1,
                        ModelCollectionSubtypeId = speedTreeName,
                        Actions = new[]
                        {
                            new MyObjectBuilder_GrowableEnvironmentItemDefinition.EnvironmentItemActionDef
                            {
                                Name = "Cut",
                                Id = new SerializableDefinitionId(typeof(MyObjectBuilder_TreeDefinition), speedTreeName),
                                NextStep = "Dead"
                            }
                        }
                    },
                    new MyObjectBuilder_GrowableEnvironmentItemDefinition.GrowthStepDef
                    {
                        Name = "Dead",
                        StartingProbability = 0,
                    }
                }
            });

            var cuttingGroups = new List<MyObjectBuilder_CuttingDefinition.MyCuttingGroup>();
            var cuttingGroupConnection = new List<MyObjectBuilder_CuttingDefinition.MyCuttingGroupConnection>();
            foreach (var branch in tree.BoneToBranch.Values.Distinct())
            {
                cuttingGroups.Add(new MyObjectBuilder_CuttingDefinition.MyCuttingGroup
                {
                    Name = branch.Name,
                    IsRoot = branch.Parent == null,
                    SourceBreakableShapes = new[] { $"{speedTreeName}_{branch.Name}" },
                    SpawnCondition = MyObjectBuilder_CuttingDefinition.MyCuttingPrefabSpawnCondition.Destruction,
                    SpawnPrefabs = new[]
                    {
                        new MyObjectBuilder_CuttingDefinition.MyCuttingPrefab
                        {
                            Item = new SerializableDefinitionId(typeof(MyObjectBuilder_InventoryItem), "Log"),
                            Amount = 1,
                            FlattenToGravity = false,
                        }
                    }
                });
                if (branch.Parent != null)
                    cuttingGroupConnection.Add(new MyObjectBuilder_CuttingDefinition.MyCuttingGroupConnection
                    {
                        GroupA = branch.Parent.Name,
                        GroupB = branch.Name,
                    });
            }

            var cuttingDefinition = new MyObjectBuilder_CuttingDefinition
            {
                Id = new SerializableDefinitionId(typeof(MyObjectBuilder_CuttingDefinition), speedTreeName),
                EntityId = new SerializableDefinitionId(typeof(MyObjectBuilder_TreeDefinition), speedTreeName),
                CuttingGroups = cuttingGroups.ToArray(),
                CuttingGroupsConnections = cuttingGroupConnection.ToArray(),
            };
            definitions.Definitions.Add(cuttingDefinition);

            DefinitionWriter.Write(definitions, DefinitionFile.Value);
        }

        private static KeenMaterial ConvertMaterial(SpeedTreeRawMaterialsMaterial material, string relativeTo)
        {
            var keen = new KeenMaterial("not-named");
            foreach (var mtl in material.Map)
            {
                if (string.IsNullOrEmpty(mtl.File))
                    continue;
                var file = Path.GetFileNameWithoutExtension(mtl.File);
                if (file.EndsWith("_cm", StringComparison.OrdinalIgnoreCase))
                    keen.ColorMetalTexture = Path.Combine(relativeTo, mtl.File);
                else if (file.EndsWith("_ng", StringComparison.OrdinalIgnoreCase))
                    keen.NormalGlossTexture = Path.Combine(relativeTo, mtl.File);
                else if (file.EndsWith("_add", StringComparison.OrdinalIgnoreCase))
                    keen.ExtensionTexture = Path.Combine(relativeTo, mtl.File);
                else if (file.EndsWith("_alphamask", StringComparison.OrdinalIgnoreCase))
                    keen.AlphaMaskTexture = Path.Combine(relativeTo, mtl.File);
                else
                    throw new Exception($"Unknown material map: {mtl.File}");
            }

            if (keen.ColorMetalTexture == null)
                throw new Exception("No color metal texture for material");

            var name = CleanFileName(keen.ColorMetalTexture);
            var extraHash = 0;

            foreach (var file in keen.Descriptor.Textures.Values)
            {
                var cleanOther = CleanFileName(file);
                if (cleanOther != name)
                    extraHash += cleanOther.GetHashCode();
            }

            if (extraHash != 0)
                name += "_" + extraHash.ToString("x8");
            keen = keen.Rename(name);

            if (!string.IsNullOrEmpty(keen.AlphaMaskTexture))
                keen.Technique = material.TwoSided != 0 ? MyMeshDrawTechnique.ALPHA_MASKED : MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED;
            else
                keen.Technique = MyMeshDrawTechnique.MESH;

            return keen;

            string CleanFileName(string path)
            {
                var file = Path.GetFileNameWithoutExtension(path);
                var idx = file.LastIndexOf('_');
                if (idx >= 0)
                    file = file.Substring(0, idx);
                return file;
            }
        }
    }
}