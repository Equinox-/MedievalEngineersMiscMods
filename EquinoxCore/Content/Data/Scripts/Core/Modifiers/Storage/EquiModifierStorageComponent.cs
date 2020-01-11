using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Debug;
using Equinox76561198048419394.Core.ModelGenerator;
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
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.Core.Modifiers.Storage
{
    public abstract class EquiModifierStorageComponent<TRtKey, TObKey, TObSeed> : MyEntityComponent, IMyEventProxy
        where TRtKey : struct, IModifierRtKey<TObKey>, IEquatable<TRtKey>
        where TObKey : struct, IModifierObKey<TRtKey>
        where TObSeed : struct, IModifierObSeed<TObKey>
    {
        protected readonly FastResourceLock Lock = new FastResourceLock();

        protected readonly Dictionary<TRtKey, InterningBag<EquiModifierBaseDefinition>> Modifiers =
            new Dictionary<TRtKey, InterningBag<EquiModifierBaseDefinition>>();

        protected readonly Dictionary<ModifierDataKey, IModifierData> ModifierData = new Dictionary<ModifierDataKey, IModifierData>();


        public delegate void ModifiersAppliedDelegate(EquiModifierStorageComponent<TRtKey, TObKey, TObSeed> owner, TRtKey block);

        public event ModifiersAppliedDelegate ModifiersApplied;

        protected struct ModifierDataKey : IEquatable<ModifierDataKey>
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

        protected void RemoveExtraModifiers()
        {
            using (Lock.AcquireExclusiveUsing())
            {
                using (PoolManager.Get(out List<TRtKey> toRemove))
                {
                    foreach (var id in Modifiers.Keys)
                        if (!TryCreateContext(in id, InterningBag<EquiModifierBaseDefinition>.Empty, out _))
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                    {
                        if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                            this.GetLogger().Info($"Removing {rem} since context creation failed");
                        Modifiers.Remove(rem);
                    }
                }

                using (PoolManager.Get(out List<ModifierDataKey> toRemove))
                {
                    foreach (var id in ModifierData.Keys)
                        if (!TryCreateContext(in id.Host, InterningBag<EquiModifierBaseDefinition>.Empty, out _))
                            toRemove.Add(id);

                    foreach (var rem in toRemove)
                    {
                        if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                            this.GetLogger().Info($"Removing {rem} since context creation failed");
                        ModifierData.Remove(rem);
                    }
                }
            }
        }

        protected abstract bool TryGetParent(in TRtKey key, out TRtKey parent);

        protected abstract void GetChildren(in TRtKey parent, List<TRtKey> children);

        public abstract bool TryCreateContext(in TRtKey key, InterningBag<EquiModifierBaseDefinition> modifiers, out ModifierContext context);

        protected abstract void ApplyOutput(in TRtKey key, in ModifierContext context, in ModifierOutput output);

        private InterningBag<EquiModifierBaseDefinition> GetModifiersUnsafe(in TRtKey block)
        {
            return Modifiers.GetValueOrDefault(block, InterningBag<EquiModifierBaseDefinition>.Empty);
        }

        public InterningBag<EquiModifierBaseDefinition> GetModifiers(in TRtKey block)
        {
            using (Lock.AcquireSharedUsing())
                return GetModifiersUnsafe(block);
        }

        public void AddModifier(in TRtKey key, EquiModifierBaseDefinition modifier, IModifierData useData = null)
        {
            if (!TryCreateContext(in key, GetModifiers(in key), out var ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier.Id} to {key} since context creation failed");
                return;
            }

            if (!modifier.CanApply(in ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier.Id} to {key} since it can't be applied to {ctx}");
                return;
            }
            var modifierData = (useData ?? modifier.CreateData(in ctx))?.Serialize() ?? "";
            RaiseAddModifierInternal(in key, in modifier.Id, modifierData);
        }

        public void UpdateModifierData(in TRtKey key, EquiModifierBaseDefinition modifier, IModifierData useData)
        {
            if (!GetModifiers(in key).Contains(modifier))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Can't update {modifier.Id} on {key} with {useData}");
                return;
            }
            
            RaiseUpdateModifierInternal(in key, modifier.Id, useData?.Serialize() ?? "");
        }

        public void RemoveModifier(in TRtKey key, EquiModifierBaseDefinition modifier)
        {
            if (!GetModifiers(in key).Contains(modifier))
                return;
            RaiseRemoveModifierInternal(in key, in modifier.Id);
        }

        protected abstract void RaiseAddModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data);

        protected void AddModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data)
        {
            var modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null)
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier} to {key} since {modifier} doesn't seem to exist");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier} to {key} since {MyEventContext.Current.Sender} isn't trusted");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!TryCreateContext(in key, GetModifiers(in key), out var ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier} to {key} since context creation failed");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!modifierDef.CanApply(in ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not adding {modifier} to {key} since it can't be applied to {ctx}");
                MyEventContext.ValidationFailed();
                return;
            }

            using (Lock.AcquireExclusiveUsing())
            {
                var mods = GetModifiersUnsafe(key);
                var edited = mods.With(modifierDef);
                if (mods.Equals(edited))
                {
                    if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                        this.GetLogger().Info($"Adding {modifier} to {key} was a no-op");
                    MyEventContext.ValidationFailed();
                    return;
                }
                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Adding {modifier} to {key}");

                foreach (var k in mods)
                    if (modifierDef.ShouldEvict(k))
                    {
                        if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                            this.GetLogger().Info($"Evicting {k} from {key} while adding {modifier}");
                        ModifierData.Remove(new ModifierDataKey(key, k.Id));
                        edited = edited.Without(k);
                    }

                RemoveOrphanedModifiers(in key, ref edited);

                Modifiers[key] = edited;
                if (!string.IsNullOrEmpty(data))
                    ModifierData[new ModifierDataKey(key, modifier)] = modifierDef.CreateData(data);
            }

            DispatchModifiersChanged(key);
        }

        protected abstract void RaiseUpdateModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data);

        protected void UpdateModifierInternal(in TRtKey key, in MyDefinitionId modifier, string data)
        {
            var modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null)
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not updating {modifier} on {key} since {modifier} doesn't seem to exist");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not updating {modifier} on {key} since {MyEventContext.Current.Sender} isn't trusted");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!GetModifiers(in key).Contains(modifierDef))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not updating {modifier} on {key} since it doesn't exist");
                MyEventContext.ValidationFailed();
                return;
            }
            
            var dataKey = new ModifierDataKey(key, modifier);
            var dataObj = modifierDef.CreateData(data);
            using (Lock.AcquireExclusiveUsing())
            {
                ModifierData[dataKey] = dataObj;
                DispatchModifiersChanged(in key);
            }
        }

        protected abstract void RaiseRemoveModifierInternal(in TRtKey key, in MyDefinitionId modifier);

        protected void RemoveModifierInternal(in TRtKey key, in MyDefinitionId modifier)
        {
            var modifierDef = MyDefinitionManager.Get<EquiModifierBaseDefinition>(modifier);
            if (modifierDef == null)
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not removing {modifier} from {key} since {modifier} doesn't seem to exist");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!MyEventContext.Current.IsLocallyInvoked && !NetworkTrust.IsTrusted(this))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not removing {modifier} from {key} since {MyEventContext.Current.Sender} isn't trusted");
                MyEventContext.ValidationFailed();
                return;
            }

            if (!TryCreateContext(in key, GetModifiers(key), out var ctx))
            {
                if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Not removing {modifier} from {key} since context creation failed");
                MyEventContext.ValidationFailed();
                return;
            }

            using (Lock.AcquireExclusiveUsing())
            {
                var mods = GetModifiersUnsafe(in key);
                var edited = mods.Without(modifierDef);
                if (ReferenceEquals(edited, mods))
                {
                    if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                        this.GetLogger().Info($"Adding {modifier} to {key} was a no-op");
                    MyEventContext.ValidationFailed();
                    return;
                }
                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Removing {modifier} from {key}");

                ModifierData.Remove(new ModifierDataKey(key, modifier));
                RemoveOrphanedModifiers(in key, ref edited);

                if (edited.Count > 0)
                    Modifiers[key] = edited;
                else
                    Modifiers.Remove(key);
            }

            DispatchModifiersChanged(in key);
        }

        // No locking
        protected void RemoveOrMoveInternal(in TRtKey root, EquiModifierStorageComponent<TRtKey, TObKey, TObSeed> other)
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
                        if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                            this.GetLogger().Info($"Removing {v} from {key} since it is no longer applicable");
                        ModifierData.Remove(new ModifierDataKey(key, v.Id));
                    }

                if (ReferenceEquals(input, edited))
                    break;
                input = edited;
            }
        }

        /// <summary>
        /// Modifiers changed, we need to apply them to this block AND its generated pieces.
        /// </summary>
        private void DispatchModifiersChanged(in TRtKey invoker)
        {
            using (PoolManager.Get(out List<TRtKey> children))
            {
                children.Add(invoker);
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < children.Count; i++)
                {
                    var key = children[i];
                    ApplyModifiers(in key);
                    GetChildren(in key, children);
                }
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
                    if (DebugFlags.Debug(typeof(EquiModifierStorageComponent<,,>)))
                        this.GetLogger().Info($"Context creation for {key} failed while applying modifiers");
                    return;
                }

                if (DebugFlags.Trace(typeof(EquiModifierStorageComponent<,,>)))
                    this.GetLogger().Info($"Applying modifiers to {key}");
                GenerateModifierOutput(in key, in ctx, out var result);
                ApplyOutput(in key, in ctx, in result);
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
            var ob = (MyObjectBuilder_EquiModifierStorageComponent<TObKey, TObSeed>) base.Serialize(copy);
            var tmpModifiers = new Dictionary<InterningBag<EquiModifierBaseDefinition>, List<TObKey>>();
            var tmpDataSets = new Dictionary<MyDefinitionId, List<TObSeed>>();

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
                    if (!tmpDataSets.TryGetValue(block.Key.Modifier, out var list))
                        tmpDataSets[block.Key.Modifier] = list = new List<TObSeed>();
                    list.Add(new TObSeed
                    {
                        Key = block.Key.Host.ToObjectBuilder(),
                        Data = block.Value.Serialize()
                    });
                }
            }

            ob.Modifiers = tmpModifiers.Select(bb => new MyObjectBuilder_EquiModifierStorageComponent<TObKey, TObSeed>.ModifierSet
            {
                Blocks = bb.Value.ToArray(),
                Modifiers = bb.Key.Select(x => (SerializableDefinitionId) x.Id).ToArray()
            }).ToArray();
            ob.Storage = tmpDataSets.Select(bb => new MyObjectBuilder_EquiModifierStorageComponent<TObKey, TObSeed>.DataSet
            {
                Modifier = bb.Key,
                Blocks = bb.Value.ToArray()
            }).ToArray();
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiModifierStorageComponent<TObKey, TObSeed>) builder;
            using (Lock.AcquireExclusiveUsing())
            {
                Modifiers.Clear();
                ModifierData.Clear();

                if (ob.Modifiers != null)
                    foreach (var mod in ob.Modifiers)
                        if (mod.Blocks != null && mod.Modifiers != null && mod.Modifiers.Length > 0 && mod.Blocks.Length > 0)
                        {
                            var bag = InterningBag<EquiModifierBaseDefinition>.Of(mod.Modifiers.Select(x =>
                                MyDefinitionManager.Get<EquiModifierBaseDefinition>(x)));
                            foreach (var block in mod.Blocks)
                            {
                                Modifiers[block.ToRuntime()] = bag;
                            }
                        }

                if (ob.Storage != null)
                    foreach (var dataSets in ob.Storage)
                        if (dataSets.Blocks != null)
                        {
                            var definition = MyDefinitionManager.Get<EquiModifierBaseDefinition>(dataSets.Modifier);
                            foreach (var data in dataSets.Blocks)
                                if (!string.IsNullOrEmpty(data.Data))
                                    ModifierData[new ModifierDataKey(data.Key.ToRuntime(), dataSets.Modifier)] = definition.CreateData(data.Data);
                        }
            }
        }

        public override bool IsSerialized => Modifiers.Count > 0;
    }

    public class MyObjectBuilder_EquiModifierStorageComponent<TKey, TSeed> : MyObjectBuilder_EntityComponent
        where TKey : struct
        where TSeed : struct, IModifierObSeed<TKey>
    {
        [XmlElement("Modifiers")]
        public ModifierSet[] Modifiers;

        public struct ModifierSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId[] Modifiers;

            [XmlElement("Object")]
            public TKey[] Blocks;
        }

        [XmlElement("Storage")]
        public DataSet[] Storage;

        public struct DataSet
        {
            [XmlElement("Modifier")]
            public SerializableDefinitionId Modifier;

            [XmlElement("Object")]
            public TSeed[] Blocks;
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

    public interface IModifierObSeed<TKey> where TKey : struct
    {
        [XmlIgnore]
        TKey Key { get; set; }

        [XmlIgnore]
        string Data { get; set; }
    }
}