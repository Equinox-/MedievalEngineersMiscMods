using System.Xml.Serialization;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public abstract class CompositeCondition : ConditionBase
    {
        protected readonly ICondition[] Children;

        protected CompositeCondition(MyEntity container, ICondition[] children, bool invert) : base(container, invert)
        {
            Children = children;
            foreach (var c in Children)
                c.StateChanged += ChildChanged;
            // ReSharper disable once VirtualMemberCallInConstructor
            SetState(Calculate());
        }

        private void ChildChanged(bool arg1, bool arg2)
        {
            SetState(Calculate());
        }

        protected abstract bool Calculate();

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            foreach (var c in Children)
                (c as ConditionBase)?.OnAddedToContainer();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            foreach (var c in Children)
                (c as ConditionBase)?.OnBeforeRemovedFromContainer();
            base.OnBeforeRemovedFromContainer();
        }
    }

    public abstract class CompositeConditionDefinition : ConditionDefinition
    {
        [XmlElement("Ref", typeof(ReferenceConditionDefinition))]
        [XmlElement("Power", typeof(PowerCondition))]
        [XmlElement("All", typeof(AllConditionDefinition))]
        [XmlElement("Any", typeof(AnyConditionDefinition))]
        [XmlElement("State", typeof(StateCondition))]
        public ConditionDefinition[] Children;
    }
}