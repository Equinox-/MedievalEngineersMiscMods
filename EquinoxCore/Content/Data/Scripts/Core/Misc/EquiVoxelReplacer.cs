using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Components.Session;
using VRage.Definitions;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Models;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiVoxelReplacer))]
    [MyDefinitionRequired(typeof(EquiVoxelReplacerDefinition))]
    [MyDependency(typeof(MyComponentEventBus), Critical = false)]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false)]
    [MyDependency(typeof(MyModelComponent), Critical = false)]
    [MyDependency(typeof(MyEntityStateComponent), Critical = false)]
    [StaticEventOwner]
    public class EquiVoxelReplacer : MyEntityComponent, IMyComponentEventProvider, IMyEventOwner
    {
        public const string EventDidWork = "EquiVoxelReplacer_DidWork";

        private readonly PowerObserver _powerObserver = new PowerObserver();
        private readonly InventoryObserver _inventoryObserver = new InventoryObserver();

        [Automatic]
        private readonly MyPositionComponentBase _positionComponent = null;

        [Automatic]
        private readonly MyModelAttachmentComponent _modelAttachmentComponent = null;

        [Automatic]
        private readonly MyModelComponent _modelComponent = null;

        [Automatic]
        private readonly MyComponentEventBus _eventBus = null;

        [Automatic]
        private readonly MyEntityStateComponent _state = null;

        public EquiVoxelReplacer()
        {
            _powerObserver.PoweredChanged += (oldState, newState) => ScheduleUpdate();
            _inventoryObserver.InventoryChanged += ScheduleUpdate;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _powerObserver.OnAddedToContainer(Container);
            _inventoryObserver.OnAddedToContainer(Container);
            _positionComponent.OnPositionChanged += OnPositionChanged;
            if (_modelComponent != null)
                _modelComponent.ModelChanged += OnModelChanged;
            if (_modelAttachmentComponent != null)
            {
                _modelAttachmentComponent.OnEntityAttached += OnEntityAttached;
                _modelAttachmentComponent.OnEntityDetached += OnEntityDetached;
                foreach (var entry in Definition.Dummies)
                foreach (var attached in _modelAttachmentComponent.GetAttachedEntities(entry.Key))
                    OnEntityAttached(_modelAttachmentComponent, attached);
            }
        }

        public override void OnRemovedFromScene()
        {
            if (MyMultiplayerModApi.Static.IsServer)
            {
                _powerObserver.OnRemovedFromContainer();
                _inventoryObserver.OnRemovedFromContainer();
                _positionComponent.OnPositionChanged -= OnPositionChanged;
                if (_modelComponent != null)
                    _modelComponent.ModelChanged -= OnModelChanged;
                if (_modelAttachmentComponent != null)
                {
                    _modelAttachmentComponent.OnEntityAttached -= OnEntityAttached;
                    _modelAttachmentComponent.OnEntityDetached -= OnEntityDetached;
                    foreach (var entry in Definition.Dummies)
                    foreach (var attached in _modelAttachmentComponent.GetAttachedEntities(entry.Key))
                        OnEntityDetached(_modelAttachmentComponent, attached);
                }
            }

            base.OnRemovedFromScene();
        }

        private void OnEntityAttached(MyModelAttachmentComponent component, MyEntity entity)
        {
            if (!Definition.Dummies.ContainsKey(component.GetEntityAttachmentPoint(entity))) return;
            entity.PositionComp.OnPositionChanged += OnPositionChanged;
            var model = entity.Get<MyModelComponent>();
            if (model != null) model.ModelChanged += OnModelChanged;
        }

        private void OnEntityDetached(MyModelAttachmentComponent component, MyEntity entity)
        {
            entity.PositionComp.OnPositionChanged -= OnPositionChanged;
            var model = entity.Get<MyModelComponent>();
            if (model != null) model.ModelChanged -= OnModelChanged;
        }

        private void OnPositionChanged(MyPositionComponentBase obj) => ScheduleUpdate();

        private void OnModelChanged(MyModelComponent.ModelChangedArgs modelChangedArgs) => ScheduleUpdate();

        public EquiVoxelReplacerDefinition Definition { get; private set; }

        private bool _updateScheduled;
        private readonly VoxelPlacementBuffer _placementBuffer = new VoxelPlacementBuffer();
        private readonly VoxelMiningBuffer _miningBuffer = new VoxelMiningBuffer();

        private void ScheduleUpdate()
        {
            if (!Entity.InScene || !MyMultiplayerModApi.Static.IsServer) return;
            if (_updateScheduled) return;
            _updateScheduled = true;
            Scheduler.AddScheduledCallback(ExecuteUpdate, MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS * 2);
        }

        [Update(false)]
        private void ExecuteUpdate(long dt)
        {
            _updateScheduled = false;
            if (!_powerObserver.IsPowered) return;
            if (Definition.RequiredStates.Count > 0 && _state != null && !Definition.RequiredStates.Contains(_state.CurrentState)) return;
            using (PoolManager.Get(out List<OrientedBoundingBoxD> boxes))
            using (PoolManager.Get(out List<MyVoxelBase> voxels))
            {
                GatherWorldVolumes(boxes);
                if (boxes.Count == 0) return;
                var worldBounds = BoundingBoxD.CreateInvalid();
                foreach (var box in boxes)
                    worldBounds.Include(box.GetAABB());
                GatherVoxels(worldBounds, voxels);
                if (voxels.Count == 0)
                    return;
                var availableForDeposit = ComputeDepositAvailability();
                if (availableForDeposit == 0)
                    return;

                var used = PerformVoxelOp(voxels, boxes, Definition.PlacementDefinition.Material.Index,
                    availableForDeposit, _miningBuffer, Definition.RemoveFarmingItems);
                if (used <= 0) return;
                _eventBus?.Invoke(EventDidWork, true);
                UseDepositMaterials(used);
                GiftMiningMaterials();
            }
        }

        private int ComputeDepositAvailability()
        {
            if (MyAPIGateway.Session.CreativeMode)
                return int.MaxValue;
            if (string.IsNullOrEmpty(Definition.SourceInventory.String))
                return _placementBuffer.AvailableVolume(_inventoryObserver.Inventories.Values.GetEnumerator(), Definition.PlacementDefinition);
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (_inventoryObserver.Inventories.TryGetValue(Definition.SourceInventory, out var inv))
                return _placementBuffer.AvailableVolume(inv, Definition.PlacementDefinition);
            return 0;
        }

        private void UseDepositMaterials(int amount)
        {
            if (MyAPIGateway.Session.CreativeMode)
                return;
            if (string.IsNullOrEmpty(Definition.SourceInventory.String))
                _placementBuffer.ConsumeVolume(_inventoryObserver.Inventories.Values.GetEnumerator(), Definition.PlacementDefinition, amount);
            if (_inventoryObserver.Inventories.TryGetValue(Definition.SourceInventory, out var inv))
                _placementBuffer.ConsumeVolume(inv, Definition.PlacementDefinition, amount);
        }

        private void GiftMiningMaterials()
        {
            var def = Definition.MiningDefinition;
            if (def == null) return;
            if (string.IsNullOrEmpty(Definition.DestinationInventory.String))
            {
                foreach (var inv in _inventoryObserver.Inventories.Values)
                    _miningBuffer.PutMaterialsInto(inv, def);
            }
            else if (_inventoryObserver.Inventories.TryGetValue(Definition.DestinationInventory, out var inv))
            {
                _miningBuffer.PutMaterialsInto(inv, def);
            }
        }

        private static readonly MyStorageData VoxelData = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);

        private static int PerformVoxelOp(
            List<MyVoxelBase> voxels,
            List<OrientedBoundingBoxD> boxes,
            byte replacementMaterial,
            int materialLimit,
            VoxelMiningBuffer miningBuffer,
            bool disableFarmingItems)
        {
            var result = PerformVoxelOpInternal(voxels, boxes, replacementMaterial, materialLimit, miningBuffer, disableFarmingItems);
            if (result <= 0) return result;
            var voxelIds = new EntityId[voxels.Count];
            for (var i = 0; i < voxelIds.Length; i++)
                voxelIds[i] = voxels[i].Id;
            var serializedBoxes = new SerializableOrientedBoundingBoxD[boxes.Count];
            for (var i = 0; i < serializedBoxes.Length; i++)
                serializedBoxes[i] = boxes[i];
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => PerformVoxelOpClient, voxelIds, serializedBoxes,
                replacementMaterial, materialLimit, disableFarmingItems);

            return result;
        }

        [Event]
        [Broadcast]
        private static void PerformVoxelOpClient(EntityId[] voxelIds, SerializableOrientedBoundingBoxD[] serializedBoxes, byte replacementMaterial,
            int materialLimit, bool disableFarmingItems)
        {
            var voxels = new List<MyVoxelBase>(voxelIds.Length);
            foreach (var id in voxelIds)
                if (MySession.Static.Scene.TryGetEntity(id, out var ent) && ent is MyVoxelBase voxel)
                    voxels.Add(voxel);
            var boxes = new List<OrientedBoundingBoxD>(serializedBoxes.Length);
            foreach (var box in serializedBoxes)
                boxes.Add(box);
            PerformVoxelOpInternal(voxels, boxes, replacementMaterial, materialLimit, null, disableFarmingItems);
        }

        private static int PerformVoxelOpInternal(
            List<MyVoxelBase> voxels,
            List<OrientedBoundingBoxD> boxes,
            byte replacementMaterial,
            int materialLimit,
            VoxelMiningBuffer miningBuffer,
            bool disableFarmingItems)
        {
            var data = VoxelData;
            var usedMaterial = 0;
            using (PoolManager.Get(out List<OrientedBoundingBoxD> storageBoxes))
                foreach (var voxel in voxels)
                {
                    if (materialLimit < usedMaterial)
                        break;
                    var invWorld = voxel.PositionComp.WorldMatrixInvScaled;
                    var storageBounds = BoundingBoxD.CreateInvalid();
                    storageBoxes.Clear();
                    var voxelOffset = (voxel.Size >> 1) + voxel.StorageMin;
                    foreach (var obb in boxes)
                    {
                        var storageObb = obb;
                        storageObb.Transform(invWorld);
                        storageObb.HalfExtent /= voxel.VoxelSize;
                        storageObb.Center = storageObb.Center / voxel.VoxelSize + voxelOffset;
                        storageBoxes.Add(storageObb);
                        storageBounds.Include(storageObb.GetAABB());
                    }

                    var storageMin = Vector3I.Max(Vector3I.Floor(storageBounds.Min), voxel.StorageMin);
                    var storageMax = Vector3I.Min(Vector3I.Ceiling(storageBounds.Max), voxel.StorageMax);
                    var localBox = new BoundingBox(storageMin, storageMax);
                    localBox.Translate(-voxel.SizeInMetresHalf - voxel.StorageMin);
                    if (voxel.IntersectStorage(ref localBox, false) == ContainmentType.Disjoint)
                        continue;
                    data.Resize(storageMin, storageMax);
                    voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.ContentAndMaterial, 0, storageMin, storageMax);
                    var modified = false;
                    var modifiedVoxels = BoundingBoxD.CreateInvalid();
                    foreach (var pt in new BoundingBoxI(Vector3I.Zero, storageMax - storageMin).EnumeratePoints())
                    {
                        if (materialLimit < usedMaterial)
                            break;
                        var contained = false;
                        var voxelBox = new BoundingBoxD(storageMin + pt, storageMin + pt + 1);
                        foreach (var storageBox in storageBoxes)
                            if (storageBox.Intersects(ref voxelBox))
                            {
                                contained = true;
                                break;
                            }

                        if (!contained) continue;
                        var tmpPt = pt;
                        var index = data.ComputeLinear(ref tmpPt);
                        var content = data.Content(index);
                        if (content <= 0) continue;
                        var material = data.Material(index);
                        if (material == replacementMaterial) continue;

                        usedMaterial += content;
                        miningBuffer?.Add(material, content);
                        data.Material(index, replacementMaterial);
                        modified = true;
                        modifiedVoxels.Include(voxelBox);
                    }

                    if (!modified) continue;
                    voxel.Storage.WriteRange(data, MyStorageDataTypeFlags.Material, storageMin, storageMax);
                    if (!disableFarmingItems) continue;
                    modifiedVoxels.Min = (modifiedVoxels.Min - voxelOffset) * voxel.VoxelSize;
                    modifiedVoxels.Max = (modifiedVoxels.Max - voxelOffset) * voxel.VoxelSize;
                    voxel.DisableFarmingItemsIn(OrientedBoundingBoxD.Create(modifiedVoxels, voxel.WorldMatrix));
                }

            return usedMaterial;
        }

        private static void GatherVoxels(in BoundingBoxD box, List<MyVoxelBase> voxels)
        {
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopmostEntitiesInBox(box, entities, MyEntityQueryType.Static);
                foreach (var ent in entities)
                    if (ent is MyVoxelBase voxel && !(ent is MyVoxelPhysics))
                        voxels.Add(voxel);
            }
        }

        private void GatherWorldVolumes(List<OrientedBoundingBoxD> boxes)
        {
            if (Definition.Dummies.Count == 0)
            {
                boxes.Add(new OrientedBoundingBoxD(_positionComponent.LocalAABB, _positionComponent.WorldMatrix));
                return;
            }

            foreach (var subpart in Definition.Dummies)
            {
                if (string.IsNullOrEmpty(subpart.Key.String))
                    GatherWorldVolumesFromModel(Entity, subpart.Value, boxes);
                else
                {
                    var attachment = _modelAttachmentComponent?.GetAttachedEntities(subpart.Key) ?? default;
                    foreach (var entity in attachment)
                        GatherWorldVolumesFromModel(entity, subpart.Value, boxes);
                }
            }
        }

        private static void GatherWorldVolumesFromModel(MyEntity entity,
            HashSetReader<string> dummies,
            List<OrientedBoundingBoxD> boxes)
        {
            if (entity == null || !entity.InScene)
                return;
            var model = entity.Get<MyModelComponent>()?.Model;
            if (model == null)
                return;
            var worldMatrix = entity.PositionComp.WorldMatrix;
            foreach (var dummy in dummies)
                if (model.TryGetDummy(dummy, out var dummyObj))
                    boxes.Add(new OrientedBoundingBoxD(dummyObj.Matrix * worldMatrix));
        }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiVoxelReplacerDefinition) def;
            _powerObserver.RequiredPower = Definition.RequiredPower;
        }

        public bool HasEvent(string eventName) => eventName == EventDidWork;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelReplacer : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiVoxelReplacerDefinition))]
    [MyDependency(typeof(MyVoxelMiningDefinition), Recursive = true)]
    [MyDependency(typeof(MyVoxelMaterialDefinition), Recursive = true)]
    public class EquiVoxelReplacerDefinition : MyEntityComponentDefinition
    {
        public PowerObserver.RequiredPowerEnum RequiredPower;
        public HashSetReader<MyStringHash> RequiredStates { get; private set; }

        public DictionaryReader<MyStringHash, HashSetReader<string>> Dummies { get; private set; }
        public MyVoxelMiningDefinition MiningDefinition { get; private set; }

        public VoxelPlacementBuffer.VoxelPlacementDefinition PlacementDefinition { get; private set; }

        public MyStringHash SourceInventory { get; private set; }
        public MyStringHash DestinationInventory { get; private set; }

        public bool RemoveFarmingItems { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiVoxelReplacerDefinition) def;

            RequiredPower = ob.RequiredPower ?? PowerObserver.RequiredPowerEnum.None;
            if (ob.RequiredStates != null && ob.RequiredStates.Length > 0)
            {
                var requiredStates = new HashSet<MyStringHash>();
                foreach (var required in ob.RequiredStates)
                    requiredStates.Add(MyStringHash.GetOrCompute(required));
                RequiredStates = requiredStates;
            }
            else
                RequiredStates = default;

            if (ob.Dummies != null && ob.Dummies.Length > 0)
            {
                var dummiesBuilder = new MyHashSetDictionary<MyStringHash, string>();
                foreach (var dummy in ob.Dummies)
                {
                    var spec = new MyDummySpec(dummy);
                    dummiesBuilder.Add(MyStringHash.GetOrCompute(spec.SubpartName), spec.Name);
                }

                var dummies = new Dictionary<MyStringHash, HashSetReader<string>>();
                foreach (var built in dummiesBuilder)
                    dummies.Add(built.Key, built.Value);
                Dummies = dummies;
            }
            else
                Dummies = default;

            if (ob.MiningDefinition.HasValue)
            {
                MiningDefinition = MyDefinitionManager.Get<MyVoxelMiningDefinition>(ob.MiningDefinition.Value);
                if (MiningDefinition == null)
                    Log.Warning($"Failed to load voxel mining definition {ob.MiningDefinition}");
            }
            else
                MiningDefinition = null;

            if (ob.ReplacementMaterial != null)
            {
                PlacementDefinition = new VoxelPlacementBuffer.VoxelPlacementDefinition(ob.ReplacementMaterial, Log);
            }
            else
            {
                Log.Warning("No voxel placement definition");
                PlacementDefinition = new VoxelPlacementBuffer.VoxelPlacementDefinition(MyVoxelMaterialDefinition.Default, 64,
                    DictionaryReader<MyDefinitionId, int>.Empty);
            }

            SourceInventory = MyStringHash.GetOrCompute(ob.SourceInventory);
            DestinationInventory = MyStringHash.GetOrCompute(ob.DestinationInventory);
            RemoveFarmingItems = ob.RemoveFarmingItems ?? true;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiVoxelReplacerDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("RequiredPower")]
        public PowerObserver.RequiredPowerEnum? RequiredPower;

        [XmlElement("RequiredState")]
        public string[] RequiredStates;

        [XmlElement("Dummy")]
        public string[] Dummies;

        [XmlElement("SourceInventory")]
        public string SourceInventory;

        [XmlElement("DestInventory")]
        public string DestinationInventory;

        [XmlElement("Mining")]
        public SerializableDefinitionId? MiningDefinition;

        [XmlElement("Replacement")]
        public MyObjectBuilder_VoxelMiningDefinition.MiningDef ReplacementMaterial;

        [XmlElement("RemoveFarmingItems")]
        public bool? RemoveFarmingItems;
    }
}