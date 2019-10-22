using System;
using System.Collections.Generic;
using Medieval.Entities.Components.Crafting;
using VRage.Game.Components;

namespace Equinox76561198048419394.Core.Util
{
    public class PowerObserver
    {
        private RequiredPowerEnum _requiredPower;

        public RequiredPowerEnum RequiredPower
        {
            get { return _requiredPower; }
            set
            {
                if (_requiredPower == value)
                    return;
                _requiredPower = value;
                Recompute();
            }
        }

        private MyEntityComponentContainer _container;

        private readonly List<IMyPowerProvider> _powerProviders = new List<IMyPowerProvider>();
        private readonly Action<MyEntityComponent> _registerComponent, _unregisterComponent;

        public PowerObserver()
        {
            // ReSharper disable HeapView.ClosureAllocation HeapView.DelegateAllocation
            Action<IMyPowerProvider, bool> poweredStateChanged = (a, b) => Recompute();
            _registerComponent = (x) =>
            {
                var power = x as IMyPowerProvider;
                if (power == null)
                    return;
                power.PowerStateChanged += poweredStateChanged;
                _powerProviders.Add(power);
                Recompute();
            };
            _unregisterComponent = (x) =>
            {
                var power = x as IMyPowerProvider;
                if (power == null)
                    return;
                power.PowerStateChanged -= poweredStateChanged;
                _powerProviders.Remove(power);
                Recompute();
            };
            // ReSharper restore HeapView.ClosureAllocation HeapView.DelegateAllocation
        }

        public void OnAddedToContainer(MyEntityComponentContainer container)
        {
            _container = container;
            container.ComponentAdded += _registerComponent;
            container.ComponentRemoved += _unregisterComponent;
            foreach (var k in container)
                _registerComponent(k);
        }

        public void OnRemovedFromContainer()
        {
            _container.ComponentAdded -= _registerComponent;
            _container.ComponentRemoved -= _unregisterComponent;
            foreach (var k in _container)
                _unregisterComponent(k);
            _container = null;
        }

        public delegate void PoweredChangedDel(bool oldState, bool newState);

        public event PoweredChangedDel PoweredChanged;

        private bool _isPowered;

        public bool IsPowered
        {
            get { return _isPowered; }
            private set
            {
                if (_isPowered == value)
                    return;
                var old = _isPowered;
                _isPowered = value;
                PoweredChanged?.Invoke(old, value);
            }
        }

        public void RequestPower()
        {
            if (RequiredPower == RequiredPowerEnum.None)
                return;
            foreach (var p in _powerProviders)
            {
                p.TryStart();
                if (p.IsPowered && RequiredPower == RequiredPowerEnum.Any)
                    return;
            }
        }

        private void Recompute()
        {
            switch (_requiredPower)
            {
                case RequiredPowerEnum.None:
                    IsPowered = true;
                    return;
                case RequiredPowerEnum.Any:
                {
                    foreach (var p in _powerProviders)
                        if (p.IsPowered)
                        {
                            IsPowered = true;
                            return;
                        }

                    IsPowered = false;
                    return;
                }
                case RequiredPowerEnum.All:
                {
                    var any = false;
                    foreach (var p in _powerProviders)
                    {
                        if (!p.IsPowered)
                        {
                            IsPowered = false;
                            return;
                        }

                        any = true;
                    }

                    IsPowered = any;
                    return;
                }
            }
        }


        public enum RequiredPowerEnum
        {
            None,
            Any,
            All
        }
    }
}