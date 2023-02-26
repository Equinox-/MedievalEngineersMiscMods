using System;
using System.Xml.Serialization;
using Medieval.Definitions;
using Medieval.Entities.Components.Decay;
using ObjectBuilders.Definitions;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Misc
{
    [MyComponent(typeof(MyObjectBuilder_EquiDecayComponent))]
    [MyDependency(typeof(MyPositionComponentBase), Recursive = true)]
    [MyDefinitionRequired(typeof(EquiDecayComponentDefinition))]
    public class EquiDecayComponent : MySimpleDecayComponent
    {
        [Automatic]
        private readonly MyPositionComponentBase _position;
        
        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            if (Definition.MovementFlags != EquiDecayComponentDefinition.DecayMovementFlags.All && MyMultiplayerModApi.Static.IsServer)
                _position.OnPositionChanged += OnPositionChanged;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            _position.OnPositionChanged -= OnPositionChanged;
            base.OnBeforeRemovedFromContainer();
        }

        public override void OnAddedToScene()
        {
            Enabled = false;
            base.OnAddedToScene();
            UpdateEnabled(false);
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            _backgroundUpdating = false;
        }

        private void OnPositionChanged(MyPositionComponentBase obj)
        {
            if (_backgroundUpdating || !Entity.InScene) return;
            AddScheduledUpdate(UpdateBackground, 5000);
            _backgroundUpdating = true;
        }


        private bool _backgroundUpdating;
        [Update(false)]
        private void UpdateBackground(long dt)
        {
            UpdateEnabled(true);
        }

        private void UpdateEnabled(bool resetDecayIfNeeded)
        {
            var physics = Entity?.Physics;
            if (physics == null || !Entity.InScene)
                return;
            EquiDecayComponentDefinition.DecayMovementFlags movementFlags;
            if (physics.IsStatic || physics.IsKinematic)
                movementFlags = EquiDecayComponentDefinition.DecayMovementFlags.Static;
            else if (physics.LinearVelocity.LengthSquared() > .01f || physics.AngularVelocity.LengthSquared() > .01f)
                movementFlags = EquiDecayComponentDefinition.DecayMovementFlags.Moving;
            else
                movementFlags = EquiDecayComponentDefinition.DecayMovementFlags.Sleeping;
            var shouldEnable = (Definition.MovementFlags & movementFlags) != 0;
            if (shouldEnable && resetDecayIfNeeded && !Enabled)
                ResetDecayTime();
            Enabled = shouldEnable;
        }

        private new EquiDecayComponentDefinition Definition => (EquiDecayComponentDefinition)base.Definition;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            ResetDecayTime();
        }

        private void ResetDecayTime()
        {
            DecayTime = GetInitialDecayTime() + MyRandom.Instance.Next((int)Definition.DecayTimeJitter.TotalMilliseconds);
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiDecayComponent)base.Serialize(copy);
            if (Definition.SaveProgress)
                ob.TimeToUpdate = DecayTime;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiDecayComponent)builder;
            if (Definition.SaveProgress)
                DecayTime = ob.TimeToUpdate;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecayComponent : MyObjectBuilder_SimpleDecayComponent
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecayComponentDefinition))]
    public class EquiDecayComponentDefinition : MySimpleDecayComponentDefinition
    {
        [Flags]
        public enum DecayMovementFlags
        {
            Static = 1,
            Sleeping = 2,
            Moving = 4,
            All = Static | Sleeping | Moving,
        }

        public bool SaveProgress { get; private set; }
        public DecayMovementFlags MovementFlags { get; private set; }
        public TimeSpan DecayTimeJitter { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            var ob = (MyObjectBuilder_EquiDecayComponentDefinition)def;
            SaveProgress = ob.SaveProgress ?? false;
            if (SaveProgress)
                ob.Serialize = true;
            base.Init(def);

            DecayTimeJitter = ob.DecayTimeJitter.HasValue ? (TimeSpan)ob.DecayTimeJitter : TimeSpan.FromTicks(DecayTime.Ticks / 4);
            if (ob.StaticDecay == null && ob.SleepingDecay == null && ob.MovingDecay == null)
                MovementFlags = DecayMovementFlags.Static | DecayMovementFlags.Sleeping | DecayMovementFlags.Moving;
            else
            {
                MovementFlags = 0;
                if (ob.StaticDecay ?? false)
                    MovementFlags |= DecayMovementFlags.Static;
                if (ob.SleepingDecay ?? false)
                    MovementFlags |= DecayMovementFlags.Sleeping;
                if (ob.MovingDecay ?? false)
                    MovementFlags |= DecayMovementFlags.Moving;
            }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecayComponentDefinition : MyObjectBuilder_SimpleDecayComponentDefinition
    {
        public TimeDefinition? DecayTimeJitter;
        public bool? SaveProgress;
        public bool? StaticDecay;
        public bool? SleepingDecay;
        public bool? MovingDecay;
    }
}