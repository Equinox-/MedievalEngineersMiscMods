using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
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
                EquiEntityControllerComponent c;
                if (!k.Components.TryGet(out c))
                    continue;
                if (MyMultiplayerModApi.Static.IsServer)
                    c.ReleaseControl();
                else
                    c.ChangeSlotInternal(null, 0f);
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
            if (actionEnum != UseActionEnum.Manipulate)
                return;
            if (user != MyAPIGateway.Session.ControlledObject)
                return;
            var state = StateForDummy(dummyName);
            if (state == null)
                return;
            user.Get<EquiEntityControllerComponent>()?.RequestControl(state);
        }

        private static readonly MyActionDescription InvalidActionDesc = new MyActionDescription {Text = MyStringId.GetOrCompute("Bad action")};

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            if (actionEnum != UseActionEnum.Manipulate)
                return InvalidActionDesc;
            var state = StateForDummy(dummyName);
            if (state != null)
                return state.AttachedCharacter != null ? state.Definition.OccupiedActionDesc : state.Definition.EmptyActionDesc;
            return InvalidActionDesc;
        }

        private Slot StateForDummy(string dummy)
        {
            var entry = _definition.AttachmentForDummy(dummy);
            return entry != null ? _states.GetValueOrDefault(entry.Name) : null;
        }

        public UseActionEnum SupportedActions => PrimaryAction | SecondaryAction;
        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
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
            
            public bool UpdateShift(Vector3 linear, Vector3 angular) {
                LinearShift = Vector3.Clamp(linear, Definition.MinLinearShift, Definition.MaxLinearShift);
                AngularShift = Vector3.Clamp(angular, Definition.MinAngularShift, Definition.MaxAngularShift);
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
            
            public MatrixD AttachMatrix => Shift * RawAttachMatrix;

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
                    if (slot.LinearShift != Vector3.Zero || slot.AngularShift != Vector3.Zero)
                        return true;
                return false;
            }
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiPlayerAttachmentComponent) base.Serialize(copy);
            ob.Attached = new List<MyObjectBuilder_EquiPlayerAttachmentComponent.AttachmentData>();
            foreach (var slot in _states)
                if (slot.Value.LinearShift != Vector3.Zero || slot.Value.AngularShift != Vector3.Zero)
                    ob.Attached.Add(new MyObjectBuilder_EquiPlayerAttachmentComponent.AttachmentData
                    {
                        Name = slot.Key,
                        LinearShift = slot.Value.LinearShift,
                        AngularShift = slot.Value.AngularShift,
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
                    slot.UpdateShift(attached.LinearShift, attached.AngularShift);
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
            public SerializableVector3 AngularShift;
        }
    }
}