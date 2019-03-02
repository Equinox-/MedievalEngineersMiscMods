using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Crafting;
using Medieval.Entities.Components.Crafting.Power;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.State
{
    [MyComponent(typeof(MyObjectBuilder_EquiPowerStateTransition))]
    [MyDependency(typeof(MyEntityStateComponent), Critical = true)]
    [MyDefinitionRequired(typeof(EquiPowerStateTransitionDefinition))]
    public class EquiPowerStateTransition : MyEntityComponent
    {
        private readonly List<IPowerProvider> _providers = new List<IPowerProvider>();
        private MyEntityStateComponent _state;

        public override void OnAddedToContainer()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            base.OnAddedToContainer();
            _state = Container.Get<MyEntityStateComponent>();
            _state.StateChanged += StateChanged;
            Container.ComponentAdded += ComponentAdded;
            Container.ComponentRemoved += ComponentRemoved;
            foreach (var k in Container)
                ComponentAdded(k);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _state.StateChanged -= StateChanged;
            Container.ComponentAdded -= ComponentAdded;
            Container.ComponentRemoved -= ComponentRemoved;
            foreach (var k in Container)
                ComponentRemoved(k);
            _state = null;
            base.OnBeforeRemovedFromContainer();
        }

        private void ComponentAdded(MyEntityComponent obj)
        {
            var p = obj as IPowerProvider;
            if (p == null)
                return;
            _providers.Add(p);
            p.OnPowerChanged += PowerStateChanged;
        }

        private void ComponentRemoved(MyEntityComponent obj)
        {
            var p = obj as IPowerProvider;
            if (p == null)
                return;
            _providers.Remove(p);
            p.OnPowerChanged -= PowerStateChanged;
            MarkForUpdate();
        }

        private void StateChanged(MyStringHash oldState, MyStringHash newState)
        {
            MarkForUpdate();
        }

        private void PowerStateChanged(IPowerProvider arg1, bool arg2)
        {
            MarkForUpdate();
        }

        private bool _needsUpdate;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            MarkForUpdate();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _needsUpdate = false;
            RemoveScheduledUpdate(Update);
        }

        private void MarkForUpdate()
        {
            if (Entity == null || !Entity.InScene || MyAPIGateway.Session == null)
                return;
            _needsUpdate = true;
            AddScheduledCallback(Update);
        }

        [Update(false)]
        private void Update(long dt)
        {
            if (Entity == null || !Entity.InScene || _state == null || !_needsUpdate)
                return;
            _needsUpdate = false;
            var ns = Definition.TryTransition(_state.CurrentState, _providers);
            if (ns != MyStringHash.NullOrEmpty)
                _state.TransitionTo(ns);
        }


        public EquiPowerStateTransitionDefinition Definition { get; private set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiPowerStateTransitionDefinition) def;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPowerStateTransition : MyObjectBuilder_EntityComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiPowerStateTransitionDefinition))]
    public class EquiPowerStateTransitionDefinition : MyEntityComponentDefinition
    {
        private class Trigger
        {
            public readonly MyObjectBuilder_EquiPowerStateTransitionDefinition.PowerNeeded NeedsPower;
            public readonly bool AutoStart;
            public readonly MyStringHash From, To;

            public Trigger(MyObjectBuilder_EquiPowerStateTransitionDefinition.Trigger t)
            {
                NeedsPower = t.NeedsPower;
                AutoStart = t.AutoStart;
                From = MyStringHash.GetOrCompute(t.From);
                To = MyStringHash.GetOrCompute(t.To);
            }

            public override string ToString()
            {
                return $"Power={NeedsPower}, Ignite={AutoStart}, From={From}, To={To}";
            }
        }

        private readonly Dictionary<MyStringHash, List<Trigger>> _triggersBySource = new Dictionary<MyStringHash, List<Trigger>>();

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiPowerStateTransitionDefinition) def;
            _triggersBySource.Clear();
            foreach (var t in ob.Triggers)
            {
                var trig = new Trigger(t);
                if (trig.To == MyStringHash.NullOrEmpty)
                    MyDefinitionErrors.Add(Package, $"Trigger {trig} in {Id} has no to destination state", LogSeverity.Critical);

                _triggersBySource.AddMulti(trig.From, trig);
            }
        }

        public MyStringHash TryTransition(MyStringHash source, IReadOnlyCollection<IPowerProvider> providers)
        {
            if (providers.Count == 0)
                return MyStringHash.NullOrEmpty;

            var specific = _triggersBySource.GetValueOrDefault(source);
            var global = _triggersBySource.GetValueOrDefault(source);
            if (specific == null && global == null)
                return MyStringHash.NullOrEmpty;

            var providersOn = 0;
            foreach (var p in providers)
                if (p.TryConsumePower(TimeSpan.FromMilliseconds(1)))
                    providersOn++;

            var allOn = providersOn == providers.Count;

            var enumerable = Enumerable.Empty<Trigger>();
            if (specific != null)
                enumerable = enumerable.Concat(specific);
            if (global != null)
                enumerable = enumerable.Concat(global);
            foreach (var type in enumerable)
            {
                switch (type.NeedsPower)
                {
                    case MyObjectBuilder_EquiPowerStateTransitionDefinition.PowerNeeded.None:
                    {
                        if (providersOn == 0)
                            return type.To;
                        if (!type.AutoStart)
                            continue;
                        var success = true;
                        foreach (var k in providers)
                        {
                            success &= !k.TryConsumePower(TimeSpan.FromMilliseconds(1));
                            if (!success)
                                break;
                        }
                        if (success)
                            return type.To;
                        break;
                    }
                    case MyObjectBuilder_EquiPowerStateTransitionDefinition.PowerNeeded.Any:
                    {
                        if (providersOn > 0)
                            return type.To;
                        if (!type.AutoStart)
                            continue;
                        var success = false;
                        foreach (var k in providers)
                        {
                            success |= k.TryConsumePower(TimeSpan.FromMilliseconds(1));
                            if (!success)
                                break;
                        }

                        if (success)
                            return type.To;
                        break;
                    }
                    default:
                    case MyObjectBuilder_EquiPowerStateTransitionDefinition.PowerNeeded.All:
                    {
                        if (providersOn > 0 && allOn)
                            return type.To;
                        if (!type.AutoStart)
                            continue;
                        var success = true;
                        foreach (var k in providers)
                        {
                            success &= k.TryConsumePower(TimeSpan.FromMilliseconds(1));
                            if (!success)
                                break;
                        }
                        if (success)
                            return type.To;
                        break;
                    }
                }
            }

            return MyStringHash.NullOrEmpty;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPowerStateTransitionDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public enum PowerNeeded
        {
            Any,
            All,
            None
        }

        [XmlElement("Trigger")]
        public Trigger[] Triggers;

        public class Trigger
        {
            [XmlAttribute]
            public PowerNeeded NeedsPower;

            [XmlAttribute]
            public bool AutoStart;

            [XmlAttribute]
            public string From;

            [XmlAttribute]
            public string To;
        }
    }
}