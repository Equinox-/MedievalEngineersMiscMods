using System;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public interface ICondition
    {
        bool State { get; }
        event Action<bool, bool> StateChanged;
    }

    public abstract class ConditionBase : ICondition
    {
        private bool _state;

        bool ICondition.State => _state;

        public MyEntity Container { get; }

        private readonly bool _inverted;

        protected void SetState(bool s)
        {
            var value = s ^ _inverted;
            if (_state == value)
                return;
            var old = _state;
            _state = value;
            StateChanged?.Invoke(old, value);
        }

        protected ConditionBase(MyEntity container, bool inverted)
        {
            Container = container;
            _inverted = inverted;
        }

        public virtual void OnAddedToContainer()
        {
        }

        public virtual void OnBeforeRemovedFromContainer()
        {
        }

        public event Action<bool, bool> StateChanged;
    }

    public abstract class ConditionDefinition
    {
        [XmlAttribute]
        [DefaultValue(false)]
        public bool Invert;

        public abstract ICondition Compile(MyEntity container, bool inverted);
    }
}