using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using VRage.Collections;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public abstract class InputAttributeBase : Attribute
    {
        /// <summary>
        /// Marks this input as optional.
        /// </summary>
        public bool Optional { get; set; } = false;
    }

    public sealed class InputAttribute : InputAttributeBase, IObjectFingerprintSettings
    {
        /// <summary>
        /// JSON converter to use.
        /// </summary>
        public Type Converter { get; set; }

        /// <summary>
        /// Force objects assignable to this interfaces to be serialized only accessing the properties on the interface.
        /// </summary>
        public Type ForcedInterface { get; set; }
    }

    public sealed class InputFileAttribute : InputAttributeBase
    {
    }

    public sealed class InputNestedAttribute : InputAttributeBase
    {
    }

    public sealed class OutputFileAttribute : Attribute
    {
    }

    public abstract class AssetTask
    {
        public readonly AssetTaskManager TaskManager;
        private readonly Dictionary<AssetTask, string> _dependencies = new Dictionary<AssetTask, string>(EqualityUtils.ReferenceEquality);

        public DictionaryReader<AssetTask, string> Dependencies => _dependencies;

        private volatile AssetTaskIdentifier _id;

        protected virtual string TaskNameOverride => null;

        public AssetTaskIdentifier Id
        {
            get
            {
                var id = _id;
                if (id != null) return id;
                lock (this)
                {
                    id = _id;
                    if (id != null) return id;
                    return _id = new AssetTaskIdentifier(this, TaskNameOverride);
                }
            }
        }

        public void DependsOn(AssetTask other, string reason = "unknown") => _dependencies[other] = reason;

        protected AssetTask(AssetTaskManager taskManager) => TaskManager = taskManager;

        protected abstract void ExecuteInternal();

        internal void ExecuteFromContext() => ExecuteInternal();

        public override string ToString() => Id.Name;
    }

    public abstract class ModTask : AssetTask
    {
        protected readonly KeenMod Mod;

        protected ModTask(KeenMod mod) : base(mod)
        {
        }
    }
}