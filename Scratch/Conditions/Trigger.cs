using System;
using System.Xml.Serialization;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public class Trigger
    {
        public enum Mode
        {
            Falling,
            Rising
        }

        public event Action Triggered;
        public readonly MyEntity Container;
        private ICondition _condition;
        private Mode _mode;

        public Trigger(MyEntity container, ICondition condition, Mode mode)
        {
            Container = container;
            _condition = condition;
            _mode = mode;
        }

        public void OnAddedToContainer()
        {
            (_condition as ConditionBase)?.OnAddedToContainer();
            _condition.StateChanged += OnStateChanged;
        }

        public void OnBeforeRemovedFromContainer()
        {
            (_condition as ConditionBase)?.OnBeforeRemovedFromContainer();
            _condition.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(bool old, bool @new)
        {
            if (_mode == Mode.Falling && !@new)
                Triggered?.Invoke();
            else if (_mode == Mode.Rising && @new)
                Triggered?.Invoke();
        }
    }

    public class TriggerDefinition
    {
        public AnyConditionDefinition Condition;

        [XmlAttribute]
        public Trigger.Mode Mode;

        public Trigger Compile(MyEntity container)
        {
            return new Trigger(container, Condition.Compile(container, false), Mode);
        }
    }
}