using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Util;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Library.Threading;
using VRage.Network;
using VRage.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    public abstract class EquiModifierStorageComponent<TRtKey, TObKey> : MyEntityComponent, IMyEventProxy
        where TRtKey : struct, IModifierRtKey<TObKey>, IEquatable<TRtKey>
        where TObKey : struct, IModifierObKey<TRtKey>, IMyRemappable
    {
        protected readonly FastResourceLock Lock = new FastResourceLock();

        protected readonly Dictionary<TRtKey, InterningBag<EquiModifierBaseDefinition>> Modifiers =
            new Dictionary<TRtKey, InterningBag<EquiModifierBaseDefinition>>();

        protected readonly Dictionary<ModifierDataKey, IModifierData> ModifierData = new Dictionary<ModifierDataKey, IModifierData>();


        public delegate void ModifiersAppliedDelegate(EquiModifierStorageComponent<TRtKey, TObKey> owner, TRtKey block);

        public event ModifiersAppliedDelegate ModifiersApplied;

        protected readonly struct ModifierDataKey : IEquatable<ModifierDataKey>
        {
            public readonly TRtKey Host;
            public readonly MyDefinitionId Modifier;

            public ModifierDataKey(TRtKey host, MyDefinitionId modifier)
            {
                Host = host;
                Modifier = modifier;
            }

            public bool Equals(ModifierDataKey other)
            {
                return Host.Equals(other.Host) && Modifier.Equals(other.Modifier);
            }

            public override bool Equals(object obj)
            {
                return obj is ModifierDataKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Host.GetHashCode() * 397) ^ Modifier.GetHashCode();
                }
            }

            public override string ToString()
            {
                return $"{nameof(Host)}: {Host}, {nameof(Modifier)}: {Modifier}";
            }
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            using (Lock.AcquireSharedUsing())
            using (PoolManager.Get(out List<TRtKey> children))
            using (PoolManager.Get(out HashSet<TRtKey> explored))
            {
                foreach (var id in Modifiers.Keys)
                    children.Add(id);

                while (children.Count > 0)
                {
                    var id = children[children.Count - 1];
                    children.RemoveAt(children.Count - 1);
                    if (!explored.Add(id))
                        continue;
                    GetChildren(id, children);
                    ApplyModifiers(id);
                }
            }
        }

        [Update(false)]
        protected void RemoveExtraModifiers(long dt)
        {
            if (Entity == null || !Entity.InScene)
                return;
            using (Lock.AcquireExclusiveUsing())
            {
                using (PoolManager.Get(out List<TRtKey> toRemove))
                {
                    foreach (var id in Modifiers.Keys)
                        if (!KeyExists(in id))
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                    {
                        if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                            this.GetLogger().Info($"Removing {rem} since context creation failed");
                        Modifiers.Remove(rem);
                    }
                }

                using (PoolManager.Get(out List<ModifierDataKey> toRemove))
                {
                    foreach (var id in ModifierData.Keys)
                        if (!KeyExists(in id.Host))
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                    {
                        if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                            this.GetLogger().Info($"Removing {rem} since context creation failed");
                        ModifierData.Remove(rem);
                    }
                }
            }
        }

        protected abstract bool TryGetParent(in TRtKey key, out TRtKey parent);

        protected abstract void GetChildren(in TRtKey parent, List<TRtKey> children);

        public abstract bool TryCreateContext(in TRtKey key, InterningBag<EquiModifierBaseDefinition> modifiers, out ModifierContext context);

        protected abstract bool KeyExists(in TRtKey key);

        protected abstract void ApplyOutput(in TRtKey key, in ModifierContext context, in ModifierOutput output);

        private InterningBag<EquiModifierBaseDefinition> GetModifiersUnsafe(in TRtKey block)
        {
            return Modifiers.TryGetValue(block, out var result) ? result : InterningBag<EquiModifierBaseDefinition>.Empty;
        }

        public InterningBag<EquiModifierBaseDefinition> GetModifiers(in TRtKey block)
        {
            using (Lock.AcquireSharedUsing())
                return GetModifiersUnsafe(block);
        }

        public void AddModifier(in TRtKey key, EquiModifierBaseDefinition modifier, IModifierData useData = null, bool recursive = false)
        {
            if (!TryCreateContext(in key, GetModifiers(in key), out var ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not adding {modifier.Id} / {useData} to {key} since context creation failed");
                return;
            }

            if (!modifier.CanApply(in ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not adding {modifier} / {useData} to {key} since it can't be applied to {ctx}");
                return;
            }

            var modifierData = (useData ?? modifier.CreateDefaultData(in ctx))?.Serialize() ?? "";
            RaiseAddModifierInternal(in key, in modifier.Id, modifierData, recursive);
        }

        public void UpdateModifierData(in TRtKey key, EquiModifierBaseDefinition modifier, IModifierData useData, bool recursive = false)
        {
            if (!GetModifiers(in key).Contains(modifier))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Can't update {modifier.Id} on {key} with {useData}");
                return;
            }

            RaiseUpdateModifierInternal(in key, modifier.Id, useData?.Serialize() ?? "", recursive);
        }

        public void RemoveModifier(in TRtKey key, EquiModifierBaseDefinition modifier, bool recursive = false)
        {
            if (!GetModifiers(in key).Contains(modifier))
                return;
            RaiseRemoveModifierInternal(in key, in modifier.Id, recursive);
        }

        protected abstract void RaiseAddModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data, bool recursive);

        private bool AddUpdateRemoveModifierSetup(in TRtKey key, in MyDefinitionId modifier,
            string op,
            out EquiModifierBaseDefinition modifierDef,
            out ModifierContext ctx)
        {
            modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null)
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not {op} {modifier} on {key} since {modifier} doesn't seem to exist");
                MyEventContext.ValidationFailed();
                ctx = default;
                return false;
            }

            if (!TryCreateContext(in key, GetModifiers(in key), out ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not {op} {modifier} on {key} since context creation failed");
                MyEventContext.ValidationFailed();
                return false;
            }

            if (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this, ctx.Position))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not {op} {modifier} on {key} since {MyEventContext.Current.Sender} isn't trusted");
                MyEventContext.ValidationFailed();
                return false;
            }

            return true;
        }

        protected void AddModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data, bool recursive)
        {
            if (!AddUpdateRemoveModifierSetup(in key, in modifier, "adding", out var modifierDef, out var ctx))
                return;

            if (!modifierDef.CanApply(in ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not adding {modifier} to {key} since it can't be applied to {ctx}");
                MyEventContext.ValidationFailed();
                return;
            }

            using (PoolManager.Get(out List<TRtKey> modifiedKeys))
            {
                using (Lock.AcquireExclusiveUsing())
                {
                    var modifiedData = string.IsNullOrEmpty(data) ? null : modifierDef.CreateData(data);
                    modifiedKeys.Add(key);
                    AddModifierUnsafe(in key, modifierDef, modifiedData);
                    if (recursive)
                        AddModifierRecursiveUnsafe(in key, modifierDef, modifiedData, modifiedKeys);
                }

                foreach (var modified in modifiedKeys)
                    ApplyModifiers(in modified);
            }
        }

        private void AddModifierRecursiveUnsafe(in TRtKey root, EquiModifierBaseDefinition modifier, IModifierData data, List<TRtKey> modifiedKeys)
        {
            using (PoolManager.Get(out List<TRtKey> children))
            {
                GetChildren(in root, children);
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    GetChildren(in child, children);
                    if (!TryCreateContext(in child, GetModifiersUnsafe(in child), out var childCtx) || !modifier.CanApply(in childCtx))
                        continue;
                    AddModifierUnsafe(in child, modifier, data);
                    modifiedKeys.Add(child);
                }
            }
        }

        private void AddModifierUnsafe(in TRtKey key, EquiModifierBaseDefinition modifier, IModifierData data)
        {
            var mods = GetModifiersUnsafe(key);
            var edited = mods.With(modifier);
            if (mods.Equals(edited))
            {
                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Adding {modifier.Id} / {data} to {key} was a no-op");
                MyEventContext.ValidationFailed();
                return;
            }

            if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                this.GetLogger().Info($"Adding {modifier.Id} / {data} to {key}");

            foreach (var k in mods)
                if (modifier.ShouldEvict(k))
                {
                    if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                        this.GetLogger().Info($"Evicting {k} from {key} while adding {modifier.Id}");
                    ModifierData.Remove(new ModifierDataKey(key, k.Id));
                    edited = edited.Without(k);
                }

            RemoveOrphanedModifiers(in key, ref edited);

            Modifiers[key] = edited;
            var dataKey = new ModifierDataKey(key, modifier.Id);
            if (data != null)
                ModifierData[dataKey] = data;
        }

        protected abstract void RaiseUpdateModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data, bool recursive);

        protected void UpdateModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data, bool recursive)
        {
            if (!AddUpdateRemoveModifierSetup(in key, in modifier, "updating", out var modifierDef, out var ctx))
                return;

            if (!ctx.Modifiers.Contains(modifierDef))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Not updating {modifier} on {key} since it doesn't exist");
                MyEventContext.ValidationFailed();
                return;
            }

            var dataObj = modifierDef.CreateData(data);
            using (PoolManager.Get(out List<TRtKey> modifiedKeys))
            {
                using (Lock.AcquireExclusiveUsing())
                {
                    ModifierData[new ModifierDataKey(key, modifier)] = dataObj;
                    modifiedKeys.Add(key);
                    if (recursive)
                        using (PoolManager.Get(out List<TRtKey> children))
                        {
                            GetChildren(in key, children);
                            // ReSharper disable once ForCanBeConvertedToForeach
                            for (var i = 0; i < children.Count; i++)
                            {
                                var child = children[i];
                                GetChildren(in child, children);
                                if (!GetModifiersUnsafe(in child).Contains(modifierDef)) continue;
                                ModifierData[new ModifierDataKey(child, modifier)] = dataObj;
                                modifiedKeys.Add(child);
                            }
                        }
                }

                foreach (var modified in modifiedKeys)
                    ApplyModifiers(in modified);
            }
        }

        protected abstract void RaiseRemoveModifierInternal(in TRtKey key, in MyDefinitionId modifier, bool recursive);

        protected void RemoveModifierInternal(in TRtKey key, in MyDefinitionId modifier, bool recursive)
        {
            if (!AddUpdateRemoveModifierSetup(in key, in modifier, "removing", out var modifierDef, out _))
                return;

            using (PoolManager.Get(out List<TRtKey> modifiedKeys))
            {
                using (Lock.AcquireExclusiveUsing())
                {
                    if (!RemoveModifierInternalUnsafe(in key, modifierDef))
                    {
                        MyEventContext.ValidationFailed();
                        return;
                    }
                    modifiedKeys.Add(key);

                    if (recursive)
                        RemoveModifierRecursiveUnsafe(in key, modifierDef, modifiedKeys);
                }

                foreach (var modified in modifiedKeys)
                    ApplyModifiers(in modified);
            }
        }

        private void RemoveModifierRecursiveUnsafe(in TRtKey root, EquiModifierBaseDefinition modifier, List<TRtKey> modifiedKeys)
        {
            using (PoolManager.Get(out List<TRtKey> children))
            {
                GetChildren(in root, children);
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    GetChildren(in child, children);
                    if (RemoveModifierInternalUnsafe(in child, modifier))
                        modifiedKeys.Add(child);
                }
            }
        }

        private bool RemoveModifierInternalUnsafe(in TRtKey key, EquiModifierBaseDefinition modifier)
        {
            var mods = GetModifiersUnsafe(in key);
            var edited = mods.Without(modifier);
            if (ReferenceEquals(edited, mods))
            {
                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Removing {modifier.Id} from {key} was a no-op");
                return false;
            }

            if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                this.GetLogger().Info($"Removing {modifier.Id} from {key}");

            ModifierData.Remove(new ModifierDataKey(key, modifier.Id));
            RemoveOrphanedModifiers(in key, ref edited);

            if (edited.Count > 0)
                Modifiers[key] = edited;
            else
                Modifiers.Remove(key);
            return true;
        }

        // No locking
        protected void RemoveOrMoveInternal(in TRtKey root, EquiModifierStorageComponent<TRtKey, TObKey> other)
        {
            using (PoolManager.Get(out List<TRtKey> children))
            {
                children.Add(root);
                for (var i = 0; i < children.Count; i++)
                {
                    var item = children[i];
                    GetChildren(item, children);

                    if (!Modifiers.TryGetValue(item, out var modifiers))
                        continue;
                    Modifiers.Remove(item);
                    if (other != null)
                        other.Modifiers[item] = modifiers;
                    foreach (var mod in modifiers)
                    {
                        var key = new ModifierDataKey(item, mod.Id);
                        if (!ModifierData.TryGetValue(key, out var data))
                            continue;
                        ModifierData.Remove(key);
                        if (other != null)
                            other.ModifierData[key] = data;
                    }
                }
            }
        }

        /// <summary>
        /// Removes modifiers missing their dependencies 
        /// </summary>
        private void RemoveOrphanedModifiers(in TRtKey key, ref InterningBag<EquiModifierBaseDefinition> input)
        {
            while (true)
            {
                if (!TryCreateContext(in key, GetModifiersUnsafe(key), out var ctx))
                    break;
                var edited = input;
                foreach (var v in input)
                    if (!v.CanApply(in ctx))
                    {
                        edited = edited.Without(v);
                        if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                            this.GetLogger().Info($"Removing {v} from {key} since it is no longer applicable");
                        ModifierData.Remove(new ModifierDataKey(key, v.Id));
                    }

                if (ReferenceEquals(input, edited))
                    break;
                input = edited;
            }
        }

        private bool _applyingModifiers;

        protected void ApplyModifiers(in TRtKey key)
        {
            if (_applyingModifiers)
                return;
            try
            {
                _applyingModifiers = true;
                if (!TryCreateContext(in key, GetModifiers(in key), out var ctx))
                {
                    if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,>)))
                        this.GetLogger().Info($"Context creation for {key} failed while applying modifiers");
                    return;
                }

                GenerateModifierOutput(in key, in ctx, out var result);
                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,>)))
                    this.GetLogger().Info($"Applying modifiers to {key}: {ctx.Modifiers} produced {result}");
                ApplyOutput(in key, in ctx, in result);
                result.MaterialEditsBuilder?.Dispose();
                ModifiersApplied?.Invoke(this, key);
            }
            finally

            {
                _applyingModifiers = false;
            }
        }

        protected void GenerateModifierOutput(in TRtKey key, in ModifierContext ctx, out ModifierOutput output)
        {
            output = new ModifierOutput();
            using (Lock.AcquireSharedUsing())
            {
                output.Model = ctx.OriginalModel;
                foreach (var modifier in ctx.Modifiers)
                    modifier.Apply(in ctx, ModifierData.GetValueOrDefault(new ModifierDataKey(key, modifier.Id)), ref output);
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiModifierStorageComponent<TObKey>) base.Serialize(copy);
            var tmpModifiers = new Dictionary<InterningBag<EquiModifierBaseDefinition>, List<TObKey>>();
            var tmpDataSets = new Dictionary<DataSetKey, List<TObKey>>();

            using (Lock.AcquireSharedUsing())
            {
                foreach (var block in Modifiers)
                {
                    if (!tmpModifiers.TryGetValue(block.Value, out var list))
                        tmpModifiers[block.Value] = list = new List<TObKey>();
                    list.Add(block.Key.ToObjectBuilder());
                }

                foreach (var block in ModifierData)
                {
                    var dataSet = new DataSetKey(block.Key.Modifier, block.Value.Serialize());
                    if (string.IsNullOrEmpty(dataSet.Seed))
                        continue;
                    if (!tmpDataSets.TryGetValue(dataSet, out var list))
                        tmpDataSets[dataSet] = list = new List<TObKey>();
                    list.Add(block.Key.Host.ToObjectBuilder());
                }
            }

            ob.Modifiers = tmpModifiers.Select(bb => new MyObjectBuilder_EquiModifierStorageComponent<TObKey>.ModifierSet
            {
                Blocks = bb.Value.ToArray(),
                Modifiers = bb.Key.Select(x => (SerializableDefinitionId) x.Id).ToArray()
            }).ToArray();
            ob.Storage = tmpDataSets.Select(bb => new MyObjectBuilder_EquiModifierStorageComponent<TObKey>.DataSet
            {
                Modifier = bb.Key.Id,
                Seed = bb.Key.Seed,
                Blocks = bb.Value.ToArray()
            }).ToArray();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiModifierStorageComponent<TObKey>) builder;
            using (Lock.AcquireExclusiveUsing())
            {
                Modifiers.Clear();
                ModifierData.Clear();

                if (ob.Modifiers != null)
                    foreach (var mod in ob.Modifiers)
                        if (mod.Blocks != null && mod.Modifiers != null && mod.Modifiers.Length > 0 && mod.Blocks.Length > 0)
                        {
                            var bag = InterningBag<EquiModifierBaseDefinition>.Of(mod.Modifiers
                                .Select(x =>
                                {
                                    var def = MyDefinitionManager.Get<EquiModifierBaseDefinition>(x);
                                    if (def == null)
                                        this.GetLogger().Error($"Failed to find modifier definition {x}.  Dropping it.");
                                    return def;
                                })
                                .Where(def => def != null));
                            if (bag.Count == 0)
                                continue;
                            foreach (var block in mod.Blocks)
                            {
                                Modifiers[block.ToRuntime()] = bag;
                            }
                        }

                if (ob.Storage != null)
                    foreach (var dataSet in ob.Storage)
                        if (dataSet.Blocks != null && !string.IsNullOrEmpty(dataSet.Seed))
                        {
                            var definition = MyDefinitionManager.Get<EquiModifierBaseDefinition>(dataSet.Modifier);
                            if (definition == null)
                            {
                                this.GetLogger().Error($"Failed to find modifier definition {dataSet.Modifier}.  Dropping it.");
                                continue;
                            }

                            foreach (var data in dataSet.Blocks)
                                ModifierData[new ModifierDataKey(data.ToRuntime(), dataSet.Modifier)] = definition.CreateData(dataSet.Seed);
                        }
            }
        }

        public override bool IsSerialized => Modifiers.Count > 0;

        private readonly struct DataSetKey : IEquatable<DataSetKey>
        {
            public readonly MyDefinitionId Id;
            public readonly string Seed;

            public DataSetKey(MyDefinitionId id, string seed)
            {
                Id = id;
                Seed = seed;
            }

            public bool Equals(DataSetKey other)
            {
                return Id.Equals(other.Id) && Seed == other.Seed;
            }

            public override bool Equals(object obj)
            {
                return obj is DataSetKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Id.GetHashCode() * 397) ^ (Seed != null ? Seed.GetHashCode() : 0);
            }
        }
    }

    public class MyObjectBuilder_EquiModifierStorageComponent<TKey> : MyObjectBuilder_EntityComponent, IMyRemappable where TKey : struct, IMyRemappable
    {
        [XmlElement("Modifiers")]
        [Nullable]
        public ModifierSet[] Modifiers;

        public struct ModifierSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId[] Modifiers;

            [XmlElement("Object")]
            public TKey[] Blocks;
        }

        [XmlElement("Storage")]
        [Nullable]
        public DataSet[] Storage;

        public struct DataSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId Modifier;

            [XmlElement("Data")]
            public string Seed;

            [XmlElement("Object")]
            public TKey[] Blocks;
        }

        [SuppressMessage("ReSharper", "ForCanBeConvertedToForeach")]
        public void Remap(IMySceneRemapper remapper)
        {
            if (Modifiers != null)
                foreach (var modifier in Modifiers)
                    if (modifier.Blocks != null)
                        for (var i = 0; i < modifier.Blocks.Length; i++)
                            modifier.Blocks[i].Remap(remapper);

            if (Storage != null)
                foreach (var storage in Storage)
                    if (storage.Blocks != null)
                        for (var i = 0; i < storage.Blocks.Length; i++)
                            storage.Blocks[i].Remap(remapper);
        }
    }

    public interface IModifierRtKey<out TObKey> where TObKey : struct
    {
        [Pure]
        TObKey ToObjectBuilder();
    }

    public interface IModifierObKey<out TRtKey> where TRtKey : struct
    {
        [Pure]
        TRtKey ToRuntime();
    }
}