using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.State
{
    [MyComponent(typeof(MyObjectBuilder_EquiStateEventTrigger))]
    [MyDependency(typeof(MyComponentEventBus), Critical = true)]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    [MyDefinitionRequired]
    public class EquiStateEventTrigger : MyEntityComponent, IMyComponentEventProvider
    {
        private MyComponentEventBus _eventBus;
        private MyEntityStateComponent _state;
        private readonly Queue<MyStringHash> _destinationEvents = new Queue<MyStringHash>();
        private readonly Action<string> _triggerEvent;

        public EquiStateEventTrigger()
        {
            _triggerEvent = (evt) => _eventBus?.Invoke(evt);
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            _eventBus = Container.Get<MyComponentEventBus>();
            _state = Container.Get<MyEntityStateComponent>();
            _state.StateChanged += OnStateChanged;
            OnStateChanged(MyStringHash.NullOrEmpty, _state.CurrentState);
        }

        private void OnStateChanged(MyStringHash oldState, MyStringHash newState)
        {
            if (oldState == newState)
                return;
            Definition.TriggerFromEvents(oldState, newState, _triggerEvent);
            if (!Definition.Defer)
            {
                Definition.TriggerToEvents(newState, _triggerEvent);
                return;
            }

            _destinationEvents.Enqueue(_state.CurrentState);
            AddScheduledCallback(Update);
        }

        private void Update(long dt)
        {
            if (!Definition.Defer || Entity == null || _state == null || !Entity.InScene)
                return;
            MyStringHash state;
            while (_destinationEvents.TryDequeue(out state))
                Definition.TriggerToEvents(state, _triggerEvent);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();
            _state.StateChanged -= OnStateChanged;
            _eventBus = null;
            _state = null;
        }

        public EquiStateEventTriggerDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiStateEventTriggerDefinition) def;
        }

        public bool HasEvent(string eventName)
        {
            return Definition.HasEvent(eventName);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiStateEventTrigger : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiStateEventTriggerDefinition))]
    public class EquiStateEventTriggerDefinition : EquiEventStateDefinitionBase
    {
        private readonly Dictionary<MyStringHash, List<Trigger>> _fromTriggers = new Dictionary<MyStringHash, List<Trigger>>();
        private readonly Dictionary<MyStringHash, List<Trigger>> _toGlobTriggers = new Dictionary<MyStringHash, List<Trigger>>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            _fromTriggers.Clear();
            _toGlobTriggers.Clear();

            foreach (var trig in Triggers)
            {
                if (trig.From == MyStringHash.NullOrEmpty)
                    _toGlobTriggers.AddMulti(trig.To, trig);
                else
                    _fromTriggers.AddMulti(trig.From, trig);
            }
        }


        public void TriggerFromEvents(MyStringHash from, MyStringHash to, Action<string> callback)
        {
            List<Trigger> tmp;
            if (!_fromTriggers.TryGetValue(from, out tmp))
                return;
            foreach (var k in tmp)
                if (k.To == MyStringHash.NullOrEmpty || k.To == to)
                    callback(k.Event);
        }

        public void TriggerToEvents(MyStringHash to, Action<string> callback)
        {
            List<Trigger> tmp;
            if (!_toGlobTriggers.TryGetValue(to, out tmp))
                return;
            foreach (var k in tmp)
                callback(k.Event);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiStateEventTriggerDefinition : MyObjectBuilder_EquiEventStateDefinitionBase
    {
    }
}