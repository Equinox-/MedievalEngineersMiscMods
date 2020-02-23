using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Inventory;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Modifiers.Def
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiModifierBaseDefinition))]
    public abstract class EquiModifierBaseDefinition : MyDefinitionBase
    {
        public HashSetReader<MyStringHash> Tags { get; private set; }

        private HashSet<MyDefinitionId> _objectDependencies;
        private HashSet<MyDefinitionId> _modifierDependencies;
        private HashSet<MyDefinitionId> _modifierExclusives;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiModifierBaseDefinition) def;
            if (ob.Tags != null && ob.Tags.Count > 0)
            {
                var tmp = new HashSet<MyStringHash>(MyStringHash.Comparer);
                foreach (var s in ob.Tags)
                    tmp.Add(MyStringHash.GetOrCompute(s));
                Tags = tmp;
            }
            else
            {
                Tags = new HashSetReader<MyStringHash>(null);
            }

            if (ob.ModifierDependencies != null && ob.ModifierDependencies.Count > 0)
            {
                _modifierDependencies = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
                foreach (var s in ob.ModifierDependencies)
                    _modifierDependencies.Add(s);
            }

            if (ob.ObjectDependencies != null && ob.ObjectDependencies.Count > 0)
            {
                _objectDependencies = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
                foreach (var s in ob.ObjectDependencies)
                    _objectDependencies.Add(s);
            }

            if (ob.ModifierExclusives != null && ob.ModifierExclusives.Count > 0)
            {
                _modifierExclusives = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
                foreach (var s in ob.ModifierExclusives)
                    _modifierExclusives.Add(s);
            }
        }

        /// <summary>
        /// Can this modifier exist on the given modifier context. 
        /// </summary>
        public virtual bool CanApply(in ModifierContext ctx)
        {
            if (_modifierDependencies != null)
            {
                if (ctx.Modifiers.Count == 0)
                    return false;
                using (PoolManager.Get(out HashSet<MyStringHash> tags))
                {
                    // Flatten tag set:
                    foreach (var e in ctx.Modifiers)
                    foreach (var tag in e.Tags)
                        tags.Add(tag);

                    foreach (var dep in _modifierDependencies)
                    {
                        if (dep.TypeId == typeof(MyObjectBuilder_ItemTagDefinition))
                        {
                            if (!tags.Contains(dep.SubtypeId))
                                return false;
                        }
                        else
                        {
                            var good = false;
                            foreach (var mod in ctx.Modifiers)
                                if (mod.Id == dep)
                                {
                                    good = true;
                                    break;
                                }

                            if (!good)
                                return false;
                        }
                    }
                }
            }

            if (_objectDependencies == null) return true;

            if (ctx.Block != null)
            {
                if (_objectDependencies.Contains(ctx.Block.DefinitionId))
                    return true;
                if (!MyDefinitionManager.TryGet(ctx.Block.DefinitionId, out MyContainerDefinition cdef))
                    return false;
                foreach (var tag in cdef.Tags)
                    if (_objectDependencies.Contains(new MyDefinitionId(typeof(MyObjectBuilder_ItemTagDefinition), tag)))
                        return true;
            }
            else
            {
                var entDef = ctx.Entity.Definition;
                if (entDef == null)
                    return false;
                if (_objectDependencies.Contains(entDef.Id))
                    return true;
                foreach (var tag in entDef.Tags)
                    if (_objectDependencies.Contains(new MyDefinitionId(typeof(MyObjectBuilder_ItemTagDefinition), tag)))
                        return true;
            }

            return false;
        }

        /// <summary>
        /// Will adding this modifier evict the other modifier from the container.
        /// </summary>
        public virtual bool ShouldEvict(EquiModifierBaseDefinition other)
        {
            if (_modifierExclusives == null)
                return false;
            if (_modifierExclusives.Contains(other.Id))
                return true;
            foreach (var tag in other.Tags)
                if (_modifierExclusives.Contains(new MyDefinitionId(typeof(MyObjectBuilder_ItemTagDefinition), tag)))
                    return true;
            return false;
        }

        public abstract void Apply(in ModifierContext ctx, IModifierData data, ref ModifierOutput output);
        public abstract IModifierData CreateDefaultData(in ModifierContext ctx);
        public abstract IModifierData CreateData(string data);
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public abstract class MyObjectBuilder_EquiModifierBaseDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement("Tag")]
        public List<string> Tags;

        /**
         * What modifiers must be present for this modifier to be applied.  ALL of these must be present.
         */
        [XmlElement("ModifierDependency")]
        public List<DefinitionTagId> ModifierDependencies;

        /**
         * What types of objects this modifier can be applied to.  Entity definitions, block definitions, entity tags
         */
        [XmlElement("ObjectDependency")]
        public List<DefinitionTagId> ObjectDependencies;

        /**
         * When this modifier is added it will remove these other modifiers 
         */
        [XmlElement("ModifierExclusive")]
        public List<DefinitionTagId> ModifierExclusives;
    }
}