using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game.EntityComponents;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Conditions
{
    public class StateCondition : ConditionBase
    {
        private readonly HashSet<MyStringHash> _validStates;

        public StateCondition(MyEntity container, bool inverted, HashSet<MyStringHash> valid) : base(container, inverted)
        {
            _validStates = valid;
        }

        private MyEntityStateComponent _stateComponent;

        private MyEntityStateComponent StateComponent
        {
            get { return _stateComponent; }
            set
            {
                if (_stateComponent == value)
                    return;
                if (_stateComponent != null)
                    _stateComponent.StateChanged -= EntityStateChanged;
                _stateComponent = value;
                if (_stateComponent != null)
                    _stateComponent.StateChanged += EntityStateChanged;
                Calculate();
            }
        }

        public override void OnAddedToContainer()
        {
            Container.Components.ComponentAdded += OnComponentAdded;
            Container.Components.ComponentRemoved += OnComponentRemoved;
            foreach (var c in Container.Components)
                OnComponentAdded(c);
            Calculate();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            Container.Components.ComponentAdded -= OnComponentAdded;
            Container.Components.ComponentRemoved -= OnComponentRemoved;
            foreach (var c in Container.Components)
                OnComponentRemoved(c);
            Calculate();
        }

        private void EntityStateChanged(MyStringHash oldState, MyStringHash newState)
        {
            Calculate();
        }

        private void OnComponentAdded(MyEntityComponent obj)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var cc = obj as MyEntityStateComponent;
            if (cc == null)
                return;
            StateComponent = cc;
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            if (StateComponent != obj)
                return;
            StateComponent = null;
        }

        private void Calculate()
        {
            var s = StateComponent;
            SetState(s != null && _validStates.Contains(s.CurrentState));
        }
    }


    public class StateConditionDefinition : ConditionDefinition
    {
        [XmlAttribute]
        public string State;

        [XmlArrayItem("State")]
        public string[] States;

        private HashSet<MyStringHash> _compiled;

        private void EnsureCompiled()
        {
            if (_compiled != null)
                return;
            _compiled = new HashSet<MyStringHash>();
            if (!string.IsNullOrWhiteSpace(State))
                _compiled.Add(MyStringHash.GetOrCompute(State));
            if (States == null) return;
            foreach (var s in States)
                if (!string.IsNullOrWhiteSpace(s))
                    _compiled.Add(MyStringHash.GetOrCompute(s));
        }

        public override ICondition Compile(MyEntity container, bool inverted)
        {
            EnsureCompiled();
            return new StateCondition(container, inverted ^ Invert, _compiled);
        }
    }
}