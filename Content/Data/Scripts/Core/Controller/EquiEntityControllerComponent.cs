using System;
using System.Xml.Serialization;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Replication;
using Sandbox.ModAPI;
using VRage;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [MyComponent(typeof(MyObjectBuilder_EquiEntityControllerComponent))]
    [MyDependency(typeof(MyAnimationControllerComponent), Critical = false)]
    [ReplicatedComponent]
    public class EquiEntityControllerComponent : MyEntityComponent, IMyEventProxy
    {
        private readonly MyObjectBuilder_EquiEntityControllerComponent _saveData = new MyObjectBuilder_EquiEntityControllerComponent();
        private EquiEntityControllerTracker _tracker;

        private EquiPlayerAttachmentComponent.Slot _controlledSlot;

        public delegate void DelControlledChanged(EquiEntityControllerComponent controllerComponent, EquiPlayerAttachmentComponent.Slot old,
            EquiPlayerAttachmentComponent.Slot @new);

        #region API

        public event DelControlledChanged ControlledChanged;

        public EquiPlayerAttachmentComponent.Slot Controlled
        {
            get { return _controlledSlot; }
            set
            {
                if (_controlledSlot == value)
                    return;
                if (value == null)
                    _tracker?.ReleaseControl(this);
                else
                    _tracker?.RequestControl(this, value);
            }
        }

        public void RequestControl(EquiPlayerAttachmentComponent.Slot slot)
        {
            Controlled = slot;
        }

        public void ReleaseControl()
        {
            Controlled = null;
        }

        #endregion

        private static readonly Random Rand = new Random();

        internal bool RequestControlInternal(EquiPlayerAttachmentComponent.Slot slot)
        {
            var seed = (float) Rand.NextDouble();
            ChangeSlotInternal(slot, seed);
            MyAPIGateway.Multiplayer?.RaiseEvent(this, x => x.ChangeControlledClient, slot.Controllable.Entity.EntityId, slot.Definition.Name, seed);
            return true;
        }

        internal bool ReleaseControlInternal()
        {
            ChangeSlotInternal(null, 0f);
            MyAPIGateway.Multiplayer?.RaiseEvent(this, x => x.ChangeControlledClient, 0L, "", 0f);
            return true;
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void ChangeControlledClient(long entity, string slot, float randSeed)
        {
            ChangeSlotInternal(MyEntities.GetEntityByIdOrDefault(entity)?.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(slot),
                randSeed);
        }

        private void CommitAnimationStart(long dt)
        {
            var adata = _controlledSlot?.Definition.ByIndex(_saveData.AnimationId);
            if (!adata.HasValue)
                return;
            var animController = Entity.Components.Get<MyAnimationControllerComponent>();
            animController?.TriggerAction(adata.Value.Start);
        }

        internal void ChangeSlotInternal(EquiPlayerAttachmentComponent.Slot slot, float randSeed)
        {
            MyLog.Default.WriteLine("Changing slot for " + Entity + " to " + slot?.Controllable?.Entity + "#" + slot?.Definition.Name);
            var old = _controlledSlot;
            _controlledSlot = slot;

            var oldAnimData = old?.Definition.ByIndex(_saveData.AnimationId);

            EquiPlayerAttachmentComponentDefinition.AnimationDesc? newAnimData = null;
            if (slot != null && _saveData.ControlledSlot == slot.Definition.Name)
                newAnimData = slot.Definition.ByIndex(_saveData.AnimationId);
            if (!newAnimData.HasValue)
                newAnimData = slot?.Definition.SelectAnimation(Entity.DefinitionId ?? default(MyDefinitionId), randSeed, out _saveData.AnimationId);

            // Handles animation controller switching
            var animController = Entity.Components.Get<MyAnimationControllerComponent>();
            if (animController != null)
            {
                if (oldAnimData.HasValue)
                    animController.TriggerAction(oldAnimData.Value.Stop);
                if (newAnimData.HasValue)
                    AddScheduledCallback(CommitAnimationStart);
            }

            // Handle keeping the physics in check
            if (Entity.Physics != null)
            {
                Entity.Physics.Enabled = slot == null;
                if (slot == null)
                {
                    var oldPhys = old?.Controllable?.Entity.ParentedPhysics();
                    if (oldPhys != null)
                        Entity.Physics.LinearVelocity = oldPhys.GetVelocityAtPoint(old.Controllable.Entity.WorldMatrix.Translation);
                }
            }

            // Handle restoring character's position
            if (slot == null && old?.Controllable?.Entity != null && old.Controllable.Entity.InScene)
            {
                var relMatrix = _saveData.RelativeOrientation.GetMatrix();
                if (relMatrix.Scale.AbsMax() < 1)
                    relMatrix = MatrixD.Identity;
                var outPos = relMatrix * old.AttachMatrix;
                var translate = MyAPIGateway.Entities.FindFreePlace(outPos.Translation + Entity.PositionComp.LocalVolume.Center,
                    Entity.PositionComp.LocalVolume.Radius * .6f, 200, 20, 2f);
                if (translate.HasValue)
                    outPos.Translation = translate.Value - Entity.PositionComp.LocalVolume.Center;
                else
                    outPos = old.AttachMatrix;
                Entity.PositionComp.SetWorldMatrix(outPos, Entity.Parent, true);
            }

            // Handles storing the character's position when attaching
            if (slot != null)
                _saveData.RelativeOrientation = new MyPositionAndOrientation(MatrixD.Normalize(Entity.WorldMatrix * MatrixD.Invert(slot.AttachMatrix)));

            _saveData.ControlledEntity = slot?.Controllable.Entity.EntityId ?? 0;
            _saveData.ControlledSlot = slot?.Definition.Name;
            if (slot == null)
                _tracker.Unlink(Entity.EntityId);
            else
                _tracker.Link(Entity.EntityId, new ControlledId(slot));

            if (old?.Controllable != null)
                RemoveFixedUpdate(FixPosition);

            if (slot?.Controllable != null)
                AddFixedUpdate(FixPosition, PriorityOverride);

            FixPosition();

            if (old != null)
                old.AttachedCharacter = null;
            if (slot != null)
                slot.AttachedCharacter = Entity;
            ControlledChanged?.Invoke(this, old, slot);
            _tracker.RaiseControlledChange(this, old, slot);
        }
        
        private const int PriorityOverride = int.MaxValue;

        private const double AutoDetachDistance = 25;

        private void FixPosition()
        {
            var slot = Controlled;
            if (slot?.Controllable?.Entity == null || MyAPIGateway.Session == null || Entity?.PositionComp == null)
                return;
            if (MyMultiplayerModApi.Static.IsServer &&
                (Vector3D.DistanceSquared(Entity.PositionComp.WorldMatrix.Translation, slot.AttachMatrix.Translation) > AutoDetachDistance * AutoDetachDistance
                 || !slot.Controllable.Entity.InScene))
                ReleaseControl();
            else
                Entity.PositionComp.WorldMatrix = slot.AttachMatrix;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            _tracker = MySession.Static.Components.Get<EquiEntityControllerTracker>();
            if (_saveData.ControlledEntity != 0 && _saveData.ControlledSlot != null)
            {
                MyEntities.OnEntityAdd += CheckForEntity;
                CheckForEntity(null);
            }
        }

        #region Auto-attach from save

        private void CheckForEntity(MyEntity e)
        {
            if (_saveData.ControlledEntity == 0 || !Entity.InScene)
            {
                MyEntities.OnEntityAdd -= CheckForEntity;
                return;
            }

            if (e == null)
                MyEntities.TryGetEntityById(_saveData.ControlledEntity, out e);
            if (e == null)
                return;

            if (e.EntityId != _saveData.ControlledEntity)
                return;

            var ctl = e.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(_saveData.ControlledSlot);
            if (ctl != null)
            {
                if (MyMultiplayerModApi.Static.IsServer)
                    RequestControl(ctl);
                else
                    ChangeSlotInternal(ctl, (float) Rand.NextDouble());
            }

            MyEntities.OnEntityAdd -= CheckForEntity;
        }

        #endregion

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();
            _tracker = null;
            MyEntities.OnEntityAdd -= CheckForEntity;
        }

        #region Serialization

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            (builder as MyObjectBuilder_EquiEntityControllerComponent)?.CopyTo(_saveData);
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var result = base.Serialize(copy);
            var dat = result as MyObjectBuilder_EquiEntityControllerComponent;
            if (dat != null)
                _saveData.CopyTo(dat);
            return result;
        }

        public override bool IsSerialized => _saveData.ControlledEntity != 0;

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiEntityControllerComponent : MyObjectBuilder_EntityComponent
    {
        public long ControlledEntity;
        public string ControlledSlot;
        public int AnimationId;
        public MyPositionAndOrientation RelativeOrientation;

        internal void CopyTo(MyObjectBuilder_EquiEntityControllerComponent ctl)
        {
            ctl.ControlledEntity = ControlledEntity;
            ctl.ControlledSlot = ControlledSlot;
            ctl.AnimationId = AnimationId;
            ctl.RelativeOrientation = RelativeOrientation;
        }
    }
}