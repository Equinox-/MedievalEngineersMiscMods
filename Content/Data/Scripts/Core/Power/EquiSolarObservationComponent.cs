using System;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components.Crafting;
using Sandbox.Game.SessionComponents;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Power
{
    [MyComponent(typeof(MyObjectBuilder_EquiSolarObservationComponent))]
    [MyDependency(typeof(MyComponentEventBus), Critical = false)]
    [MyDefinitionRequired]
    public class EquiSolarObservationComponent : MyEntityComponent, IMyComponentEventProvider, IMyPowerProvider
    {
        private static readonly Random Rand = new Random();
        private const double StaggerDistance = 100D;

        private const string SolarMatchEventStart = "SolarMatchStart";
        private const string SolarMatchEventStop = "SolarMatchStop";

        private MySectorWeatherComponent _weather;

        private MyComponentEventBus _eventBus;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _weather = MySession.Static.Components.Get<MySectorWeatherComponent>();
            if (_weather == null || Definition == null)
                return;
            Container.TryGet(out _eventBus);
            AddScheduledCallback(FirstUpdate, 0);
            Update(0);
        }

        private void FirstUpdate(long dt)
        {
            var currTime = (long) (MySession.Static?.ElapsedGameTime.TotalMilliseconds ?? 0);
            var targetUpdateTime = ((currTime / Definition.UpdateIntervalMs) + 1) * Definition.UpdateIntervalMs;
            switch (Definition.Transition)
            {
                case EquiSolarObservationComponentDefinition.ScheduleTransition.WaveExpand:
                {
                    var phaseFactor = MiscMath.PlanetaryWavePhaseFactor(Entity?.GetPosition() ?? Vector3D.One, StaggerDistance);
                    var mod = Definition.UpdateIntervalMs * MathHelper.Clamp(Math.Abs((phaseFactor % 2) - 1), 0, 1);
                    targetUpdateTime += (int) mod;
                    break;
                }
                case EquiSolarObservationComponentDefinition.ScheduleTransition.Wave:
                {
                    var phaseFactor = MiscMath.PlanetaryWavePhaseFactor(Entity?.GetPosition() ?? Vector3D.One, StaggerDistance);
                    var mod = Definition.UpdateIntervalMs * (phaseFactor % 1);
                    targetUpdateTime += (int) mod;
                    break;
                }
                case EquiSolarObservationComponentDefinition.ScheduleTransition.Sparkle:
                {
                    var mod = (Entity?.EntityId ?? Rand.Next()) % Definition.UpdateIntervalMs;
                    targetUpdateTime += (int) mod;
                    break;
                }
                case EquiSolarObservationComponentDefinition.ScheduleTransition.Immediate:
                default:
                    break;
            }

            AddScheduledCallback(StartUpdateLoop, (targetUpdateTime - currTime));
            Update(0);
        }

        private void StartUpdateLoop(long dt)
        {
            var intervalRand = 0;

            var varianceMax = Definition.UpdateVarianceMs * 2 + 1;
            if (varianceMax > 0)
            {
                intervalRand = (int) ((Entity?.EntityId ?? Rand.Next()) % varianceMax);
                intervalRand -= varianceMax / 2;
            }

            AddScheduledUpdate(Update, Definition.UpdateIntervalMs + intervalRand);
            Update(0);
        }

        public override void OnRemovedFromScene()
        {
            RemoveScheduledUpdate(FirstUpdate);
            RemoveScheduledUpdate(StartUpdateLoop);
            RemoveScheduledUpdate(Update);
            _weather = null;
            IsActive = false;
            base.OnRemovedFromScene();
        }

        private EquiSolarObservationComponentDefinition Definition { get; set; }

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            Definition = (EquiSolarObservationComponentDefinition) def;
        }

        private bool? _isActive;

        private bool IsActive
        {
            get { return _isActive ?? false; }
            set
            {
                if (_isActive.HasValue && value == _isActive.Value)
                    return;

                _eventBus?.Invoke(value ? SolarMatchEventStart : SolarMatchEventStop);
                ReadyStateChanged?.Invoke(this, value);
                PowerStateChanged?.Invoke(this, value);
                StateChanged?.Invoke(!value, value);
                _isActive = value;
            }
        }

        private void Update(long dt)
        {
            if (_weather == null || Entity == null || MySession.Static == null || Definition == null)
                return;
            var observation = _weather.CreateSolarObservation(MySession.Static.ElapsedGameTime, Entity.GetPosition());
            IsActive = Definition.Test(observation);
        }

        public bool HasEvent(string eventName)
        {
            return eventName == SolarMatchEventStart || eventName == SolarMatchEventStop;
        }

        public void TryStart()
        {
            Update(0);
        }

        public void TryStop()
        {
        }

        public bool IsPowered => IsActive;
        public bool IsReady => IsPowered;

        public event Action<IMyPowerProvider, bool> PowerStateChanged;
        public event Action<IMyPowerProvider, bool> ReadyStateChanged;
        public event Action<bool, bool> StateChanged;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSolarObservationComponent : MyObjectBuilder_EntityComponent
    {
    }
}