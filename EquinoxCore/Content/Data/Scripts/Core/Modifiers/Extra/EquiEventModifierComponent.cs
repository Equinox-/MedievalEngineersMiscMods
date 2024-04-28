using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Modifiers.Data;
using Equinox76561198048419394.Core.Modifiers.Def;
using Equinox76561198048419394.Core.Modifiers.Storage;
using Sandbox.Game.EntityComponents;
using VRage.Collections;
using VRage.Components;
using VRage.Components.Block;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Modifiers.Extra
{
    [MyComponent(typeof(MyObjectBuilder_EquiEventModifierComponent))]
    [MyDefinitionRequired(typeof(EquiEventModifierComponent))]
    [MyDependency(typeof(MyComponentEventBus), Critical = false)]
    [MyDependency(typeof(MyEntityStateComponent), Critical = false)]
    [MyDependency(typeof(MyBlockComponent), Critical = false)]
    public class EquiEventModifierComponent : MyEntityComponent
    {
        [Automatic]
        private readonly MyComponentEventBus _eventBus = null;

        [Automatic]
        private readonly MyEntityStateComponent _state = null;

        [Automatic]
        private readonly MyBlockComponent _block = null;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayer.IsServer) return;
            if (_eventBus != null)
                foreach (var evtAndOps in Definition.EventToOps)
                    _eventBus.AddListener(evtAndOps.Key, HandleEvent);
            if (_state != null)
            {
                _state.StateChanged += OnStateChanged;
                AddScheduledCallback(ApplyCurrentStateAsync, 0);
            }
        }

        public override void OnRemovedFromScene()
        {
            if (_eventBus != null)
                foreach (var evtAndOps in Definition.EventToOps)
                    _eventBus.RemoveListener(evtAndOps.Key, HandleEvent);
            if (_state != null)
                _state.StateChanged -= OnStateChanged;
            base.OnRemovedFromScene();
        }

        public EquiEventModifierComponentDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiEventModifierComponentDefinition)def;
        }

        private void ApplyOps<TRtKey, TObKey>(ListReader<EquiEventModifierComponentDefinition.ModifierOp> ops,
            EquiModifierStorageComponent<TRtKey, TObKey> storage,
            Func<EquiEventModifierComponent, MyStringHash, TRtKey> constructKey)
            where TRtKey : struct, IModifierRtKey<TObKey>, IEquatable<TRtKey>
            where TObKey : struct, IModifierObKey<TRtKey>, IMyRemappable
        {
            foreach (var op in ops)
            {
                if (op.Modifier == null) continue;
                if (op.IncludeRoot)
                {
                    var rootKey = constructKey(this, MyStringHash.NullOrEmpty);
                    if (op.Remove)
                        storage.RemoveModifier(rootKey, op.Modifier);
                    else
                        storage.AddModifier(rootKey, op.Modifier, op.Data);
                }

                foreach (var child in op.Attachments)
                {
                    var childKey = constructKey(this, child);
                    if (op.Remove)
                        storage.RemoveModifier(childKey, op.Modifier);
                    else
                        storage.AddModifier(childKey, op.Modifier, op.Data);
                }
            }
        }

        private void ApplyOps(ListReader<EquiEventModifierComponentDefinition.ModifierOp> ops)
        {
            var gridModifierStorage = _block?.GridData?.Container?.Get<EquiGridModifierComponent>();
            if (gridModifierStorage == null) return;
            ApplyOps(ops, gridModifierStorage, (self, attachment) =>
                new EquiGridModifierComponent.BlockModifierKey(self._block.BlockId, attachment));
        }

        private void OnStateChanged(MyStringHash oldState, MyStringHash newState)
        {
            if (!Definition.StateToOps.TryGetValue(newState, out var ops)) return;
            ApplyOps(ops);
        }

        private void HandleEvent(string evt)
        {
            if (!Definition.EventToOps.TryGetValue(evt, out var ops)) return;
            ApplyOps(ops);
        }


        [Update(false)]
        public void ApplyCurrentStateAsync(long dt)
        {
            if (_state == null) return;
            OnStateChanged(MyStringHash.NullOrEmpty, _state.CurrentState);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiEventModifierComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiEventModifierComponentDefinition))]
    [MyDependency(typeof(EquiModifierBaseDefinition))]
    public class EquiEventModifierComponentDefinition : MyEntityComponentDefinition
    {
        public DictionaryReader<string, ListReader<ModifierOp>> EventToOps { get; private set; }
        public DictionaryReader<MyStringHash, ListReader<ModifierOp>> StateToOps { get; private set; }

        public class ModifierOp
        {
            private readonly EquiEventModifierComponentDefinition _owner;
            private readonly MyDefinitionId _modifierId;
            private readonly string _modifierData;
            public readonly bool Remove;
            public readonly ListReader<MyStringHash> Attachments;
            public readonly bool IncludeRoot;

            public ModifierOp(EquiEventModifierComponentDefinition owner, MyObjectBuilder_EquiEventModifierComponentDefinition.ModifierOp ob)
            {
                _owner = owner;
                _modifierId = ob.Modifier;
                _modifierData = ob.Data;
                Remove = ob.Remove ?? false;
                IncludeRoot = ob.IncludeRoot ?? true;

                if (ob.Attachments == null || ob.Attachments.Count == 0)
                    Attachments = ListReader<MyStringHash>.Empty;
                else
                    Attachments = ob.Attachments.Select(MyStringHash.GetOrCompute).ToList();
            }

            private bool _hasMemorized;
            private EquiModifierBaseDefinition _memorizedModifier;
            private IModifierData _memorizedData;

            private void EnsureMemorized()
            {
                if (_hasMemorized) return;
                _memorizedModifier = MyDefinitionManager.Get<EquiModifierBaseDefinition>(_modifierId);
                if (_memorizedModifier == null)
                    MyDefinitionErrors.Add(_owner.Package, $"Failed to find modifier {_modifierId} for {_owner.Id}", LogSeverity.Critical);
                _memorizedData = _memorizedModifier?.CreateData(_modifierData);
                _hasMemorized = true;
            }

            public EquiModifierBaseDefinition Modifier
            {
                get
                {
                    EnsureMemorized();
                    return _memorizedModifier;
                }
            }

            public IModifierData Data
            {
                get
                {
                    EnsureMemorized();
                    return _memorizedData;
                }
            }
        }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiEventModifierComponentDefinition)def;

            var eventToOps = new Dictionary<string, ListReader<ModifierOp>>();
            var stateToOps = new Dictionary<MyStringHash, ListReader<ModifierOp>>();
            if (ob.Binding != null)
            {
                foreach (var binding in ob.Binding)
                {
                    if (binding.Operations == null || binding.Operations.Count == 0) continue;
                    if ((binding.Events == null || binding.Events.Count == 0) && (binding.States == null || binding.States.Count == 0)) continue;
                    var ops = new List<ModifierOp>();
                    foreach (var op in binding.Operations)
                        ops.Add(new ModifierOp(this, op));

                    if (ops.Count == 0) continue;
                    if (binding.Events != null)
                        foreach (var evt in binding.Events)
                            eventToOps[evt] = ops;
                    if (binding.States != null)
                        foreach (var state in binding.States)
                            stateToOps[MyStringHash.GetOrCompute(state)] = ops;
                }
            }

            EventToOps = eventToOps;
            StateToOps = stateToOps;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiEventModifierComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        [XmlElement("Binding")]
        public List<EventHandler> Binding;

        public class EventHandler
        {
            [XmlElement("Event")]
            public List<string> Events;

            [XmlElement("State")]
            public List<string> States;

            [XmlElement("Operation")]
            public List<ModifierOp> Operations;
        }

        public struct ModifierOp
        {
            [XmlElement]
            public SerializableDefinitionId Modifier;

            [XmlElement("Attachment")]
            public List<string> Attachments;

            [XmlElement("IncludeRoot")]
            public bool? IncludeRoot;

            [XmlElement]
            public string Data;

            [XmlElement]
            public bool? Remove;
        }
    }
}