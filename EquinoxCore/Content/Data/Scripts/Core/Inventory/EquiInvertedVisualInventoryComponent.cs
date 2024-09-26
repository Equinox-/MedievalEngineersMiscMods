using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.Inventory;
using Medieval.GameSystems;
using Sandbox.Entities.Components;
using Sandbox.Game.Components;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Components.Entity.Animations;
using VRage.Definitions.Components;
using VRage.Definitions.Inventory;
using VRage.Entity.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Inventory
{
    [MyComponent(typeof(MyObjectBuilder_EquiInvertedVisualInventoryComponent))]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = true)]
    [MyDependency(typeof(MyInventoryBase))]
    [MyDependency(typeof(MyEntityEquipmentComponent))]
    [MyDependency(typeof(MyComponentEventBus))]
    [MyDependency(typeof(MySkeletonComponent))]
    [MyDefinitionRequired(typeof(EquiInvertedVisualInventoryComponentDefinition))]
    public class EquiInvertedVisualInventoryComponent : MyEntityComponent, IMyComponentEventProvider, IComponentDebugDraw
    {
        [Automatic]
        private readonly MyModelAttachmentComponent _modelAttachment = null;

        [Automatic]
        private readonly MyComponentEventBus _eventBus = null;

        [Automatic]
        private readonly MyEntityEquipmentComponent _equipment = null;

        [Automatic]
        private readonly MySkeletonComponent _skeleton = null;

        private readonly Dictionary<MyStringHash, MyInventoryBase> _trackedInventories = new Dictionary<MyStringHash, MyInventoryBase>();

        private readonly Dictionary<EquiInvertedVisualInventoryComponentDefinition.Attachment, MappingResult> _mappingResults =
            new Dictionary<EquiInvertedVisualInventoryComponentDefinition.Attachment, MappingResult>();

        private readonly Dictionary<EquiInvertedVisualInventoryComponentDefinition.Attachment, Dictionary<MyDefinitionId, MyEntity>> _trackedEntities =
            new Dictionary<EquiInvertedVisualInventoryComponentDefinition.Attachment, Dictionary<MyDefinitionId, MyEntity>>();

        private static bool IsDedicated => ((IMyUtilities)MyAPIUtilities.Static).IsDedicated;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (IsDedicated && _definition.FacadeOutgoingEvents.Count == 0)
                return;

            foreach (var inv in Container.GetComponents<MyInventoryBase>())
                if (_definition.AllInventories || _definition.Inventories.Contains(inv.SubtypeId))
                {
                    inv.ContentsChanged += InventoryChanged;
                    _trackedInventories.Add(inv.SubtypeId, inv);
                }

            if (_eventBus != null)
                foreach (var incoming in _definition.FacadeIncomingEvents)
                    _eventBus.TryAddListener(incoming, HandleEvent);

            if (_equipment != null)
            {
                _equipment.ItemEquipped += EquipmentChanged;
                _equipment.ItemUnequipped += EquipmentChanged;
            }

            _scheduled = true;
            AddScheduledCallback(UpdateMappings);

            FacadeEditorDebug.Changed += FacadeEdited;
            EquiInvertedVisualInventoryDebug.Changed += VisualInventoryEdited;
        }

        public override void OnRemovedFromScene()
        {
            FacadeEditorDebug.Changed -= FacadeEdited;
            EquiInvertedVisualInventoryDebug.Changed -= VisualInventoryEdited;

            foreach (var inv in _trackedInventories.Values)
                inv.ContentsChanged -= InventoryChanged;
            _trackedInventories.Clear();

            if (_equipment != null)
            {
                _equipment.ItemEquipped -= EquipmentChanged;
                _equipment.ItemUnequipped -= EquipmentChanged;
            }

            if (_eventBus != null)
                foreach (var incoming in _definition.FacadeIncomingEvents)
                    _eventBus.RemoveListener(incoming, HandleEvent);
            base.OnRemovedFromScene();
        }

        private void VisualInventoryEdited(
            EquiInvertedVisualInventoryComponentDefinition arg1,
            EquiInvertedVisualInventoryComponentDefinition.Attachment arg2)
        {
            _mappingResults.Clear();
            ScheduleUpdate();
        }

        private void FacadeEdited(MyItemFacadeDefinition obj)
        {
            _mappingResults.Clear();
            ScheduleUpdate();
        }

        private void InventoryChanged(MyInventoryBase obj) => ScheduleUpdate();

        private void EquipmentChanged(MyEquipmentItem item)
        {
            if (_definition.NeedsRecentlyEquippedFor(item.GetDefinition()))
            {
                if (_recentlyEquipped.Count == 0 || _recentlyEquipped[_recentlyEquipped.Count - 1] != item.DefinitionId)
                    _recentlyEquipped.Add(item.DefinitionId);
                PruneRecentlyEquipped();
            }

            ScheduleUpdate();
        }

        private void ScheduleUpdate()
        {
            if (Entity.MarkedForClose)
                return;
            if (_scheduled)
                return;
            AddScheduledCallback(UpdateMappings);
        }

        private bool _scheduled;

        [Update(false)]
        private void UpdateMappings(long dt)
        {
            _scheduled = false;
            using (PoolManager.Get(out List<MappingResult> target))
            {
                foreach (var slot in _definition.AttachmentPoints)
                {
                    var attachments = slot.Value.AttachmentPoints;
                    target.Clear();
                    CalculateMapping(slot.Value.Mappings, target, attachments.Count);
                    for (var i = 0; i < attachments.Count; i++)
                    {
                        var attachment = attachments[i];
                        var newMapping = i < target.Count ? target[i] : default;
                        if (_mappingResults.TryGetValue(attachment, out var prevResult) && newMapping.Equals(prevResult)) continue;
                        if (!IsDedicated)
                            ApplyResult(attachment, newMapping);
                        _mappingResults[attachment] = newMapping;
                    }
                }
            }
        }

        private void CalculateMapping(
            ListReader<EquiInvertedVisualInventoryComponentDefinition.Mapping> mappings,
            List<MappingResult> target,
            int limit)
        {
            foreach (var mapping in mappings)
            {
                if (target.Count >= limit) return;

                if (!mapping.RecentlyEquipped)
                {
                    CalculateMappingNonEquipment(null);
                    continue;
                }

                for (var i = 0; i < RecentlyEquippedMax && i < _recentlyEquipped.Count && target.Count < limit; i++)
                {
                    var id = _recentlyEquipped[_recentlyEquipped.Count - 1 - i];
                    CalculateMappingNonEquipment(id);
                }

                continue;

                void CalculateMappingNonEquipment(MyDefinitionId? only)
                {
                    if (mapping.Inventory == MyStringHash.NullOrEmpty)
                    {
                        foreach (var inv in _trackedInventories.Values)
                        {
                            CalculateMappingAgainstInventory(inv, only);
                            if (target.Count >= limit) return;
                        }
                    }
                    else if (_trackedInventories.TryGetValue(mapping.Inventory, out var inv))
                        CalculateMappingAgainstInventory(inv, only);
                }

                void CalculateMappingAgainstInventory(
                    MyInventoryBase inv,
                    MyDefinitionId? only)
                {
                    foreach (var item in inv.Items)
                    {
                        if (target.Count >= limit) return;
                        // Filter out all items that don't match the filter.
                        if (only.HasValue && item.DefinitionId != only.Value) continue;
                        // Equipment is already visible, so don't show it again.
                        if (item is MyEquipmentItem equipmentItem && _equipment.IsEquipped(equipmentItem)) continue;
                        var facade = mapping.TryGetFacade(item.GetDefinition());
                        if (facade != null)
                            target.Add(new MappingResult(mapping, facade, inv, item));
                    }
                }
            }
        }

        private readonly struct MappingResult
        {
            public readonly EquiInvertedVisualInventoryComponentDefinition.Mapping Mapping;
            public readonly MyInventoryBase Inventory;
            public readonly MyInventoryItem Item;
            public readonly MyItemFacadeDefinition Facade;
            public bool HasValue => Mapping != null && Inventory != null && Item != null;

            public MappingResult(EquiInvertedVisualInventoryComponentDefinition.Mapping mapping, MyItemFacadeDefinition facade, MyInventoryBase inventory,
                MyInventoryItem item)
            {
                Mapping = mapping;
                Facade = facade;
                Inventory = inventory;
                Item = item;
            }

            public bool Equals(MappingResult other) => Equals(Mapping, other.Mapping)
                                                       && Equals(Facade, other.Facade)
                                                       && Equals(Inventory, other.Inventory)
                                                       && Equals(Item, other.Item);
        }

        private void ApplyResult(EquiInvertedVisualInventoryComponentDefinition.Attachment attachment, MappingResult result)
        {
            if (!_trackedEntities.TryGetValue(attachment, out var entities))
                _trackedEntities.Add(attachment, entities = new Dictionary<MyDefinitionId, MyEntity>());
            if (!result.HasValue)
            {
                foreach (var ent in entities.Values)
                    Hide(ent);
                return;
            }

            var entityDef = result.Mapping.Entity ?? default;
            MyEntity edit = null;
            foreach (var ent in entities)
            {
                if (ent.Key == entityDef)
                {
                    edit = ent.Value;
                    continue;
                }

                Hide(ent.Value);
            }

            if (edit == null)
            {
                edit = Scene.CreateEntity(entityDef);
                entities.Add(entityDef, edit);
                edit.Components.Add(new MyModelComponent());
                edit.Components.Add(new MyInventoryItemComponent());
                edit.AddDebugRenderComponent(new MyDebugRenderComponent(edit));
                edit.Render = new VRage.Components.Entity.Render.MyRenderComponent();
                edit.Save = false;
                _modelAttachment.TryAttachEntityToPoint(edit, attachment.Name);
            }

            var model = result.Facade.Model ?? result.Item.GetDefinition().Model;

            // Update item.
            edit.Components.Get<MyInventoryItemComponent>()?.SetItemAndContainer(result.Item, result.Inventory);

            // Update model.
            if (!string.IsNullOrEmpty(model))
            {
                edit.ModelComp.SetModel(model);
                edit.Render.Visible = result.Facade.Visible ?? true;
            }
            else
                edit.Render.Visible = false;

            // Refresh render object.
            edit.Render.UpdateRenderObject(false);
            if (edit.Render.Visible)
                edit.Render.UpdateRenderObject(true);

            // Update the location.
            _modelAttachment.SetAdditionalMatrix(edit, result.Facade.Offset * attachment.Transform);

            return;

            void Hide(MyEntity ent)
            {
                ent.Render.Visible = false;
                ent.Components.Get<MyInventoryItemComponent>()?.SetItemAndContainer(null);
                ent.Render.UpdateRenderObject(false);
            }
        }

        private EquiInvertedVisualInventoryComponentDefinition _definition;

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            _definition = (EquiInvertedVisualInventoryComponentDefinition)def;
        }

        #region Event Bus

        private void HandleEvent(string eventName)
        {
            foreach (var mapping in _mappingResults.Values)
                if (mapping.HasValue && mapping.Facade.EventMapping.TryGetValue(eventName, out var outgoingEvent))
                    _eventBus?.Invoke(outgoingEvent);
        }

        public bool HasEvent(string eventName) => _definition.FacadeOutgoingEvents.Contains(eventName);

        #endregion

        #region Recently Equipped

        private const int RecentlyEquippedMax = 16;
        private const int RecentlyEquippedBloat = 5;
        private readonly List<MyDefinitionId> _recentlyEquipped = new List<MyDefinitionId>();

        private void PruneRecentlyEquipped()
        {
            var remove = _recentlyEquipped.Count - RecentlyEquippedMax;
            if (remove > RecentlyEquippedBloat) _recentlyEquipped.RemoveRange(0, remove);
        }

        public override bool IsSerialized => _recentlyEquipped.Count > 0 && _definition.NeedsRecentlyEquipped;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiInvertedVisualInventoryComponent)base.Serialize(copy);
            var size = Math.Min(_recentlyEquipped.Count, RecentlyEquippedMax);
            if (size > 0)
            {
                ob.RecentlyEquipped = new SerializableDefinitionId[size];
                for (var i = 0; i < size; i++)
                    ob.RecentlyEquipped[i] = _recentlyEquipped[_recentlyEquipped.Count - size + i];
            }

            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiInvertedVisualInventoryComponent)builder;
            _recentlyEquipped.Clear();
            if (ob.RecentlyEquipped != null)
                for (var i = Math.Max(0, ob.RecentlyEquipped.Length - RecentlyEquippedMax); i < ob.RecentlyEquipped.Length; i++)
                    _recentlyEquipped.Add(ob.RecentlyEquipped[i]);
        }

        #endregion

        private bool _modelAttachmentsInitialized;
        private IReadOnlyDictionary<MyStringHash, MyModelAttachmentComponentDefinition.AttachmentPoint> _modelAttachmentDef;

        public void DebugDraw()
        {
            if (!_modelAttachmentsInitialized)
            {
                var def = Entity?.DefinitionId;
                if (def.HasValue && MyDefinitionManager.TryGet(def.Value, out MyContainerDefinition container))
                    _modelAttachmentDef = container.Get<MyModelAttachmentComponentDefinition>()?.AttachmentPoints;
                _modelAttachmentsInitialized = true;
            }

            if (_modelAttachmentDef == null || Entity == null) return;
            foreach (var group in _definition.AttachmentPoints)
            foreach (var slot in group.Value.AttachmentPoints)
                if (_modelAttachmentDef.TryGetValue(slot.Name, out var slotDef))
                {
                    var matrix = (MatrixD)slot.Transform;
                    matrix *= slotDef.Offset;
                    if (_skeleton != null)
                    {
                        var bone = _skeleton.FindBone(slotDef.Bone, out var boneIndex);
                        if (bone != null)
                        {
                            matrix *= _skeleton.RootBoneMatrixInv.GetOrientation() * _skeleton.BoneAbsoluteTransforms[boneIndex];
                        }
                    }

                    matrix *= Entity.WorldMatrix;
                    MyRenderProxy.DebugDrawAxis(matrix, 0.25f, depthRead: false);
                }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiInvertedVisualInventoryComponent : MyObjectBuilder_EntityComponent
    {
        [XmlElement("RecentlyEquipped")]
        public SerializableDefinitionId[] RecentlyEquipped;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition))]
    [MyDependency(typeof(MyItemFacadeDefinition))]
    public class EquiInvertedVisualInventoryComponentDefinition : MyEntityComponentDefinition
    {
        private readonly HashSet<MyStringHash> _inventories = new HashSet<MyStringHash>();
        private readonly Dictionary<MyStringHash, AttachmentGroup> _attachmentPoints = new Dictionary<MyStringHash, AttachmentGroup>();

        private readonly HashSet<string> _facadeIncomingEvents = new HashSet<string>();
        private readonly HashSet<string> _facadeOutgoingEvents = new HashSet<string>();

        public HashSetReader<MyStringHash> Inventories => _inventories;
        public bool AllInventories => _inventories.Contains(MyStringHash.NullOrEmpty);

        public HashSetReader<string> FacadeIncomingEvents => _facadeIncomingEvents;
        public HashSetReader<string> FacadeOutgoingEvents => _facadeOutgoingEvents;
        public DictionaryReader<MyStringHash, AttachmentGroup> AttachmentPoints => _attachmentPoints;

        private FacadeLookup _recentlyEquippedFacades;
        internal bool NeedsRecentlyEquipped => _recentlyEquippedFacades.Count > 0;
        internal bool NeedsRecentlyEquippedFor(MyInventoryItemDefinition def) => _recentlyEquippedFacades.TryGetFacade(def) != null;

        private sealed class FacadeLookup
        {
            private readonly ListReader<MyItemFacadeDefinition> _facades;
            private readonly Dictionary<MyDefinitionId, MyItemFacadeDefinition> _lut = new Dictionary<MyDefinitionId, MyItemFacadeDefinition>();

            internal int Count => _facades.Count;

            internal FacadeLookup(List<MyItemFacadeDefinition> list)
            {
                list.TrimExcess();
                _facades = list;
            }

            internal MyItemFacadeDefinition TryGetFacade(MyInventoryItemDefinition def)
            {
                lock (_lut)
                {
                    if (_lut.TryGetValue(def.Id, out var cached))
                        return cached;
                    cached = null;
                    foreach (var facade in _facades)
                        if (facade.MatchesItemDefinition(def))
                        {
                            cached = facade;
                            break;
                        }

                    _lut.Add(def.Id, cached);

                    return cached;
                }
            }
        }

        public sealed class Mapping
        {
            public readonly MyStringHash Inventory;
            public readonly MyDefinitionId? Entity;
            public readonly bool RecentlyEquipped;
            private readonly FacadeLookup _facades;

            public Mapping(
                MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition.Mapping ob,
                List<MyItemFacadeDefinition> facades,
                string defaultInventory)
            {
                Inventory = MyStringHash.GetOrCompute(string.IsNullOrEmpty(ob.Inventory) ? defaultInventory : ob.Inventory);
                _facades = new FacadeLookup(facades);
                Entity = ob.Entity;
                RecentlyEquipped = ob.RecentlyEquipped;
            }

            public MyItemFacadeDefinition TryGetFacade(MyInventoryItemDefinition def) => _facades.TryGetFacade(def);
        }

        public sealed class Attachment
        {
            public readonly MyStringHash Name;
            public Matrix Transform { get; internal set; }

            public Attachment(MyStringHash name, Matrix transform)
            {
                Name = name;
                Transform = transform;
            }
        }

        public sealed class AttachmentGroup
        {
            public readonly ListReader<Attachment> AttachmentPoints;
            public readonly ListReader<Mapping> Mappings;

            public AttachmentGroup(ListReader<Attachment> attachmentPoints, ListReader<Mapping> mappings)
            {
                AttachmentPoints = attachmentPoints;
                Mappings = mappings;
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition)def;
            var recentlyEquipped = new List<MyItemFacadeDefinition>();

            var sorted = new List<MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition.Mapping>();
            if (ob.Mappings != null)
                sorted.AddRange(ob.Mappings);
            sorted.Sort((a, b) => -a.Priority.CompareTo(b.Priority));
            var mappingByGroup = new MyListDictionary<MyStringHash, Mapping>();
            foreach (var mapping in sorted)
            {
                var attachmentPoint = MyStringHash.GetOrCompute(mapping.Group);
                if (attachmentPoint == MyStringHash.NullOrEmpty) continue;
                var facades = Enumerable.Empty<MyItemFacadeDefinition>();

                if (mapping.Facade != null &&
                    MyDefinitionManager.TryGet<MyItemFacadeDefinition>(MyStringHash.GetOrCompute(mapping.Facade), out var singleFacade))
                    facades = facades.Concat(new[] { singleFacade });
                if (mapping.FacadeTag != null)
                    facades = facades.Concat(MyFacadeManager.GetFacadesByTag(MyStringHash.GetOrCompute(mapping.FacadeTag)));
                var facadesList = facades.Distinct().ToList();
                if (facadesList.Count == 0)
                    continue;

                var built = new Mapping(mapping, facadesList, ob.DefaultInventory);
                mappingByGroup.Add(attachmentPoint, built);
                _inventories.Add(built.Inventory);

                if (built.RecentlyEquipped)
                    foreach (var facade in facadesList)
                        recentlyEquipped.Add(facade);

                foreach (var facade in facadesList)
                foreach (var facadeEvent in facade.EventMapping)
                {
                    _facadeIncomingEvents.Add(facadeEvent.Key);
                    _facadeOutgoingEvents.Add(facadeEvent.Value);
                }
            }

            var attachmentByGroup = new MyListDictionary<MyStringHash, Attachment>();
            foreach (var at in ob.Attachments ?? Array.Empty<MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition.Attachment>())
            {
                var point = MyStringHash.GetOrCompute(at.Point ?? at.Group);
                var group = attachmentByGroup.GetOrAdd(MyStringHash.GetOrCompute(at.Group ?? at.Point));
                for (var i = 0; i < group.Count; i++)
                    if (group[i].Name == point)
                    {
                        group.RemoveAt(i);
                        break;
                    }

                group.Add(new Attachment(point, FacadeEditorDebug.ConstructMatrix(at.Offset, at.Rotation, at.Scale == 0 ? 1 : at.Scale)));
            }

            foreach (var attachmentPoint in mappingByGroup)
                _attachmentPoints.Add(attachmentPoint.Key, attachmentByGroup.TryGet(attachmentPoint.Key, out var attachments)
                    ? new AttachmentGroup(attachments, attachmentPoint.Value)
                    : new AttachmentGroup(new List<Attachment> { new Attachment(attachmentPoint.Key, Matrix.Identity) }, attachmentPoint.Value));

            _recentlyEquippedFacades = new FacadeLookup(recentlyEquipped.Distinct().ToList());
        }
    }

    /// <summary>
    /// Defines groups of model attachment associations that are filled with items from the entity's inventory according to the referenced facades.
    /// </summary>
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiInvertedVisualInventoryComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public class Attachment
        {
            /// <summary>
            /// Attachment point group name.
            /// </summary>
            [XmlAttribute]
            public string Group;

            /// <summary>
            /// Name of the attachment point to attach to.
            /// </summary>
            [XmlAttribute]
            public string Point;

            /// <summary>
            /// Natural offset of the attachment point.
            /// </summary>
            [XmlElement]
            public SerializableVector3 Offset;

            /// <summary>
            /// Natural rotation of the attachment point.
            /// </summary>
            [XmlElement]
            public SerializableVector3 Rotation;

            /// <summary>
            /// Natural scale of the attachment point.
            /// </summary>
            [XmlElement]
            public float Scale;
        }

        public class Mapping
        {
            /// <summary>
            /// Name of the attachment point or attachment point group to attach to.
            /// </summary>
            [XmlAttribute]
            public string Group;

            /// <summary>
            /// Facade definition to use.
            /// </summary>
            [XmlAttribute]
            public string Facade;

            /// <summary>
            /// Facade tag to use.
            /// </summary>
            [XmlAttribute]
            public string FacadeTag;

            /// <summary>
            /// Subtype of the inventory to watch, or null to watch the default inventory.
            /// </summary>
            [XmlAttribute]
            public string Inventory;

            /// <summary>
            /// Entity container ID of the entity to generate for this mapping.
            /// </summary>
            public SerializableDefinitionId? Entity;

            /// <summary>
            /// Determines the priority of this mapping. The highest priority mapping that matches an item will be shown.
            /// </summary>
            [XmlAttribute]
            public float Priority;

            /// <summary>
            /// Only consider the most recently equipped items.
            /// </summary>
            [XmlAttribute]
            public bool RecentlyEquipped;
        }

        /// <summary>
        /// Default inventory to watch for mappings, or empty string to watch all by default.
        /// </summary>
        [XmlElement("DefaultInventory")]
        public string DefaultInventory;

        /// <summary>
        /// Associate a new attachment point and transformation with an attachment group. 
        /// </summary>
        [XmlElement("Attachment")]
        public Attachment[] Attachments;

        /// <summary>
        /// Associates an inventory and item facade with an attachment group. 
        /// </summary>
        [XmlElement("Mapping")]
        public Mapping[] Mappings;
    }
}