using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.BlockGeneration;
using Medieval.Entities.Block;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Components.Entity.CubeGrid;
using VRage.Definitions.Components;
using VRage.Entity.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    [ReplicatedComponent]
    [MyComponent(typeof(MyObjectBuilder_EquiGridModifierComponent))]
    [MyDependency(typeof(MyGridDataComponent), Critical = true)]
    [MyDependency(typeof(MyGridConnectivityComponent), Critical = false)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = false)]
    [MyDependency(typeof(MyGridHierarchyComponent), Critical = false)]
    public class EquiGridModifierComponent : EquiModifierStorageComponent<EquiGridModifierComponent.BlockModifierKey,
        MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey,
        MyObjectBuilder_EquiGridModifierComponent.BlockModifierSeed>
    {
        public struct BlockModifierKey : IEquatable<BlockModifierKey>, IModifierRtKey<MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey>
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

        [Automatic]
        private readonly MyGridConnectivityComponent _gridConnectivity = null;

        public override void OnAddedToScene()
        {
            if (_gridData.BlockCount < Modifiers.Count)
                RemoveExtraModifiers();

            base.OnAddedToScene();

            _gridData.BlockAdded += BlockAdded;
            _gridData.BlockRemoved += BlockRemoved;
            _gridData.BlockChanged += BlockChanged;
            _gridData.BeforeMerge += BeforeMerge;
            if (_gridConnectivity != null)
                _gridConnectivity.BeforeGridSplit += BeforeSplit;
            if (_gridRender != null)
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;
        }

        protected override bool TryGetParent(in BlockModifierKey key, out BlockModifierKey parent)
        {
            if (key.AttachmentPoint != MyStringHash.NullOrEmpty)
            {
                parent = new BlockModifierKey(key.Block, MyStringHash.NullOrEmpty);
                return true;
            }

            var blockObj = _gridData.GetBlock(key.Block);
            if (blockObj is MyGeneratedBlock genBlock)
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

            var attachedModels = _gridHierarchy.GetBlockEntity(parent.Block)?.Definition.Get<MyModelAttachmentComponentDefinition>();
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

            var entity = _gridHierarchy.GetBlockEntity(key.Block);
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

        protected override void ApplyOutput(in BlockModifierKey key, in ModifierContext context, in ModifierOutput output)
        {
            var blockObj = _gridData.GetBlock(key.Block);
            if (blockObj == null)
                return;

            if (key.AttachmentPoint == MyStringHash.NullOrEmpty)
            {
                EquiModifierOutputHelpers.Apply(in output, blockObj, _gridData, _gridRender);
                return;
            }

            var attached = _gridHierarchy.GetBlockEntity(key.Block)?.Get<MyModelAttachmentComponent>()?.GetAttachedEntities(key.AttachmentPoint);
            if (!attached.HasValue || attached.Value.Count == 0)
                return;
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
                }
            }
        }

        protected override void RaiseAddModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier, string data)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, (x) => x.AddModifierInternalNet, key.ToObjectBuilder(), (SerializableDefinitionId) modifier, data);
        }

        protected override void RaiseUpdateModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier, string data)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, (x) => x.UpdateModifierInternalNet, key.ToObjectBuilder(), (SerializableDefinitionId) modifier, data);
        }

        protected override void RaiseRemoveModifierInternal(in BlockModifierKey key, in MyDefinitionId modifier)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, (x) => x.RemoveModifierInternalNet, key.ToObjectBuilder(), (SerializableDefinitionId) modifier);
        }

        [Event, Server, Broadcast, Reliable]
        private void AddModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id, string data)
        {
            AddModifierInternal(key.ToRuntime(), id, data);
        }

        [Event, Server, Broadcast, Reliable]
        private void UpdateModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id, string data)
        {
            UpdateModifierInternal(key.ToRuntime(), id, data);
        }

        [Event, Server, Broadcast, Reliable]
        private void RemoveModifierInternalNet(MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey key, SerializableDefinitionId id)
        {
            RemoveModifierInternal(key.ToRuntime(), id);
        }

        public override void OnRemovedFromScene()
        {
            _gridData.BlockAdded -= BlockAdded;
            _gridData.BlockRemoved -= BlockRemoved;
            _gridData.BlockChanged -= BlockChanged;
            _gridData.BeforeMerge -= BeforeMerge;
            if (_gridConnectivity != null)
                _gridConnectivity.BeforeGridSplit -= BeforeSplit;
            base.OnRemovedFromScene();
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
            if (_gridData.TryGetBlock(block, out var blockObj))
                ApplyModifiers(new BlockModifierKey(block, MyStringHash.NullOrEmpty));
        }

        private void BlockRemoved(MyBlock block, MyGridDataComponent grid)
        {
            using (Lock.AcquireExclusiveUsing())
                RemoveOrMoveInternal(new BlockModifierKey(block.Id, MyStringHash.NullOrEmpty), null);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiGridModifierComponent : MyObjectBuilder_EquiModifierStorageComponent<
        MyObjectBuilder_EquiGridModifierComponent.BlockModifierKey,
        MyObjectBuilder_EquiGridModifierComponent.BlockModifierSeed>
    {
        public struct BlockModifierKey : IModifierObKey<EquiGridModifierComponent.BlockModifierKey>
        {
            [XmlAttribute("Block")]
            public ulong Block;

            [XmlAttribute("Point")]
            public string AttachmentPoint;

            public bool ShouldSerializedAttachmentPoint() => !string.IsNullOrEmpty(AttachmentPoint);

            public EquiGridModifierComponent.BlockModifierKey ToRuntime()
            {
                return new EquiGridModifierComponent.BlockModifierKey(Block, MyStringHash.GetOrCompute(AttachmentPoint));
            }
        }

        public struct BlockModifierSeed : IModifierObSeed<BlockModifierKey>
        {
            [XmlAttribute("Block")]
            public ulong Block;

            [XmlAttribute("Point")]
            public string AttachmentPoint;

            public bool ShouldSerializedAttachmentPoint() => !string.IsNullOrEmpty(AttachmentPoint);

            [XmlAttribute("Data")]
            public string Data;

            BlockModifierKey IModifierObSeed<BlockModifierKey>.Key
            {
                get => new BlockModifierKey {Block = Block, AttachmentPoint = AttachmentPoint};
                set
                {
                    Block = value.Block;
                    AttachmentPoint = value.AttachmentPoint;
                }
            }

            string IModifierObSeed<BlockModifierKey>.Data
            {
                get => Data;
                set => Data = value;
            }
        }
    }
}