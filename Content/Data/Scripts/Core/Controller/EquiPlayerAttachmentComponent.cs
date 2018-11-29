using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Medieval.Entities.UseObject;
using Sandbox.Game.Components;
using Sandbox.Game.Entities.Entity.Stats;
using Sandbox.Game.Replication;
using Sandbox.ModAPI;
using VRage;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Systems;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [MyComponent(typeof(MyObjectBuilder_EquiPlayerAttachmentComponent))]
    [MyDependency(typeof(MyUseObjectsComponent), Critical = true)]
    [MyDefinitionRequired]
    [ReplicatedComponent]
    public class EquiPlayerAttachmentComponent : MyEntityComponent, IMyGenericUseObjectInterface, IMyEventProxy
    {
        private EquiPlayerAttachmentComponentDefinition _definition;

        private readonly Dictionary<string, Slot> _states = new Dictionary<string, Slot>();

        public override void Init(MyEntityComponentDefinition def)
        {
            base.Init(def);
            _definition = (EquiPlayerAttachmentComponentDefinition) def;


            _states.Clear();
            foreach (var k in _definition.Attachments)
                _states.Add(k.Name, new Slot(this, k));
        }

        private readonly List<MyUseObjectGeneric> _genericUseObjects = new List<MyUseObjectGeneric>();

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            AddScheduledCallback(RegisterLazy);
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

        private void RegisterLazy(long dt)
        {
            if (Entity == null || !Entity.InScene)
                return;
            var component = Entity.Components.Get<MyUseObjectsComponentBase>();
            _genericUseObjects.Clear();
            component?.GetInteractiveObjects(_genericUseObjects);

            if (_genericUseObjects.Count == 0)
            {
                MyLog.Default.Warning(
                    $"Failed to find use object for {nameof(EquiPlayerAttachmentComponent)} {_definition.Id}");
                foreach (var t in _genericUseObjects)
                    MyLog.Default.Info($"Detector " + t);
                return;
            }

            foreach (var k in _genericUseObjects)
                k.Interface = this;
        }

        public override void OnRemovedFromScene()
        {
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

            RemoveScheduledUpdate(RegisterLazy);
            foreach (var k in _genericUseObjects)
                k.Interface = null;
            _genericUseObjects.Clear();
            base.OnRemovedFromScene();
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

        #endregion

        public class Slot
        {
            public readonly EquiPlayerAttachmentComponentDefinition.ImmutableAttachmentInfo Definition;
            public readonly EquiPlayerAttachmentComponent Controllable;

            private MyEntity _attachedCharacter;

            public MyEntity AttachedCharacter
            {
                get { return _attachedCharacter; }
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

            public MatrixD AttachMatrix => Definition.Anchor.GetMatrix() * Controllable.Entity.WorldMatrix;

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
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiPlayerAttachmentComponent : MyObjectBuilder_EntityComponent
    {
        #region Legacy

        public long? Entity;
        public MyPositionAndOrientation Relative;
        public int AnimationId;

        public bool ShouldSerializeEntity()
        {
            return false;
        }

        public bool ShouldSerializeRelative()
        {
            return false;
        }

        public bool ShouldSerializeAnimationId()
        {
            return false;
        }

        #endregion

        public AttachmentData[] Attached;

        public struct AttachmentData
        {
            [XmlAttribute]
            public string Name;

            public long Entity;
            public MyPositionAndOrientation Relative;
            public int AnimationId;
        }
    }
}