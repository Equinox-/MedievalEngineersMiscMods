using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.ModelGenerator;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.BlockGeneration;
using Medieval.Entities.Block;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
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
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    [ReplicatedComponent]
    [MyComponent(typeof(MyObjectBuilder_EquiGridModifierComponentBackup))]
    [MyDependency(typeof(MyGridDataComponent), Critical = true)]
    [MyDependency(typeof(MyGridConnectivityComponent), Critical = false)]
    [MyDependency(typeof(MyRenderComponentGrid), Critical = false)]
    public class EquiGridModifierComponentBackup : MyEntityComponent, IMyEventProxy
    {
        private readonly FastResourceLock _lock = new FastResourceLock();

        private readonly Dictionary<BlockId, InterningBag<EquiModifierBaseDefinition>> _modifiers =
            new Dictionary<BlockId, InterningBag<EquiModifierBaseDefinition>>();

        private readonly Dictionary<ModifierDataKey, IModifierData> _modifierData = new Dictionary<ModifierDataKey, IModifierData>();


        public delegate void ModifiersAppliedDelegate(EquiGridModifierComponentBackup owner, BlockId block);

        public event ModifiersAppliedDelegate ModifiersApplied;

        private struct ModifierDataKey : IEquatable<ModifierDataKey>
        {
            public readonly BlockId Block;
            public readonly MyDefinitionId Modifier;

            public ModifierDataKey(BlockId block, MyDefinitionId modifier)
            {
                Block = block;
                Modifier = modifier;
            }

            public bool Equals(ModifierDataKey other)
            {
                return Block.Value.Equals(other.Block.Value) && Modifier.Equals(other.Modifier);
            }

            public override bool Equals(object obj)
            {
                return obj is ModifierDataKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Block.GetHashCode() * 397) ^ Modifier.GetHashCode();
                }
            }
        }

        [Automatic]
        private readonly MyGridDataComponent _gridData = null;

        [Automatic]
        private readonly MyRenderComponentGrid _gridRender = null;

        [Automatic]
        private readonly MyGridConnectivityComponent _gridConnectivity = null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (_gridData.BlockCount < _modifiers.Count)
                RemoveExtraModifiers();

            _gridData.BlockAdded += BlockAdded;
            _gridData.BlockRemoved += BlockRemoved;
            _gridData.BlockChanged += BlockChanged;
            _gridData.BeforeMerge += BeforeMerge;
            if (_gridConnectivity != null)
                _gridConnectivity.BeforeGridSplit += BeforeSplit;
            if (_gridRender != null)
                _gridRender.BlockRenderablesChanged += BlockRenderablesChanged;

            using (_lock.AcquireSharedUsing())
            {
                foreach (var blockId in _modifiers.Keys)
                    if (_gridData.TryGetBlock(blockId, out var block))
                        DispatchModifiersChanged(block);
            }
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

        private void RemoveExtraModifiers()
        {
            using (_lock.AcquireExclusiveUsing())
            {
                using (PoolManager.Get(out List<BlockId> toRemove))
                {
                    foreach (var id in _modifiers.Keys)
                        if (!_gridData.TryGetBlock(id, out var block) || block is MyGeneratedBlock)
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                        _modifiers.Remove(rem);
                }

                using (PoolManager.Get(out List<ModifierDataKey> toRemove))
                {
                    foreach (var id in _modifierData.Keys)
                        if (!_gridData.TryGetBlock(id.Block, out var block) || block is MyGeneratedBlock)
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                        _modifierData.Remove(rem);
                }
            }
        }

        private InterningBag<EquiModifierBaseDefinition> GetModifiersUnsafe(BlockId block)
        {
            if (_gridData.TryGetBlock(block, out var blockObj) && blockObj is MyGeneratedBlock genBlock)
                block = genBlock.ParentBlock;
            return _modifiers.GetValueOrDefault(block, InterningBag<EquiModifierBaseDefinition>.Empty);
        }

        public InterningBag<EquiModifierBaseDefinition> GetModifiers(BlockId block)
        {
            using (_lock.AcquireSharedUsing())
                return GetModifiersUnsafe(block);
        }

        public void AddModifier(MyBlock block, EquiModifierBaseDefinition modifier)
        {
            var ctx = new ModifierContext(_gridData, block, GetModifiers(block.Id));
            if (!modifier.CanApply(in ctx))
                return;
            var modifierData = modifier.CreateData(in ctx);
            MyAPIGateway.Multiplayer.RaiseEvent(this, a => a.AddModifierInternal, block.Id, (SerializableDefinitionId) modifier.Id,
                modifierData?.Serialize() ?? "");
        }

        public void RemoveModifier(MyBlock block, EquiModifierBaseDefinition modifier)
        {
            MyAPIGateway.Multiplayer.RaiseEvent(this, a => a.RemoveModifierInternal, block.Id, (SerializableDefinitionId) modifier.Id);
        }

        [Event, Server, Broadcast, Reliable]
        private void AddModifierInternal(BlockId block, SerializableDefinitionId modifier, string data)
        {
            var modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null || (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this)) ||
                !_gridData.TryGetBlock(block, out var blockObj))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (blockObj is MyGeneratedBlock genBlock)
            {
                block = genBlock.ParentBlock;
                if (!_gridData.TryGetBlock(block, out blockObj))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            var ctx = new ModifierContext(_gridData, blockObj, GetModifiers(block));
            if (!modifierDef.CanApply(in ctx))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            using (_lock.AcquireExclusiveUsing())
            {
                var mods = GetModifiersUnsafe(block);
                var edited = mods.With(modifierDef);
                if (mods.Equals(edited))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                foreach (var k in mods)
                    if (modifierDef.ShouldEvict(k))
                    {
                        _modifierData.Remove(new ModifierDataKey(block, k.Id));
                        edited = edited.Without(k);
                    }

                RemoveOrphanedModifiers(ref edited, blockObj);

                _modifiers[block] = edited;
                if (!string.IsNullOrEmpty(data))
                    _modifierData[new ModifierDataKey(block, modifier)] = modifierDef.CreateData(data);
            }

            DispatchModifiersChanged(blockObj);
        }

        [Event, Server, Broadcast, Reliable]
        private void RemoveModifierInternal(BlockId block, SerializableDefinitionId modifier)
        {
            var modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null || (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this)) ||
                !_gridData.TryGetBlock(block, out var blockObj))
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (blockObj is MyGeneratedBlock genBlock)
            {
                block = genBlock.ParentBlock;
                if (!_gridData.TryGetBlock(block, out blockObj))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }
            }

            using (_lock.AcquireExclusiveUsing())
            {
                var mods = GetModifiersUnsafe(block);
                var edited = mods.Without(modifierDef);
                if (ReferenceEquals(edited, mods))
                {
                    MyEventContext.ValidationFailed();
                    return;
                }

                _modifierData.Remove(new ModifierDataKey(block, modifier));
                RemoveOrphanedModifiers(ref edited, blockObj);

                if (edited.Count > 0)
                    _modifiers[block] = edited;
                else
                    _modifiers.Remove(block);
            }

            if (_gridData.TryGetBlock(block, out var obj))
                DispatchModifiersChanged(obj);
        }

        /// <summary>
        /// Removes modifiers missing their dependencies 
        /// </summary>
        private void RemoveOrphanedModifiers(ref InterningBag<EquiModifierBaseDefinition> input, MyBlock block)
        {
            while (true)
            {
                var ctx = new ModifierContext(_gridData, block, input);
                var edited = input;
                foreach (var v in input)
                    if (!v.CanApply(in ctx))
                    {
                        edited = edited.Without(v);
                        _modifierData.Remove(new ModifierDataKey(block.Id, v.Id));
                    }

                if (ReferenceEquals(input, edited))
                    break;
                input = edited;
            }
        }

        private static void BeforeMerge(MyGridDataComponent selfData, MyGridDataComponent otherData, List<MyBlock> selfBlocks, List<MyBlock> otherBlocks)
        {
            var otherModifiers = otherData.Container.Get<EquiGridModifierComponentBackup>();
            var selfModifiers = selfData.Container.Get<EquiGridModifierComponentBackup>();
            using (otherModifiers._lock.AcquireSharedUsing())
            using (selfModifiers._lock.AcquireExclusiveUsing())
            {
                foreach (var kv in otherModifiers._modifiers)
                    selfModifiers._modifiers[kv.Key] = kv.Value;
                foreach (var kv in otherModifiers._modifierData)
                    selfModifiers._modifierData[kv.Key] = kv.Value;
            }
        }

        private static void BeforeSplit(MyEntity sourceEntity, MyEntity destEntity, List<MyBlock> blocksMoved)
        {
            var source = sourceEntity.Get<EquiGridModifierComponentBackup>();
            var dest = destEntity.Get<EquiGridModifierComponentBackup>();
            using (source._lock.AcquireExclusiveUsing())
            using (dest._lock.AcquireExclusiveUsing())
            {
                foreach (var block in blocksMoved)
                {
                    if (!source._modifiers.TryGetValue(block.Id, out var modifiers))
                        continue;
                    source._modifiers.Remove(block.Id);
                    dest._modifiers[block.Id] = modifiers;
                    foreach (var mod in modifiers)
                    {
                        var key = new ModifierDataKey(block.Id, mod.Id);
                        if (!source._modifierData.TryGetValue(key, out var data))
                            continue;
                        source._modifierData.Remove(key);
                        dest._modifierData[key] = data;
                    }
                }
            }
        }

        private void BlockChanged(MyBlock block, MyGridDataComponent grid)
        {
            ApplyModifiers(block);
        }

        private void BlockAdded(MyBlock block, MyGridDataComponent grid)
        {
            ApplyModifiers(block);
        }

        private void BlockRenderablesChanged(MyRenderComponentGrid owner, BlockId block, ListReader<uint> renderobjects)
        {
            if (_gridData.TryGetBlock(block, out var blockObj))
                ApplyModifiers(blockObj);
        }

        private void BlockRemoved(MyBlock block, MyGridDataComponent grid)
        {
            using (_lock.AcquireExclusiveUsing())
            {
                if (!_modifiers.TryGetValue(block.Id, out var state))
                    return;
                _modifiers.Remove(block.Id);
                foreach (var modifier in state)
                    _modifierData.Remove(new ModifierDataKey(block.Id, modifier.Id));
            }
        }

        /// <summary>
        /// Modifiers changed, we need to apply them to this block AND its generated pieces.
        /// </summary>
        private void DispatchModifiersChanged(MyBlock invoker)
        {
            ApplyModifiers(invoker);

            if (invoker is MyGeneratedBlock)
                return;

            var additionalBlocks = MyDefinitionManager.Get<MyAdditionalBlocksDefinition>(invoker.DefinitionId);
            if (additionalBlocks == null)
                return;

            using (PoolManager.Get(out List<MyBlock> blocks))
            {
                _gridData.GetBlocks(invoker.Position, blocks);
                var localTrans = _gridData.GetBlockLocalMatrix(invoker);
                var rotation = Quaternion.CreateFromForwardUp(localTrans.Forward, localTrans.Up);
                foreach (var generatedBlock in additionalBlocks.GeneratedBlockItems)
                {
                    if (generatedBlock.Value.BlockPosition == null)
                        continue;
                    var rotPosToCheck = Vector3I.Transform(generatedBlock.Value.BlockPosition.Value, rotation);
                    _gridData.GetBlocks(invoker.Position + rotPosToCheck, blocks);
                }

                foreach (var block in blocks)
                    if (block is MyGeneratedBlock genBlock && genBlock.ParentBlock == invoker.Id)
                        ApplyModifiers(block);
            }
        }

        private bool _applyingModifiers;

        private void ApplyModifiers(MyBlock block)
        {
            if (_applyingModifiers)
                return;
            try
            {
                _applyingModifiers = true;
                var result = new ModifierOutput();
                using (_lock.AcquireSharedUsing())
                {
                    var ctx = new ModifierContext(_gridData, block, GetModifiersUnsafe(block.Id));
                    result.Model = ctx.OriginalModel;
                    foreach (var modifier in ctx.Modifiers)
                        modifier.Apply(in ctx, _modifierData.GetValueOrDefault(new ModifierDataKey(block.Id, modifier.Id)), ref result);
                }

                if (result.Model != null)
                {
                    var model = MyModels.GetModelOnlyData(result.Model);
                    if (model != null)
                        _gridData.ChangeModel(block, model);
                }

                var colorMask = result.ColorMask ?? Vector3.Zero;
                if (_gridRender != null)
                {
                    foreach (var renderable in _gridRender.GetBlockRenderObjectIDs(block.Id))
                        MyRenderProxy.UpdateRenderEntity(renderable, null, colorMask);
                }

                ModifiersApplied?.Invoke(this, block.Id);
            }
            finally

            {
                _applyingModifiers = false;
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiGridModifierComponentBackup) base.Serialize(copy);
            var tmpModifiers = new Dictionary<InterningBag<EquiModifierBaseDefinition>, List<ulong>>();
            var tmpDataSets = new Dictionary<MyDefinitionId, List<MyObjectBuilder_EquiGridModifierComponentBackup.DataSet.BlockSeed>>();

            using (_lock.AcquireSharedUsing())
            {
                foreach (var block in _modifiers)
                {
                    if (!tmpModifiers.TryGetValue(block.Value, out var list))
                        tmpModifiers[block.Value] = list = new List<ulong>();
                    list.Add(block.Key.Value);
                }

                foreach (var block in _modifierData)
                {
                    if (!tmpDataSets.TryGetValue(block.Key.Modifier, out var list))
                        tmpDataSets[block.Key.Modifier] = list = new List<MyObjectBuilder_EquiGridModifierComponentBackup.DataSet.BlockSeed>();
                    list.Add(new MyObjectBuilder_EquiGridModifierComponentBackup.DataSet.BlockSeed
                    {
                        Block = block.Key.Block.Value,
                        Data = block.Value.Serialize()
                    });
                }
            }

            ob.Modifiers = tmpModifiers.Select(bb => new MyObjectBuilder_EquiGridModifierComponentBackup.ModifierSet
            {
                Blocks = bb.Value.ToArray(),
                Modifiers = bb.Key.Select(x => (SerializableDefinitionId) x.Id).ToArray()
            }).ToArray();
            ob.Storage = tmpDataSets.Select(bb => new MyObjectBuilder_EquiGridModifierComponentBackup.DataSet
            {
                Modifier = bb.Key,
                Blocks = bb.Value.ToArray()
            }).ToArray();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiGridModifierComponentBackup) builder;
            using (_lock.AcquireExclusiveUsing())
            {
                _modifiers.Clear();
                _modifierData.Clear();

                if (ob.Modifiers != null)
                    foreach (var mod in ob.Modifiers)
                        if (mod.Blocks != null && mod.Modifiers != null && mod.Modifiers.Length > 0 && mod.Blocks.Length > 0)
                        {
                            var bag = InterningBag<EquiModifierBaseDefinition>.Of(mod.Modifiers.Select(x =>
                                MyDefinitionManager.Get<EquiModifierBaseDefinition>(x)));
                            foreach (var block in mod.Blocks)
                            {
                                _modifiers[block] = bag;
                            }
                        }

                if (ob.Storage != null)
                    foreach (var dataSets in ob.Storage)
                        if (dataSets.Blocks != null)
                        {
                            var definition = MyDefinitionManager.Get<EquiModifierBaseDefinition>(dataSets.Modifier);
                            foreach (var data in dataSets.Blocks)
                                if (!string.IsNullOrEmpty(data.Data))
                                    _modifierData[new ModifierDataKey(data.Block, dataSets.Modifier)] = definition.CreateData(data.Data);
                        }
            }
        }

        public override bool IsSerialized => _modifiers.Count > 0;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiGridModifierComponentBackup : MyObjectBuilder_EntityComponent
    {
        [XmlElement("Modifiers")]
        public ModifierSet[] Modifiers;

        public struct ModifierSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId[] Modifiers;

            [XmlElement("Block")]
            public ulong[] Blocks;
        }

        [XmlElement("Storage")]
        public DataSet[] Storage;

        public struct DataSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId Modifier;

            [XmlElement("Block")]
            public BlockSeed[] Blocks;

            public struct BlockSeed
            {
                [XmlAttribute("Id")]
                public ulong Block;

                [XmlAttribute("Data")]
                public string Data;
            }
        }
    }
}