using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.BlockGeneration;
using Medieval.Entities.Block;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Block;
using VRage.Components.Entity;
using VRage.Components.Entity.Camera;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Components;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    [ReplicatedComponent]
    [MyComponent(typeof(MyObjectBuilder_EquiGridModifierComponent))]
    [MyDependency(typeof(MyGridDataComponent), Critical = true)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = true)]
    [MyDependency(typeof(MyGridHierarchyComponent), Critical = false)]
    // Forward dependency on the physics shape to avoid creating the physics shape before all the models get changed by modifiers.
    [MyForwardDependency(typeof(MyGridPhysicsShapeComponent), Critical = false)]
    public class EquiGridModifierComponent : EquiModifierStorageComponent<EquiGridModifierComponent.BlockModifierKey,
        MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey>, IComponentDebugDraw
    {
        public readonly struct BlockModifierKey : IEquatable<BlockModifierKey>, IModifierRtKey<MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey>
        {
            public readonly BlockId Block;
            public readonly MyStringHash AttachmentPoint;

            public BlockModifierKey(BlockId block, MyStringHash attachmentPoint)
            {
                Block = block;
                AttachmentPoint = attachmentPoint;
            }

            public bool Equals(BlockModifierKey other)
            {
                return Block.Equals(other.Block) && AttachmentPoint.Equals(other.AttachmentPoint);
            }

            public MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey ToObjectBuilder()
            {
                return new MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey
                {
                    Block = Block.Value,
                    AttachmentPoint = AttachmentPoint.String
                };
            }

            public override bool Equals(object obj)
            {
                return obj is BlockModifierKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Block.GetHashCode() * 397) ^ AttachmentPoint.GetHashCode();
                }
            }

            public override string ToString()
            {
                return AttachmentPoint != MyStringHash.NullOrEmpty
                    ? $"{nameof(Block)}: {Block}, {nameof(AttachmentPoint)}: {AttachmentPoint}"
                    : $"{nameof(Block)}: {Block}";
            }
        }

        [Automatic]
        private readonly MyGridDataComponent _gridData = null;

        [Automatic]
        private readonly MyRenderComponentGrid _gridRender = null;

        [Automatic]
        private readonly MyGridHierarchyComponent _gridHierarchy = null;

        // Weak dependency without ordering requirements, so not marked with MyDependency
        [Automatic]
        private readonly MyGridConnectivityComponent _gridConnectivity = null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            _gridData.BlockAdded += BlockAdded;
            _gridData.BlockRemoved += BlockRemoved;
            _gridData.BlockChanged += BlockChanged;
            _gridData.BeforeMerge += BeforeMerge;
            if (_gridConnectivity != null)
                _gridConnectivity.BeforeGridSplit += BeforeSplit;
            if (_gridRender != null)
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
            if (_gridHierarchy != null)
            {
                foreach (var child in _gridHierarchy.Children)
                    if (child.Entity != null)
                        BlockEntityAdded(child.Entity);
                _gridHierarchy.ChildAdded += BlockEntityAdded;
                _gridHierarchy.ChildRemoved += BlockEntityRemoved;
            }
            
            AddScheduledCallback(RemoveExtraModifiers, 1000L);
        }

        [Update(false)]
        protected override void RemoveExtraModifiers(long dt)
        {
            base.RemoveExtraModifiers(dt);
        }

        public override void OnRemovedFromScene()
        {
            _gridData.BlockAdded -= BlockAdded;
            _gridData.BlockRemoved -= BlockRemoved;
            _gridData.BlockChanged -= BlockChanged;
            _gridData.BeforeMerge -= BeforeMerge;
            if (_gridConnectivity != null)
                _gridConnectivity.BeforeGridSplit -= BeforeSplit;
            if (_gridRender != null)
                _gridRender.BlockRenderablesChanged -= BlockRenderablesChanged;
            if (_gridHierarchy != null)
            {
                _gridHierarchy.ChildAdded -= BlockEntityAdded;
                _gridHierarchy.ChildRemoved -= BlockEntityRemoved;
            }

            base.OnRemovedFromScene();
        }

        protected override bool TryGetParent(in BlockModifierKey key, out BlockModifierKey parent)
        {
            if (key.AttachmentPoint != MyStringHash.NullOrEmpty)
            {
                parent = new BlockModifierKey(key.Block, MyStringHash.NullOrEmpty);
                return true;
            }

            if (_gridData?.GetBlock(key.Block) is MyGeneratedBlock genBlock)
            {
                parent = new BlockModifierKey(genBlock.ParentBlock, MyStringHash.NullOrEmpty);
                return true;
            }

            parent = default;
            return false;
        }

        protected override void GetChildren(in BlockModifierKey parent, List<BlockModifierKey> children)
        {
            if (parent.AttachmentPoint != MyStringHash.NullOrEmpty)
                return;

            var blockObj = _gridData.GetBlock(parent.Block);
            if (blockObj == null)
                return;

            var additionalBlocks = MyDefinitionManager.Get<MyAdditionalBlocksDefinition>(blockObj.DefinitionId);
            if (additionalBlocks != null)
            {
                using (PoolManager.Get(out List<MyBlock> blocks))
                {
                    _gridData.GetBlocks(blockObj.Position, blocks);
                    var localTrans = _gridData.GetBlockLocalMatrix(blockObj);
                    var rotation = Quaternion.CreateFromForwardUp(localTrans.Forward, localTrans.Up);
                    foreach (var generatedBlock in additionalBlocks.GeneratedBlockItems)
                    {
                        if (generatedBlock.Value.BlockPosition == null)
                            continue;
                        var rotPosToCheck = Vector3I.Transform(generatedBlock.Value.BlockPosition.Value, rotation);
                        _gridData.GetBlocks(blockObj.Position + rotPosToCheck, blocks);
                    }

                    foreach (var block in blocks)
                        if (block is MyGeneratedBlock genBlock && genBlock.ParentBlock == blockObj.Id)
                            children.Add(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty));
                }
            }

            var attachedModels = _gridHierarchy?.GetBlockEntity(parent.Block)?.Definition?.Get<MyModelAttachmentComponentDefinition>();
            if (attachedModels != null)
            {
                foreach (var point in attachedModels.AttachmentPoints.Keys)
                    children.Add(new BlockModifierKey(parent.Block, point));
            }
        }

        public override bool TryCreateContext(in BlockModifierKey key, InterningBag<EquiModifierBaseDefinition> modifiers, out ModifierContext context)
        {
            var blockObj = _gridData.GetBlock(key.Block);
            if (blockObj == null)
            {
                context = default;
                return false;
            }

            if (key.AttachmentPoint == MyStringHash.NullOrEmpty)
            {
                context = new ModifierContext(_gridData, blockObj, modifiers);
                return true;
            }

            var entity = _gridHierarchy?.GetBlockEntity(key.Block);
            var attachment = entity?.Get<MyModelAttachmentComponent>();
            if (attachment == null)
            {
                context = default;
                return false;
            }

            var attached = attachment.GetAttachedEntities(key.AttachmentPoint);
            if (attached.Count == 0)
            {
                context = default;
                return false;
            }

            using (var e = attached.GetEnumerator())
            {
                if (!e.MoveNext())
                {
                    context = default;
                    return false;
                }

                context = new ModifierContext(e.Current, modifiers);
                return true;
            }
        }

        protected override bool KeyExists(in BlockModifierKey key)
        {
            var blockObj = _gridData.GetBlock(key.Block);
            if (blockObj == null)
                return false;
            if (key.AttachmentPoint == MyStringHash.NullOrEmpty)
                return true;

            var containerDef = MyDefinitionManager.Get<MyContainerDefinition>(blockObj.DefinitionId);
            return containerDef?.Get<MyModelAttachmentComponentDefinition>()?.AttachmentPoints.ContainsKey(key.AttachmentPoint) ?? false;
        }

        protected override void ApplyOutput(in BlockModifierKey key, in ModifierContext context, in ModifierOutput output)
        {
            var blockObj = _gridData.GetBlock(key.Block);
            if (blockObj == null)
            {
                if (DebugFlags.Debug(typeof(EquiGridModifierComponent)))
                {
                    this.GetLogger().Warning($"Attempted to apply modifier output for {key}@{context.Modifiers} -> {output} but found no block with that ID");
                }

                return;
            }

            if (key.AttachmentPoint == MyStringHash.NullOrEmpty)
            {
                EquiModifierOutputHelpers.Apply(in output, blockObj, _gridData, _gridRender);
                return;
            }

            var attached = _gridHierarchy?.GetBlockEntity(key.Block)?.Get<MyModelAttachmentComponent>()?.GetAttachedEntities(key.AttachmentPoint);
            if (!attached.HasValue || attached.Value.Count == 0)
            {
                if (DebugFlags.Debug(typeof(EquiGridModifierComponent)))
                {
                    this.GetLogger().Warning($"Attempted to apply modifier output for {key}@{context.Modifiers} -> {output} but found no matching attachments");
                }

                return;
            }

            foreach (var ent in attached.Value)
            {
                var ctx = new ModifierContext(ent, context.Modifiers);
                if (ctx.OriginalModel == context.OriginalModel)
                {
                    EquiModifierOutputHelpers.Apply(in output, ent);
                }
                else
                {
                    GenerateModifierOutput(in key, in ctx, out var specializedOutput);
                    EquiModifierOutputHelpers.Apply(in specializedOutput, ent);
                    specializedOutput.MaterialEditsBuilder?.Dispose();
                }
            }
        }

        protected override void RaiseAddModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier, string data, bool recursive)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.AddModifierInternalNet,
                key.ToObjectBuilder(), (SerializableDefinitionId) modifier, data, recursive);
        }

        protected override void RaiseUpdateModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier, string data, bool recursive)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.UpdateModifierInternalNet,
                key.ToObjectBuilder(), (SerializableDefinitionId) modifier, data, recursive);
        }

        protected override void RaiseRemoveModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier, bool recursive)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.RemoveModifierInternalNet,
                key.ToObjectBuilder(), (SerializableDefinitionId) modifier, recursive);
        }

        [Event, Server, Broadcast, Reliable]
        private void AddModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id, string data,
            bool recursive)
        {
            AddModifierInternal(key.ToRuntime(), id, data, recursive);
        }

        [Event, Server, Broadcast, Reliable]
        private void UpdateModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id, string data,
            bool recursive)
        {
            UpdateModifierInternal(key.ToRuntime(), id, data, recursive);
        }

        [Event, Server, Broadcast, Reliable]
        private void RemoveModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id, bool recursive)
        {
            RemoveModifierInternal(key.ToRuntime(), id, recursive);
        }

        private static void BeforeMerge(MyGridDataComponent selfData, MyGridDataComponent otherData, List<MyBlock> selfBlocks, List<MyBlock> otherBlocks)
        {
            var otherModifiers = otherData.Container.Get<EquiGridModifierComponent>();
            var selfModifiers = selfData.Container.Get<EquiGridModifierComponent>();
            using (otherModifiers.Lock.AcquireSharedUsing())
            using (selfModifiers.Lock.AcquireExclusiveUsing())
            {
                foreach (var kv in otherModifiers.Modifiers)
                    selfModifiers.Modifiers[kv.Key] = kv.Value;
                foreach (var kv in otherModifiers.ModifierData)
                    selfModifiers.ModifierData[kv.Key] = kv.Value;
            }
        }

        private static void BeforeSplit(MyEntity sourceEntity, MyEntity destEntity, List<MyBlock> blocksMoved)
        {
            var source = sourceEntity.Get<EquiGridModifierComponent>();
            var dest = destEntity.Get<EquiGridModifierComponent>();
            using (source.Lock.AcquireExclusiveUsing())
            using (dest.Lock.AcquireExclusiveUsing())
                foreach (var block in blocksMoved)
                    source.RemoveOrMoveInternal(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty), dest);
        }

        private void BlockChanged(MyBlock block, MyGridDataComponent grid)
        {
            ApplyModifiers(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty));
        }

        private void BlockAdded(MyBlock block, MyGridDataComponent grid)
        {
            ApplyModifiers(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty));
        }

        private void BlockRenderablesChanged(MyRenderComponentGrid owner, BlockId block, ListReader<uint> renderables)
        {
            if (_gridData.TryGetBlock(block, out _))
                ApplyModifiers(new BlockModifierKey(block, MyStringHash.NullOrEmpty));
        }

        private void BlockRemoved(MyBlock block, MyGridDataComponent grid)
        {
            using (Lock.AcquireExclusiveUsing())
                RemoveOrMoveInternal(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty), null);
        }

        private void BlockEntityAdded(MyEntity child)
        {
            var modelAttachmentComponent = child.Get<MyModelAttachmentComponent>();
            if (modelAttachmentComponent == null) return;
            // No need to call BlockEntityChildAttached for already existing children since it will be handled
            // during EquiModifierStorageComponent#OnAddedToScene 
            modelAttachmentComponent.OnEntityAttached += BlockEntityChildAttached;
        }

        private void BlockEntityRemoved(MyEntity child)
        {
            var modelAttachmentComponent = child.Get<MyModelAttachmentComponent>();
            if (modelAttachmentComponent == null) return;
            modelAttachmentComponent.OnEntityAttached -= BlockEntityChildAttached;
        }

        private void BlockEntityChildAttached(MyModelAttachmentComponent attachmentComponent, MyEntity entity)
        {
            var blockId = attachmentComponent.Container?.Get<MyBlockComponent>()?.BlockId;
            if (Entity == null || !Entity.InScene || _gridData == null || !blockId.HasValue || !_gridData.Contains(blockId.Value))
                return;
            var attachmentPoint = attachmentComponent.GetEntityAttachmentPoint(entity);
            if (attachmentPoint == MyStringHash.NullOrEmpty)
                return;
            ApplyModifiers(new BlockModifierKey(blockId.Value, attachmentPoint));
        }

        public void DebugDraw()
        {
            var cam = MyCameraComponent.ActiveCamera;
            var center = Entity.PositionComp.WorldAABB.Center;
            if (!cam.GetCameraFrustum().Intersects(new BoundingBoxD(center - 1, center + 1)))
                return;

            MyRenderProxy.DebugDrawText3D(
                center,
                $"M: {Modifiers.Count}\nMD: {ModifierData.Count}",
                Color.Cyan,
                0.5f);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiGridModifierComponent :
        MyObjectBuilder_EquiModifierStorageComponent<MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey>
    {
        public struct BlockModifierKey : IModifierObKey<EquiGridModifierComponent.BlockModifierKey>, IMyRemappable
        {
            [XmlAttribute("Block")]
            public ulong Block;

            [XmlAttribute("Point")]
            [Serialize(Flags = MyObjectFlags.Nullable)]
            public string AttachmentPoint;

            public bool ShouldSerializeAttachmentPoint() => !string.IsNullOrEmpty(AttachmentPoint);

            public EquiGridModifierComponent.BlockModifierKey ToRuntime()
            {
                return new EquiGridModifierComponent.BlockModifierKey(Block, MyStringHash.GetOrCompute(AttachmentPoint));
            }

            public void Remap(IMySceneRemapper remapper)
            {
                remapper.RemapObject(MyBlock.SceneType, ref Block);
            }
        }
    }
}