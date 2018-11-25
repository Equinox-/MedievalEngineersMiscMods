using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Controller;
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
using Extensions = Equinox76561198048419394.Core.Controller.Extensions;

namespace Equinox76561198048419394.Core.State
{
    [MyComponent(typeof(MyObjectBuilder_EquiEventStateTransition))]
    [MyDependency(typeof(MyComponentEventBus), Critical = true)]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    [MyDefinitionRequired]
    public class EquiEventStateTransition : MyEntityComponent
    {
        private MyComponentEventBus _eventBus;
        private MyEntityStateComponent _state;
        private MyStringHash? _requestedState;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _eventBus = Container.Get<MyComponentEventBus>();
            _state = Container.Get<MyEntityStateComponent>();
            foreach (var evt in Definition.Events)
                _eventBus.AddListener(evt, EventOccured);
            if (_requestedState.HasValue)
                Update(0);
        }

        private void EventOccured(string evt)
        {
            var dest = Definition.TransitionTarget(evt, _state.CurrentState);
            if (dest != MyStringHash.NullOrEmpty)
                RequestState(dest);
        }

        private void RequestState(MyStringHash h)
        {
            if (!Definition.Defer)
            {
                _state.TransitionTo(h);
                return;
            }

            if (!_requestedState.HasValue && Entity != null && Entity.InScene)
                AddScheduledCallback(Update);
            _requestedState = h;
        }

        private void Update(long dt)
        {
            if (Entity == null || _state == null || !Entity.InScene || !_requestedState.HasValue)
                return;
            _state.TransitionTo(_requestedState.Value);
            _requestedState = null;
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            foreach (var evt in Definition.Events)
                _eventBus.RemoveListener(evt, EventOccured);
            _eventBus = null;
            _state = null;
            RemoveScheduledUpdate(Update);
        }

        public EquiEventStateTransitionDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiEventStateTransitionDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiEventStateTransition : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiEventStateTransitionDefinition))]
    public class EquiEventStateTransitionDefinition : EquiEventStateDefinitionBase
    {
        private class TriggerData
        {
            public Trigger AnySourceTrigger;
            public readonly List<Trigger> Triggers = new List<Trigger>();
        }

        private readonly Dictionary<string, TriggerData> _triggersByEvent = new Dictionary<string, TriggerData>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            _triggersByEvent.Clear();
            foreach (var trig in Triggers)
            {
                if (trig.To == MyStringHash.NullOrEmpty)
                    MyDefinitionErrors.Add(Context, $"Trigger {trig} in {Id} has no to state", TErrorSeverity.Critical);

                TriggerData trigs;
                if (!_triggersByEvent.TryGetValue(trig.Event, out trigs))
                    _triggersByEvent.Add(trig.Event, trigs = new TriggerData());

                if (trig.From == MyStringHash.NullOrEmpty && trigs.AnySourceTrigger != null)
                    MyDefinitionErrors.Add(Context, $"Trigger {trig} trying to overwrite {trigs.AnySourceTrigger}", TErrorSeverity.Warning);

                if (trig.From == MyStringHash.NullOrEmpty)
                    trigs.AnySourceTrigger = trig;
                else
                    trigs.Triggers.Add(trig);
            }
        }

        public MyStringHash TransitionTarget(string eventName, MyStringHash source)
        {
            var data = _triggersByEvent.GetValueOrDefault(eventName);
            if (data == null)
                return MyStringHash.NullOrEmpty;

            foreach (var k in data.Triggers)
                if (k.From == source)
                    return k.To;

            return data.AnySourceTrigger?.To ?? MyStringHash.NullOrEmpty;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiEventStateTransitionDefinition : MyObjectBuilder_EquiEventStateDefinitionBase
    {
    }
}