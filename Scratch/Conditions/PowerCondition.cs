using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Entities.Components.Crafting;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Conditions
{
    public class PowerCondition : ConditionBase
    {
        private readonly bool _all;

        public PowerCondition(MyEntity container,  bool inverted, bool all) : base(container, inverted)
        {
            _all = all;
        }

        private readonly List<IMyPowerProvider> _components = new List<IMyPowerProvider>();

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
            var cc = obj as IMyPowerProvider;
            if (cc == null)
                return;
            _components.Add(cc);
            cc.PowerStateChanged += ComponentChanged;
            if (!_blockedUpdate)
                Calculate();
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            var cc = obj as IMyPowerProvider;
            if (cc == null)
                return;
            _components.Remove(cc);
            cc.PowerStateChanged -= ComponentChanged;
            if (!_blockedUpdate)
                Calculate();
        }

        private void ComponentChanged(IMyPowerProvider arg1, bool arg2)
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
                    if (!c.IsPowered)
                    {
                        SetState(false);
                        return;
                    }

                SetState(true);
                return;
            }

            foreach (var c in _components)
                if (c.IsPowered)
                {
                    SetState(true);
                    return;
                }

            SetState(false);
        }
    }

    public class PowerConditionDefinition : ConditionDefinition
    {
        [XmlAttribute]
        public bool All;

        public override ICondition Compile(MyEntity container, bool inverted)
        {
            return new PowerCondition(container, inverted, All);
        }
    }
}