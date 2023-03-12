using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.UseObject;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Entity;
using VRage.Engine;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Utils;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [MyComponent(typeof(MyObjectBuilder_EquiPlayerAttachmentComponent))]
    [MyDependency(typeof(MyUseObjectsComponent), Critical = true)]
    [MyDependency(typeof(MyModelAttachmentComponent), Critical = false)]
    [MyDefinitionRequired(typeof(EquiPlayerAttachmentComponentDefinition))]
    [ReplicatedComponent]
    public class EquiPlayerAttachmentComponent : MyEntityComponent, IMyGenericUseObjectInterfaceFiltered, IMyEventProxy
    {
        private EquiPlayerAttachmentComponentDefinition _definition;

        private readonly Dictionary<string, Slot> _states = new Dictionary<string, Slot>();

#pragma warning disable CS0649
        [Automatic]
        private readonly MyModelAttachmentComponent _modelAttachment;
#pragma warning restore CS0649

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            _definition = (EquiPlayerAttachmentComponentDefinition) def;

            _states.Clear();
            foreach (var k in _definition.Attachments)
                _states.Add(k.Name, new Slot(this, k));
        }
        
        public IEnumerable<Slot> GetSlots()
        {
            return _states.Values;
        }

        public Slot GetSlotOrDefault(string key)
        {
            return _states.GetValueOrDefault(key);
        }

        public MyEntity GetAttachedCharacter(string key)
        {
            return _states.GetValueOrDefault(key)?.AttachedCharacter;
        }

        public IEnumerable<MyEntity> GetAttachedCharacters()
        {
            return _states.Values.Select(x => x.AttachedCharacter).Where(x => x != null);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (_modelAttachment == null) return;
            foreach (var slot in _definition.Attachments)
            foreach (var entity in _modelAttachment.GetAttachedEntities(slot.ModelAttachment))
                ModelAttachment_OnEntityAttached(_modelAttachment, entity);
            _modelAttachment.OnEntityAttached += ModelAttachment_OnEntityAttached;
        }

        public override void OnRemovedFromScene()
        {
            if (_modelAttachment != null)
                _modelAttachment.OnEntityAttached -= ModelAttachment_OnEntityAttached;
            foreach (var state in _states.Values)
            {
                var k = state.AttachedCharacter;
                if (k == null)
                    continue;
                if (!k.Components.TryGet(out EquiEntityControllerComponent c))
                    continue;
                if (MyMultiplayerModApi.Static.IsServer)
                    c.ReleaseControl();
                else
                    c.ChangeSlotInternal(null, 0f, false);
            }
            base.OnRemovedFromScene();
        }

        private void ModelAttachment_OnEntityAttached(MyModelAttachmentComponent mac, MyEntity entity)
        {
            var attachment = mac.GetEntityAttachmentPoint(entity);
            if (attachment == MyStringHash.NullOrEmpty) return;
            var slot = _definition.AttachmentForModelAttachment(attachment);
            if (slot == null) return;
            mac.SetAdditionalMatrix(entity, _states[slot.Name].Shift);
        }

        #region Use Objects

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            if (user != MyAPIGateway.Session.ControlledObject)
                return;
            var state = StateForDummy(dummyName);
            if (state == null)
                return;
            switch (actionEnum)
            {
                case UseActionEnum.Manipulate:
                    user.Get<EquiEntityControllerComponent>()?.RequestControl(state);
                    break;
                case UseActionEnum.OpenTerminal:
                    user.Get<EquiEntityControllerComponent>()?.RequestControlAndConfigure(state);
                    break;
            }
        }

        private static readonly MyActionDescription InvalidActionDesc = new MyActionDescription {Text = MyStringId.GetOrCompute("Bad action")};

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            var state = StateForDummy(dummyName);
            if (state == null)
                return InvalidActionDesc;
            MyActionDescription desc;
            switch (actionEnum)
            {
                case UseActionEnum.Manipulate:
                    desc = state.AttachedCharacter != null ? state.Definition.OccupiedActionDesc : state.Definition.EmptyActionDesc;
                    break;
                case UseActionEnum.OpenTerminal:
                    desc = state.AttachedCharacter != null ? default : state.Definition.ConfigureActionDesc;
                    break;
                default:
                    return InvalidActionDesc;
            }

            desc.FormatParams = new object[] { MyAPIGateway.Input.GetLocalizedInteractionButton() };
            return desc;
        }

        private Slot StateForDummy(string dummy)
        {
            var entry = _definition.AttachmentForDummy(dummy);
            return entry != null ? _states.GetValueOrDefault(entry.Name) : null;
        }

        public UseActionEnum SupportedActions => PrimaryAction | SecondaryAction;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.OpenTerminal;
        public bool ContinuousUsage => false;
        
        public bool AppliesTo(string dummyName)
        {
            return dummyName != null && _definition.AttachmentForDummy(dummyName) != null;
        }

        #endregion

        public class Slot
        {
            public readonly EquiPlayerAttachmentComponentDefinition.ImmutableAttachmentInfo Definition;
            public readonly EquiPlayerAttachmentComponent Controllable;

            private MyEntity _attachedCharacter;

            public Vector3 LinearShift { get; private set; } = Vector3.Zero;
            public Vector3 AngularShift { get; private set; } = Vector3.Zero;
            public Matrix Shift { get; private set; } = Matrix.Identity;
            public int? ForceAnimationId { get; private set; }

            public float LeanAngle { get; private set; } = 0;

            public bool LeanState { get; private set; }
            private float _leanTransitionVisualAngle;
            private float _leanTransitionLastTime;

            public bool UpdateShift(Vector3? linear, Vector3? angular, float? leanAngle = null) {
                LinearShift = Vector3.Clamp(linear ?? LinearShift, Definition.MinLinearShift, Definition.MaxLinearShift);
                var angularShift = Vector3.Clamp(angular ?? AngularShift, Definition.MinAngularShift, Definition.MaxAngularShift);
                if (Definition.MaxAngularShift.Y - Definition.MinAngularShift.Y >= MathHelper.TwoPi)
                {
                    // Wrap yaw.
                    var yaw = angularShift.Y - Definition.MinAngularShift.Y;
                    yaw %= MathHelper.TwoPi;
                    yaw += MathHelper.TwoPi;
                    yaw %= MathHelper.TwoPi;
                    angularShift.Y = yaw + Definition.MinAngularShift.Y;
                }

                LeanAngle = MathHelper.Clamp(leanAngle ?? LeanAngle, Definition.MinLean, Definition.MaxLean);
                if (leanAngle.HasValue)
                    _leanTransitionVisualAngle = LeanTargetAngle;
                AngularShift = angularShift;
                var newMatrix = Matrix.CreateFromYawPitchRoll(AngularShift.Y, AngularShift.X, AngularShift.Z);
                newMatrix.Translation = LinearShift;
                if (Shift == newMatrix) return false;
                Shift = newMatrix;
                var mac = Controllable._modelAttachment;
                if (Definition.ModelAttachment == MyStringHash.NullOrEmpty || mac == null) return true;
                foreach (var entity in mac.GetAttachedEntities(Definition.ModelAttachment))
                    mac.SetAdditionalMatrix(entity, newMatrix);
                return true;
            }

            public bool UpdatePose(int pose)
            {
                if (pose < 0 || pose >= Definition.AnimationCount) return false;
                ForceAnimationId = pose;
                return true;
            }

            public void ResetPose()
            {
                ForceAnimationId = null;
            }

            public void UpdateLeanState(bool leanState, bool immediate = false)
            {
                LeanState = leanState;
                if (immediate)
                    _leanTransitionVisualAngle = LeanTargetAngle;
            }

            public MyEntity AttachedCharacter
            {
                get => _attachedCharacter;
                internal set
                {
                    var old = _attachedCharacter;
                    if (old == value)
                        return;

                    if (_attachedCharacter != null && _scheduledUpdates != null)
                        foreach (var k in _scheduledUpdates)
                            k.CharacterDetached();

                    _attachedCharacter = value;

                    if (_attachedCharacter != null && _scheduledUpdates != null)
                        foreach (var k in _scheduledUpdates)
                            k.CharacterAttached();

                    AttachedCharacterChanged?.Invoke(this, old, value);
                }
            }

            public MatrixD RawAttachMatrix => Definition.Anchor.GetMatrix() * Controllable.Entity.WorldMatrix;

            private float LeanTargetAngle => LeanState ? LeanAngle : 0;
            
            private float LeanVisualAngle
            {
                get
                {
                    var target = LeanTargetAngle;
                    var currTime = (float)(Controllable.Scheduler?.CurrentUpdateTime.TotalSeconds ?? 0);
                    var dt = currTime - _leanTransitionLastTime;
                    var error = target - _leanTransitionVisualAngle;
                    var delta = Math.Sign(error) * Math.Min(dt * MathHelper.PiOver4, Math.Abs(error));
                    _leanTransitionLastTime = currTime;
                    return _leanTransitionVisualAngle += delta;
                }
            }
            
            public MatrixD AttachMatrix
            {
                get
                {
                    var result = Shift * RawAttachMatrix;
                    var leanVisualAngle = LeanVisualAngle;
                    if (leanVisualAngle != 0)
                        result = Matrix.CreateRotationZ(-leanVisualAngle) * result;
                    return result;
                }
            }

            public event AttachedCharacterChangedDelegate AttachedCharacterChanged;

            public delegate void AttachedCharacterChangedDelegate(Slot slot, MyEntity old, MyEntity @new);

            private readonly ScheduledUpdateCache[] _scheduledUpdates;

            internal Slot(EquiPlayerAttachmentComponent controllable, EquiPlayerAttachmentComponentDefinition.ImmutableAttachmentInfo def)
            {
                Controllable = controllable;
                Definition = def;

                if (def.EffectOperations != null && def.EffectOperations.Count > 0 && MyMultiplayerModApi.Static.IsServer)
                {
                    _scheduledUpdates = new ScheduledUpdateCache[def.EffectOperations.Count];
                    for (var i = 0; i < _scheduledUpdates.Length; i++)
                        _scheduledUpdates[i] = new ScheduledUpdateCache(this, def.EffectOperations[i]);
                }
                else
                {
                    _scheduledUpdates = null;
                }
            }

            private class ScheduledUpdateCache
            {
                public readonly Slot Slot;
                public readonly EquiPlayerAttachmentComponentDefinition.ImmutableEffectOperations Operation;
                public readonly MyTimedUpdate Delegate;

                public ScheduledUpdateCache(Slot slot, EquiPlayerAttachmentComponentDefinition.ImmutableEffectOperations def)
                {
                    Slot = slot;
                    Operation = def;
                    if (def.When == MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Continuous || def.DelayMs > 0)
                        Delegate = Apply;
                    else
                        Delegate = null;
                }

                public void CharacterAttached()
                {
                    // ReSharper disable once SwitchStatementMissingSomeCases
                    switch (Operation.When)
                    {
                        case MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Enter:
                        {
                            if (Operation.DelayMs > 0)
                                Slot.Controllable.AddScheduledCallback(Delegate, Operation.DelayMs);
                            else
                                Apply(0);
                            break;
                        }
                        case MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Continuous:
                            Slot.Controllable.AddScheduledUpdate(Delegate, Operation.IntervalMs);
                            break;
                    }
                }

                public void CharacterDetached()
                {
                    if (Operation.When == MyObjectBuilder_EquiPlayerAttachmentComponentDefinition.EffectOperationsInfo.TriggerTime.Leave)
                        Apply(0);
                    if (Delegate != null)
                        Slot.Controllable.RemoveScheduledUpdate(Delegate);
                }

                [Update(false)]
                private void Apply(long dt)
                {
                    var effect = Slot.AttachedCharacter?.Get<MyEntityStatComponent>();
                    if (effect == null)
                        return;
                    foreach (var op in Operation.Operations)
                        effect.AddOperation(op, Slot.Controllable.Entity?.EntityId ?? 0);
                }
            }
        }

        public override bool IsSerialized
        {
            get
            {
                foreach (var slot in _states.Values)
                    if (slot.LinearShift != Vector3.Zero || slot.AngularShift != Vector3.Zero || slot.LeanAngle != 0 || slot.ForceAnimationId.HasValue)
                        return true;
                return false;
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponent) base.Serialize(copy);
            ob.Attached = new List<MyObjectBuilder_EquiPlayerAttachmentComponent.AttachmentData>();
            foreach (var slot in _states)
                if (slot.Value.LinearShift != Vector3.Zero || slot.Value.AngularShift != Vector3.Zero || slot.Value.ForceAnimationId.HasValue)
                    ob.Attached.Add(new MyObjectBuilder_EquiPlayerAttachmentComponent.AttachmentData
                    {
                        Name = slot.Key,
                        LinearShift = slot.Value.LinearShift,
                        AngularShift = slot.Value.AngularShift,
                        LeanAngle = slot.Value.LeanAngle,
                        Pose = slot.Value.ForceAnimationId,
                    });
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponent)builder;
            if (ob.Attached == null) return;
            foreach (var attached in ob.Attached)
                if (_states.TryGetValue(attached.Name, out var slot))
                {
                    slot.UpdateShift(attached.LinearShift, attached.AngularShift, attached.LeanAngle);
                    if (attached.Pose.HasValue)
                        slot.UpdatePose(attached.Pose.Value);
                }
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlayerAttachmentComponent : MyObjectBuilder_EntityComponent
    {
        public List<AttachmentData> Attached;

        public struct AttachmentData
        {
            [XmlAttribute]
            public string Name;

            public SerializableVector3 LinearShift;

            public bool ShouldSerializeLinearShift() => LinearShift != default;

            public SerializableVector3 AngularShift;

            public bool ShouldSerializeAngularShift() => AngularShift != default;

            public float LeanAngle;

            public bool ShouldSerializeLeanAngle() => LeanAngle != default;

            public int? Pose;
        }
    }
}