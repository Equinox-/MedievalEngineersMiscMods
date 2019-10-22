using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Controller;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Network;
using VRage.Session;

namespace Equinox76561198048419394.SleepThrough
{
    [StaticEventOwner]
    [MyDependency(typeof(MySectorWeatherComponent))]
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public class EquiSleepSessionComponent : MySessionComponent
    {
        // Day Length / Sleeping Length
        private static readonly double SecondsPerSecond = TimeSpan.FromMinutes(2 * 60).TotalSeconds / TimeSpan.FromSeconds(60f).TotalSeconds;

        private readonly CachingHashSet<EquiPlayerAttachmentComponent.Slot> _slots = new CachingHashSet<EquiPlayerAttachmentComponent.Slot>();

        [Automatic]
        private readonly MySectorWeatherComponent _weather = null;

        private bool? _weatherEnabled;

        public void Register(EquiPlayerAttachmentComponent.Slot slot)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _slots.Add(slot);
            MarkDirty();
        }

        public void Unregister(EquiPlayerAttachmentComponent.Slot slot)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            _slots.Remove(slot);
            MarkDirty();
        }

        private bool _dirty;

        protected override void OnLoad()
        {
            base.OnLoad();
            MarkDirty();
        }

        public void MarkDirty()
        {
            _dirty = true;
        }

        private class StateData
        {
            public readonly MyEntity Player;
            public bool? InSun;

            public StateData(MyEntity player, bool? inSun)
            {
                Player = player;
                InSun = inSun;
            }
        }

        private Dictionary<EquiPlayerAttachmentComponent.Slot, StateData> _currentlySleeping = new Dictionary<EquiPlayerAttachmentComponent.Slot, StateData>();
        private Dictionary<EquiPlayerAttachmentComponent.Slot, StateData> _nextSleeping = new Dictionary<EquiPlayerAttachmentComponent.Slot, StateData>();

        private bool _advanceTime = false;
        private int _wakeUpTimer = 0;

        [FixedUpdate]
        private void Update()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            if (MyMultiplayerModApi.Static.Players.Count == 0)
            {
                if (_weatherEnabled.HasValue)
                {
                    _weather.Enabled = _weatherEnabled.Value;
                    _weather.DayOffset = _weather.DayOffset; // force sync
                }
                _weatherEnabled = null;
                return;
            }

            if (_dirty)
            {
                _slots.ApplyChanges();
                var required = (int) (MyMultiplayerModApi.Static.Players.Count / 2f) + 1;
                var has = 0;
                var next = _nextSleeping;
                next.Clear();
                foreach (var s in _slots)
                    if (s.Controllable.Entity != null && s.Controllable.Entity.InScene && s.AttachedCharacter != null)
                    {
                        var player = MyAPIGateway.Players?.GetPlayerControllingEntity(s.AttachedCharacter);
                        if (player == null)
                            continue;
                        has++;
                        StateData stateData;
                        if (!_currentlySleeping.TryGetValue(s, out stateData))
                        {
                            stateData = new StateData(s.AttachedCharacter, null);
                            _currentlySleeping.Add(s, stateData);
                            BroadcastNotification($"{stateData.Player.DisplayName} is now sleeping.  ({has}/{required})");
                        }

                        next.Add(s, stateData);
                    }

                _advanceTime = has >= required;
                foreach (var k in _currentlySleeping)
                    if (!next.ContainsKey(k.Key))
                        BroadcastNotification($"{k.Value.Player.DisplayName} woke up.");
                var current = _currentlySleeping;
                _currentlySleeping = next;
                current.Clear();
                _nextSleeping = current;
            }

            _wakeUpTimer--;
            if (_wakeUpTimer <= 0)
            {
                CheckWakeup();
                _wakeUpTimer = 30;
            }

            // ReSharper disable once InvertIf
            if (_advanceTime)
            {
                if (!_weatherEnabled.HasValue)
                    _weatherEnabled = _weather.Enabled;
                
                var nextOffset = _weather.DayOffset +  TimeSpan.FromSeconds(SecondsPerSecond * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);
                _weather.Enabled = true;
                _weather.DayOffset = TimeSpan.FromMinutes(nextOffset.TotalMinutes % _weather.DayDurationInMinutes);
            }
            else
            {
                if (_weatherEnabled.HasValue)
                {
                    _weather.Enabled = _weatherEnabled.Value;
                    _weather.DayOffset = _weather.DayOffset; // force sync
                }
                _weatherEnabled = null;
            }
        }

        private void CheckWakeup()
        {
            foreach (var k in _currentlySleeping)
                if (k.Key.Controllable.Entity != null)
                {
                    var info = _weather.CreateSolarObservation(_weather.CurrentTime, k.Key.Controllable.Entity.GetPosition());
                    if (!k.Value.InSun.HasValue)
                    {
                        k.Value.InSun = info.SolarElevation > 0;
                        continue;
                    }

                    var wasInSun = k.Value.InSun.Value;
                    var inSun = info.SolarElevation > 0;
                    if (wasInSun == inSun)
                        continue;
                    if (wasInSun && info.SolarElevation >= -5)
                        continue;
                    if (!wasInSun && info.SolarElevation <= 5)
                        continue;
                    k.Key.AttachedCharacter.Get<EquiEntityControllerComponent>()?.ReleaseControl();
                    k.Value.InSun = inSun;
                }
        }

        private static void BroadcastNotification(string msg)
        {
            MyMultiplayerModApi.Static.RaiseStaticEvent(x => ShowNotificationClients, msg);
        }

        [Event]
        [Server]
        [Broadcast]
        private static void ShowNotificationClients(string msg)
        {
            var utils = (IMyUtilities) MyAPIUtilities.Static;
            utils.ShowNotification(msg);
        }
    }
}