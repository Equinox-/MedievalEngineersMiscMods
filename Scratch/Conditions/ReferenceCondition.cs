using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public class ReferenceCondition : ConditionBase
    {
        private readonly string _key;
        private readonly bool _all;

        public ReferenceCondition(MyEntity container, string key, bool inverted, bool all) : base(container, inverted)
        {
            _key = key;
            _all = all;
        }

        private readonly List<IConditionComponent> _components = new List<IConditionComponent>();

        public override void OnAddedToContainer()
        {
            Container.Components.ComponentAdded += OnComponentAdded;
            Container.Components.ComponentRemoved += OnComponentRemoved;
            _blockedUpdate = true;
            foreach (var c in Container.Components)
                OnComponentAdded(c);
            Calculate();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            Container.Components.ComponentAdded -= OnComponentAdded;
            Container.Components.ComponentRemoved -= OnComponentRemoved;
            _blockedUpdate = true;
            foreach (var c in Container.Components)
                OnComponentRemoved(c);
            Calculate();
        }

        private bool _blockedUpdate = false;

        private void OnComponentAdded(MyEntityComponent obj)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var cc = obj as IConditionComponent;
            if (cc == null || cc.Id != _key)
                return;
            _components.Add(cc);
            cc.StateChanged += ComponentChanged;
            if (!_blockedUpdate)
                Calculate();
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var cc = obj as IConditionComponent;
            if (cc == null || cc.Id != _key)
                return;
            _components.Remove(cc);
            cc.StateChanged -= ComponentChanged;
            if (!_blockedUpdate)
                Calculate();
        }

        private void ComponentChanged(bool arg1, bool arg2)
        {
            if (!_blockedUpdate)
                Calculate();
        }

        private void Calculate()
        {
            _blockedUpdate = false;
            if (_all)
            {
                foreach (var c in _components)
                    if (!c.State)
                    {
                        SetState(false);
                        return;
                    }

                SetState(true);
                return;
            }

            foreach (var c in _components)
                if (c.State)
                {
                    SetState(true);
                    return;
                }

            SetState(false);
        }
    }

    public class ReferenceConditionDefinition : ConditionDefinition
    {
        [XmlAttribute]
        public string Key;

        [XmlAttribute]
        [DefaultValue(false)]
        public bool All;

        public override ICondition Compile(MyEntity container, bool inverted)
        {
            return new ReferenceCondition(container, Key, Invert ^ inverted, All);
        }
    }
}