using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.UI;
using Equinox76561198048419394.Core.Util;
using Equinox76561198048419394.Core.Util.EqMath;
using Medieval.Constants;
using Sandbox.Definitions;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.Game.WorldEnvironment.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Core;
using VRage.Definitions;
using VRage.Definitions.Block;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.Import;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiDecorativeModelToolDefinition))]
    [StaticEventOwner]
    public class EquiDecorativeModelTool : EquiDecorativeToolBase
    {
        private EquiDecorativeModelToolDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiDecorativeModelToolDefinition)definition;
        }

        private EquiDecorativeModelToolDefinition.ModelDef ModelDef =>
            _definition.SortedModels[DecorativeToolSettings.ModelIndex % _definition.SortedModels.Count];

        protected override int RequiredPoints => 1;

        protected override void HitWithEnoughPoints(ListReader<DecorAnchor> points)
        {
            if (points.Count < 1) return;
            var remove = ActiveAction == MyHandItemActionEnum.Secondary;
            var modelDef = ModelDef;
            var scale = modelDef.Scale.Clamp(DecorativeToolSettings.ModelScale);
            if (!remove)
            {
                var volume = ModelDef.Volume * scale * scale * scale;
                var durabilityCost = (int)Math.Ceiling(ModelDef.DurabilityBase + ModelDef.DurabilityPerCubicMeter * volume);
                if (!TryRemoveDurability(durabilityCost))
                    return;
            }

            var localRotation = (Matrix)(Matrix.CreateFromQuaternion(BuildingState.WorldRotation) * points[0].Grid.Entity.PositionComp.WorldMatrixInvScaled);
            if (MyMultiplayerModApi.Static.IsServer)
            {
                var gridDecor = points[0].Grid.Container.GetOrAdd<EquiDecorativeMeshComponent>();
                if (remove)
                    gridDecor.RemoveModel(points[0].Anchor);
                else
                    gridDecor.AddModel(ModelDef, new EquiDecorativeMeshComponent.ModelArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                    {
                        Position = points[0].Anchor,
                        Forward = localRotation.Forward,
                        Up = localRotation.Up,
                        Scale = scale,
                        Color = PackedHsvShift
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
            EquiDecorativeMeshComponent.RpcBlockAndAnchor rpcPt0,
            ModelRpcArgs model,
            bool remove)
        {
            if (!MyEventContext.Current.TryGetSendersHeldBehavior(out EquiDecorativeModelTool behavior)
                || !behavior._definition.Models.TryGetValue(model.ModelId, out var modelDef)
                || !behavior.Scene.TryGetEntity(grid, out var gridEntity))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            EquiDecorativeMeshComponent.BlockAndAnchor pt0 = rpcPt0;
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
                if (!behavior.TryRemoveDurability(durabilityCost))
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
                    new EquiDecorativeMeshComponent.ModelArgs<EquiDecorativeMeshComponent.BlockAndAnchor>
                    {
                        Position = pt0,
                        Forward = VF_Packer.UnpackNormal(model.PackedForward),
                        Up = VF_Packer.UnpackNormal(model.PackedUp),
                        Scale = model.Scale,
                        Color = model.Color,
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
                if (nextAnchor.Source == AnchorSource.Existing)
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
                    Color = PackedHsvShift,
                    Scale = DecorativeToolSettings.ModelScale,
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
    public class EquiDecorativeModelToolDefinition : EquiDecorativeToolBaseDefinition
    {
        public ListReader<ModelDef> SortedModels { get; private set; }
        public DictionaryReader<MyStringHash, ModelDef> Models { get; private set; }
        public ImmutableRange<float> ScaleRange { get; private set; }
        private ImmutableRange<float> _sizeRange;

        public class ModelDef : IMyObject, IEquiIconGridItem
        {
            private Action _computeFromModel;
            private ImmutableRange<float> _scale;
            private float _volume;

            public readonly EquiDecorativeModelToolDefinition Owner;
            public readonly MyStringHash Id;
            public readonly string Model;
            public string Name { get; }
            public readonly float DurabilityBase;
            public readonly float DurabilityPerCubicMeter;
            public string[] UiIcons { get; }

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

            internal ModelDef(EquiDecorativeModelToolDefinition owner, MyStringHash id, MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef ob)
            {
                Owner = owner;
                Id = id;
                Name = ob.Name ?? EquiIconGridController.NameFromId(id);
                if (ob.UiIcons != null && ob.UiIcons.Length > 0)
                    UiIcons = ob.UiIcons;
                else
                {
                    // Log.Warning($"Model {owner.Id}/{Name} has no UI icon.  Add <UiIcon> tag to the Model.");
                    UiIcons = null;
                }

                Model = ob.Model;
                DurabilityBase = ob.DurabilityBase ?? 1;
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

            void IMyObject.Deserialize(MyObjectBuilder_Base builder) => throw new NotImplementedException();

            MyObjectBuilder_Base IMyObject.Serialize() => throw new NotImplementedException();

            IMyObjectIdentifier IMyObject.Id => throw new NotImplementedException();
            MyDefinitionId IMyObject.DefinitionId => new MyDefinitionId(typeof(MyObjectBuilder_EquiDecorativeModelToolDefinition), Id);
            bool IMyObject.NeedsSerialize => throw new NotImplementedException();
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiDecorativeModelToolDefinition)builder;

            ScaleRange = ob.Scale?.Immutable() ?? new ImmutableRange<float>(0.1f, 2f);
            _sizeRange = ob.Size?.Immutable() ?? new ImmutableRange<float>(0.1f, 5f);

            var dict = new Dictionary<MyStringHash, ModelDef>();
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

                    void AddPhysical(MyPhysicalModelDefinition model) => AddTemplated(physical, model, model.Model);

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

                    void AddItem(MyInventoryItemDefinition item) => AddTemplated(itemModel, item, item.Model);

                    void AddItems(IEnumerable<MyInventoryItemDefinition> items, bool includeHidden = false)
                    {
                        foreach (var item in items)
                            if ((includeHidden || item.Public) && item.Enabled)
                                AddItem(item);
                    }
                }

            Models = dict;
            SortedModels = dict.Values.OrderBy(x => x.Name).ToList();
            return;

            void AddModel(MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef model)
            {
                if (string.IsNullOrEmpty(model.Id) || string.IsNullOrEmpty(model.Model))
                    return;
                var id = MyStringHash.GetOrCompute(model.Id);
                dict[id] = new ModelDef(this, id, model);
            }

            void AddTemplated(MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDefShared template, MyVisualDefinitionBase visual, string model) =>
                AddModel(
                    new MyObjectBuilder_EquiDecorativeModelToolDefinition.ModelDef
                    {
                        Id = $"{visual.Id.TypeId.ShortName}/{visual.Id.SubtypeName}",
                        Name = visual.DisplayNameText,
                        Model = model,
                        UiIcons = visual.Icons,
                        DurabilityBase = template.DurabilityBase,
                        DurabilityPerCubicMeter = template.DurabilityPerCubicMeter,
                    });
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

        public class ModelDefShared
        {
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

        public class ModelDef : ModelDefShared
        {
            /// <summary>
            /// Unique identifier for this model.
            /// </summary>
            [XmlAttribute("Id")]
            public string Id;

            /// <summary>
            /// Display name for this model.
            /// </summary>
            [XmlAttribute("Name")]
            public string Name;

            /// <summary>
            /// Path to the model file.
            /// </summary>
            [XmlElement]
            public string Model;

            /// <summary>
            /// Icons to show in the UI.
            /// </summary>
            [XmlElement("UiIcon")]
            public string[] UiIcons;
        }

        [XmlElement("Model")]
        public ModelDef[] Models;

        public class PhysicalModelDef : ModelDefShared
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
        }

        [XmlElement("PhysicalModel")]
        public PhysicalModelDef[] PhysicalModels;

        public class ItemModelDef : ModelDefShared
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
        }

        [XmlElement("ItemModel")]
        public ItemModelDef[] ItemModels;
    }
}