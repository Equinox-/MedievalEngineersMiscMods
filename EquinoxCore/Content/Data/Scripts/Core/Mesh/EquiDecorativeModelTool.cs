using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.WorldEnvironment.Definitions;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions;
using VRage.Definitions.Block;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Models;
using VRage.Import;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeModelToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeModelTool : EquiDecorativeToolBase<EquiDecorativeModelToolDefinition, EquiDecorativeModelToolDefinition.ModelDef>
    {
        private EquiDecorativeModelToolDefinition.ModelDef ModelDef =>
            Def.SortedMaterials[DecorativeToolSettings.ModelIndex % Def.SortedMaterials.Count];

        protected override int RequiredPoints => 1;

        protected override void HitWithEnoughPoints(ListReader<BlockAnchorInteraction> points)
        {
            if (points.Count < 1) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            var modelDef = ModelDef;
            var scale = modelDef.Scale.Clamp(DecorativeToolSettings.ModelScale);
            if (!remove)
            {
                var volume = ModelDef.Volume * scale * scale * scale;
                var durabilityCost = (int)Math.Ceiling(ModelDef.DurabilityBase + ModelDef.DurabilityPerCubicMeter * volume);
                if (!TryRemovePreReqs(durabilityCost, modelDef))
                    return;
            }

            var localRotation = (Matrix)(Matrix.CreateFromQuaternion(BuildingState.WorldRotation) * points[0].Grid.Entity.PositionComp.WorldMatrixInvScaled);
            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveModel(points[0].Anchor);
                else
                    gridDecor.AddModel(ModelDef, new EquiDecorativeMeshComponent.ModelArgs<BlockAndAnchor>
                    {
                        Position = points[0].Anchor,
                        Forward = localRotation.Forward,
                        Up = localRotation.Up,
                        Scale = scale,
                        Shared =
                        {
                            Color = PackedHsvShift
                        },
                    });
                return;
            }

            if (remove)
            {
                MyMultiplayer.RaiseStaticEvent(x => PerformOp, points[0].Grid.Entity.Id, points[0].RpcAnchor, default(ModelRpcArgs), true);
                return;
            }

            MyMultiplayer.RaiseStaticEvent(x => PerformOp,
                points[0].Grid.Entity.Id, points[0].RpcAnchor, new ModelRpcArgs
                {
                    ModelId = ModelDef.Id,
                    PackedForward = VF_Packer.PackNormal(localRotation.Forward),
                    PackedUp = VF_Packer.PackNormal(localRotation.Up),
                    Scale = scale,
                    Color = PackedHsvShift,
                }, false);
        }

        [RpcSerializable]
        private struct ModelRpcArgs
        {
            public MyStringHash ModelId;
            public uint PackedForward;
            public uint PackedUp;
            public float Scale;
            public PackedHsvShift Color;
        }

        [Event, Reliable, Server]
        private static void PerformOp(
            EntityId grid,
            RpcBlockAndAnchor rpcPt0,
            ModelRpcArgs model,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeModelTool behavior)
                || !behavior.Def.Materials.TryGetValue(model.ModelId, out var modelDef)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            BlockAndAnchor pt0 = rpcPt0;
            if (!gridEntity.Components.TryGet(out MyGridDataComponent gridData)
                || !pt0.TryGetGridLocalAnchor(gridData, out var local0))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!NetworkTrust.IsTrusted(gridData, Vector3D.Transform(local0, gridEntity.PositionComp.WorldMatrix)))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (!remove)
            {
                var volume = model.Scale * model.Scale * model.Scale * modelDef.Volume;
                var durabilityCost = (int)Math.Ceiling(modelDef.DurabilityBase + modelDef.DurabilityPerCubicMeter * volume);
                if (!behavior.TryRemovePreReqs(durabilityCost, modelDef))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            var gridDecor = gridEntity.Components.GetOrAdd<EquiDecorativeMeshComponent>();
            if (remove)
                gridDecor.RemoveModel(pt0);
            else
                gridDecor.AddModel(
                    modelDef,
                    new EquiDecorativeMeshComponent.ModelArgs<BlockAndAnchor>
                    {
                        Position = pt0,
                        Forward = VF_Packer.UnpackNormal(model.PackedForward),
                        Up = VF_Packer.UnpackNormal(model.PackedUp),
                        Scale = model.Scale,
                        Shared =
                        {
                            Color = model.Color
                        },
                    });
        }

        private string _currentRenderObjectModel;
        private uint _currentRenderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;

        private void DestroyRenderObject()
        {
            if (_currentRenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED) return;
            MyRenderProxy.RemoveRenderObject(_currentRenderObject);
            _currentRenderObject = MyRenderProxy.RENDER_ID_UNASSIGNED;
        }

        public override void Deactivate()
        {
            base.Deactivate();
            DestroyRenderObject();
        }

        protected override void RenderHelper()
        {
            SetTarget();
            var def = ModelDef;
            MatrixD worldTransform;
            MatrixD worldInv;
            Vector3 localPos;
            if (TryGetAnchor(out var nextAnchor))
            {
                worldTransform = nextAnchor.Grid.Entity.WorldMatrix;
                worldInv = nextAnchor.Grid.Entity.PositionComp.WorldMatrixNormalizedInv;
                localPos = nextAnchor.GridLocalPosition;
                if (nextAnchor.Source == BlockAnchorInteraction.SourceType.Existing)
                    nextAnchor.Draw();
            }
            else
            {
                var detectionLine = DetectionLine;
                worldTransform = Holder.WorldMatrix;
                MatrixD.Invert(ref worldTransform, out worldInv);
                localPos = (Vector3)Vector3D.Transform(detectionLine.To, ref worldInv);
            }

            var localRotation = Matrix.CreateFromQuaternion(BuildingState.WorldRotation) * worldInv;
            var prepared = EquiDecorativeMeshComponent.CreateModelData(
                def,
                new EquiDecorativeMeshComponent.ModelArgs<Vector3>
                {
                    Position = localPos,
                    Forward = (Vector3)localRotation.Forward,
                    Up = (Vector3)localRotation.Up,
                    Scale = DecorativeToolSettings.ModelScale,
                    Shared =
                    {
                        Color = PackedHsvShift
                    },
                });

            var renderMatrix = prepared.Matrix * worldTransform;


            var previewModel = def.Model;
            if (_currentRenderObject == MyRenderProxy.RENDER_ID_UNASSIGNED || _currentRenderObjectModel != previewModel)
            {
                if (_currentRenderObject != MyRenderProxy.RENDER_ID_UNASSIGNED)
                    MyRenderProxy.RemoveRenderObject(_currentRenderObject);
                _currentRenderObject = MyRenderProxy.CreateRenderEntity(
                    $"model_preview_{Holder.EntityId}",
                    previewModel,
                    renderMatrix,
                    MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible | RenderFlags.ForceOldPipeline,
                    CullingOptions.Default,
                    Color.White,
                    prepared.ColorMask);
                _currentRenderObjectModel = previewModel;
            }
            else
                MyRenderProxy.UpdateRenderObject(_currentRenderObject, renderMatrix);

            MyRenderProxy.UpdateRenderEntity(_currentRenderObject, null, PackedHsvShift);
        }

        protected override void RenderShape(MyGridDataComponent grid, ListReader<Vector3> positions)
        {
            // Not used...
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeModelToolDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    [MyDependency(typeof(MyPhysicalModelDefinition))]
    [MyDependency(typeof(MyBlockVariantsDefinition))]
    [MyDependency(typeof(MyPhysicalModelCollectionDefinition))]
    [MyDependency(typeof(MyGrowableEnvironmentItemDefinition))]
    [MyDependency(typeof(MyInventoryItemDefinition))]
    [MyDependency(typeof(MyItemTagDefinition))]
    public class EquiDecorativeModelToolDefinition
        : EquiDecorativeToolBaseDefinition,
            IEquiDecorativeToolBaseDefinition<EquiDecorativeModelToolDefinition.ModelDef>
    {
        private readonly MaterialHolder<ModelDef> _holder;
        public DictionaryReader<MyStringHash, ModelDef> Materials => _holder.Materials;
        public ListReader<ModelDef> SortedMaterials => _holder.SortedMaterials;

        public ImmutableRange<float> ScaleRange { get; private set; }
        private ImmutableRange<float> _sizeRange;

        public EquiDecorativeModelToolDefinition()
        {
            _holder = new MaterialHolder<ModelDef>(this);
        }

        public class ModelDef : MaterialDef<EquiDecorativeModelToolDefinition>
        {
            private Action _computeFromModel;
            private ImmutableRange<float> _scale;
            private float _volume;

            public readonly string Model;
            public readonly float DurabilityPerCubicMeter;

            public ImmutableRange<float> Scale
            {
                get
                {
                    _computeFromModel?.Invoke();
                    return _scale;
                }
            }

            public float Volume
            {
                get
                {
                    _computeFromModel?.Invoke();
                    return _volume;
                }
            }

            internal ModelDef(EquiDecorativeModelToolDefinition owner, MyObjectBuilder_EquiDecorativeModelToolDefinition ownerOb,
                MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef ob)
                : base(owner, ownerOb, ob)
            {
                Model = ob.Model;
                DurabilityPerCubicMeter = ob.DurabilityPerCubicMeter ?? 0;

                _computeFromModel = () =>
                {
                    var data = MyModels.GetModelOnlyModelInfo(Model);
                    _volume = Math.Min(data.BoundingSphere.Volume(), data.BoundingBox.Volume());
                    var modelSize = data.BoundingBox.Size.AbsMax();
                    _scale = new ImmutableRange<float>(Owner._sizeRange.Min / modelSize, Owner._sizeRange.Max / modelSize);
                    _computeFromModel = null;
                };
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeModelToolDefinition)builder;

            ScaleRange = ob.Scale?.Immutable() ?? new ImmutableRange<float>(0.1f, 2f);
            _sizeRange = ob.Size?.Immutable() ?? new ImmutableRange<float>(0.1f, 5f);

            // Custom Models
            if (ob.Models != null)
                foreach (var model in ob.Models)
                    AddModel(model);

            // Models from physical models
            if (ob.PhysicalModels != null)
                foreach (var physical in ob.PhysicalModels)
                {
                    if (physical.All)
                        foreach (var model in MyDefinitionManager.GetOfType<MyPhysicalModelDefinition>())
                            AddPhysical(model);
                    foreach (var subtype in physical.Subtypes)
                        if (MyDefinitionManager.TryGet(MyStringHash.GetOrCompute(subtype), out MyPhysicalModelDefinition model))
                            AddPhysical(model);
                    foreach (var subtype in physical.CollectionSubtypes)
                        if (MyDefinitionManager.TryGet(MyStringHash.GetOrCompute(subtype), out MyPhysicalModelCollectionDefinition collection))
                            AddCollection(collection);
                    foreach (var subtype in physical.GrowableSubtypes)
                        if (MyDefinitionManager.TryGet(MyStringHash.GetOrCompute(subtype), out MyGrowableEnvironmentItemDefinition growable))
                            foreach (var step in growable.GrowthSteps)
                                if (MyDefinitionManager.TryGet(step.ModelCollectionSubtypeId, out MyPhysicalModelCollectionDefinition collection))
                                    AddCollection(collection);
                    foreach (var subtype in physical.BlockVariantSubtypes)
                        if (MyDefinitionManager.TryGet(MyStringHash.GetOrCompute(subtype), out MyBlockVariantsDefinition blockVariants))
                            foreach (var block in blockVariants.Blocks)
                                AddPhysical(block);
                    continue;

                    void AddPhysical(MyPhysicalModelDefinition model) =>
                        AddTemplated(model, model.Model, physical.DurabilityBase, physical.DurabilityPerCubicMeter);

                    void AddCollection(MyPhysicalModelCollectionDefinition collection)
                    {
                        foreach (var modelId in collection.Items)
                            if (MyDefinitionManager.TryGet(modelId, out MyPhysicalModelDefinition model))
                                AddPhysical(model);
                    }
                }

            // Item decals
            if (ob.ItemModels != null)
                foreach (var itemModel in ob.ItemModels)
                {
                    if (itemModel.All)
                        AddItems(MyDefinitionManager.GetOfType<MyInventoryItemDefinition>());
                    if (itemModel.AllWithoutSchematics)
                        AddItems(MyDefinitionManager.GetOfType<MyInventoryItemDefinition>().Where(x => !(x is MySchematicItemDefinition)));

                    if (itemModel.ItemSubtypes != null)
                        foreach (var subtype in itemModel.ItemSubtypes)
                            AddItem(MyDefinitionManager.Get<MyInventoryItemDefinition>(subtype));
                    if (itemModel.Tags != null)
                        foreach (var tag in itemModel.Tags)
                            if (MyDefinitionManager.TryGet<MyItemTagDefinition>(MyStringHash.GetOrCompute(tag), out var tagDef))
                                AddItems(tagDef.Items);
                    if (itemModel.TagsNonPublic != null)
                        foreach (var tag in itemModel.TagsNonPublic)
                            if (MyDefinitionManager.TryGet<MyItemTagDefinition>(MyStringHash.GetOrCompute(tag), out var tagDef))
                                AddItems(tagDef.Items, true);
                    continue;

                    void AddItem(MyInventoryItemDefinition item)
                    {
                        AddTemplated(item, item.Model, itemModel.DurabilityBase, itemModel.DurabilityPerCubicMeter);
                    }

                    void AddItems(IEnumerable<MyInventoryItemDefinition> items, bool includeHidden = false)
                    {
                        foreach (var item in items)
                            if ((includeHidden || item.Public) && item.Enabled)
                                AddItem(item);
                    }
                }

            return;

            void AddModel(MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef model)
            {
                if (string.IsNullOrEmpty(model.Id) || string.IsNullOrEmpty(model.Model))
                    return;
                _holder.Add(new ModelDef(this, ob, model));
            }

            void AddTemplated(
                MyVisualDefinitionBase visual, string model, float? durabilityBase, float? durabilityPerCubicMeter,
                List<InventoryActionBuilder> itemCost = default)
            {
                AddModel(
                    new MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef
                    {
                        Id = $"{visual.Id.TypeId.ShortName}/{visual.Id.SubtypeName}",
                        Name = visual.DisplayNameText,
                        Model = model,
                        UiIcons = visual.Icons,
                        DurabilityBase = durabilityBase,
                        DurabilityPerCubicMeter = durabilityPerCubicMeter,
                    });
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeModelToolDefinition : MyObjectBuilder_EquiDecorativeToolBaseDefinition
    {
        [XmlElement]
        public MutableRange<float>? Scale;

        [XmlElement]
        public MutableRange<float>? Size;

        public class ModelDef : MaterialDef
        {
            /// <summary>
            /// Path to the model file.
            /// </summary>
            /// <example>Models/Components/Scroll.mwm</example>
            [XmlElement]
            public string Model;

            /// <summary>
            /// Additional durability to use per cubic meter of placed model volume.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerCubicMeter;
        }

        [XmlElement("Model")]
        public ModelDef[] Models;

        public class PhysicalModelDef
        {
            /// <summary>
            /// Include all physical models.
            /// </summary>
            [XmlAttribute("All")]
            public bool All;

            /// <summary>
            /// Include physical models with specific subtypes.
            /// </summary>
            [XmlElement("Subtype")]
            public List<string> Subtypes;

            /// <summary>
            /// Include physical models collections with specific subtypes.
            /// </summary>
            [XmlElement("Collection")]
            public List<string> CollectionSubtypes;

            /// <summary>
            /// Include block variants collections with specific subtypes.
            /// </summary>
            [XmlElement("BlockVariant")]
            public List<string> BlockVariantSubtypes;

            /// <summary>
            /// Include all models associated with a growable environment item.
            /// </summary>
            [XmlElement("Growable")]
            public List<string> GrowableSubtypes;

            /// <summary>
            /// How much durability to use per placement, regardless of scale.
            /// </summary>
            [XmlElement]
            public float? DurabilityBase;

            /// <summary>
            /// Additional durability to use per cubic meter of placed model volume.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerCubicMeter;
        }

        [XmlElement("PhysicalModel")]
        public PhysicalModelDef[] PhysicalModels;

        public class ItemModelDef
        {
            /// <summary>
            /// Include all items.
            /// </summary>
            [XmlAttribute("All")]
            public bool All;

            /// <summary>
            /// Include all items that aren't schematic items.
            /// </summary>
            [XmlAttribute("AllWithoutSchematics")]
            public bool AllWithoutSchematics;

            /// <summary>
            /// Include items with specific subtypes.
            /// </summary>
            [XmlElement("Item")]
            public List<string> ItemSubtypes;

            /// <summary>
            /// Include public items with any of the provided tags.
            /// </summary>
            [XmlElement("Tag")]
            public List<string> Tags;

            /// <summary>
            /// Include non-public items with any of the provided tags.
            /// </summary>
            [XmlElement("TagNonPublic")]
            public List<string> TagsNonPublic;

            /// <summary>
            /// How much durability to use per placement, regardless of scale.
            /// </summary>
            [XmlElement]
            public float? DurabilityBase;

            /// <summary>
            /// Additional durability to use per cubic meter of placed model volume.
            /// </summary>
            [XmlElement]
            public float? DurabilityPerCubicMeter;
        }

        [XmlElement("ItemModel")]
        public ItemModelDef[] ItemModels;
    }
}