using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.GUI.ContextMenu;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Library.Utils;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [MyComponent(typeof(MyObjectBuilder_EquiEntityControllerComponent))]
    [MyDependency(typeof(MyAnimationControllerComponent), Critical = false)]
    [MyDependency(typeof(MyCharacterMovementComponent), Critical = false)]
    [ReplicatedComponent]
    public class EquiEntityControllerComponent : MyEntityComponent, IMyEventProxy
    {
        private readonly MyObjectBuilder_EquiEntityControllerComponent _saveData = new MyObjectBuilder_EquiEntityControllerComponent();
        private EquiEntityControllerTracker _tracker;
        private EquiPlayerAttachmentComponent.Slot _controlledSlot;
        private MyContextMenu _openMenu;

        [Automatic]
        private readonly MyCharacterMovementComponent _characterMovement;

        public int AnimationId => _saveData.AnimationId;

        public delegate void DelControlledChanged(EquiEntityControllerComponent controllerComponent, EquiPlayerAttachmentComponent.Slot old,
            EquiPlayerAttachmentComponent.Slot @new);

        #region API

        public event DelControlledChanged ControlledChanged;

        public EquiPlayerAttachmentComponent.Slot Controlled
        {
            get => _controlledSlot;
            set
            {
                if (value == _controlledSlot)
                    return;
                if (value != null)
                    RequestControl(value);
                else
                    ReleaseControl();
            }
        }

        public void RequestControl(EquiPlayerAttachmentComponent.Slot slot) => ChangeControl(slot, false);

        public void RequestControlAndConfigure(EquiPlayerAttachmentComponent.Slot slot) => ChangeControl(slot, true);

        public void ReleaseControl() => ChangeControl(null, false);

        private void ChangeControl(EquiPlayerAttachmentComponent.Slot value, bool andConfigure)
        {
            if (_controlledSlot == value)
                return;
            if (value == null)
                _tracker?.ReleaseControl(this);
            else
                _tracker?.RequestControl(this, value, andConfigure);
        }

        #endregion

        private static readonly Random Rand = new Random();

        internal bool RequestControlInternal(EquiPlayerAttachmentComponent.Slot slot, bool andConfigure)
        {
            var seed = (float)Rand.NextDouble();
            ChangeSlotInternal(slot, seed, andConfigure);
            MyAPIGateway.Multiplayer?.RaiseEvent(this, x => x.ChangeControlledClient,
                slot.Controllable.Entity.EntityId, slot.Definition.Name, seed, andConfigure);
            return true;
        }

        internal bool ReleaseControlInternal()
        {
            ChangeSlotInternal(null, 0f, false);
            MyAPIGateway.Multiplayer?.RaiseEvent(this, x => x.ChangeControlledClient, 0L, "", 0f, false);
            return true;
        }

        [Event]
        [Reliable]
        [Broadcast]
        private void ChangeControlledClient(long entity, string slot, float randSeed, bool andConfigure)
        {
            Scene.TryGetEntity(entity, out var entityObj);
            ChangeSlotInternal(entityObj?.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(slot),
                randSeed, andConfigure);
        }

        [Update(false)]
        private void CommitAnimationStart(long dt)
        {
            var adata = _controlledSlot?.Definition.ByIndex(_saveData.AnimationId);
            if (!adata.HasValue)
                return;
            this.GetLogger().Debug($"Delayed animation start for {adata.Value.Start}");
            var animController = Entity.Components.Get<MyAnimationControllerComponent>();
            animController?.Controller?.TriggerAction(adata.Value.Start);
        }

        internal void ChangeSlotInternal(EquiPlayerAttachmentComponent.Slot slot, float randSeed, bool andConfigure)
        {
            this.GetLogger().Debug($"Changing slot for {Entity} to {slot?.Controllable?.Entity}#{slot?.Definition.Name}");
            CloseConfiguration();

            var old = _controlledSlot;
            _controlledSlot = slot;
            // Always reset lean state when entering.
            slot?.UpdateLeanState(false, true);

            var oldAnimData = old?.Definition.ByIndex(_saveData.AnimationId);

            EquiPlayerAttachmentComponentDefinition.AnimationDesc? newAnimData = null;
            if (slot != null && _saveData.ControlledSlot == slot.Definition.Name)
                newAnimData = slot.Definition.ByIndex(_saveData.AnimationId);
            if (slot?.ForceAnimationId != null)
            {
                newAnimData = slot.Definition.ByIndex(slot.ForceAnimationId.Value);
                _saveData.AnimationId = slot.ForceAnimationId.Value;
            }

            if (!newAnimData.HasValue)
                newAnimData = slot?.Definition.SelectAnimation(Entity.DefinitionId ?? default(MyDefinitionId), randSeed, out _saveData.AnimationId);

            // Handles animation controller switching
            var animController = Entity.Components.Get<MyAnimationControllerComponent>();
            if (animController != null)
            {
                PerformAnimationTransition(animController, oldAnimData, newAnimData);
            }

            // Handle restoring character's position
            if (slot == null && old?.Controllable?.Entity != null && old.Controllable.Entity.InScene)
            {
                var relMatrix = _saveData.RelativeOrientation.GetMatrix();
                if (relMatrix.Scale.AbsMax() < 1)
                    relMatrix = MatrixD.Identity;
                var outPos = relMatrix * old.RawAttachMatrix;
                var gravity = Vector3.Normalize(MyGravityProviderSystem.CalculateTotalGravityInPoint(outPos.Translation));
                var rightCandidate = Vector3.Cross(gravity, (Vector3)outPos.Forward);
                if (rightCandidate.LengthSquared() < 0.5f)
                    rightCandidate = MyUtils.GetRandomVector3();
                var correctedForward = Vector3.Normalize(Vector3.Cross(rightCandidate, gravity));
                outPos = MatrixD.CreateWorld(outPos.Translation, correctedForward, -gravity);
                var transformedCenter = Vector3.TransformNormal(Entity.PositionComp.LocalAABB.Center, outPos);
                var orientation = Quaternion.CreateFromRotationMatrix(outPos);
                var originalHalfExtents = Entity.PositionComp.LocalAABB.HalfExtents;
                var halfExtents = new Vector3(originalHalfExtents.X * 0.8f, originalHalfExtents.Y, originalHalfExtents.Z * 0.8f);

                const int maxUpwardShifts = 4;
                var shiftDistance = 0f;
                for (var i = 0; i <= maxUpwardShifts; i++)
                {
                    shiftDistance = (float)Math.Pow(i, 1.5f);
                    var outPosCenter = outPos.Translation + transformedCenter - gravity * shiftDistance;
                    if (i < maxUpwardShifts)
                    {
                        var translate = FindFreePlaceImproved(outPosCenter, orientation, halfExtents, gravity);
                        if (!translate.HasValue) continue;
                        outPos.Translation = translate.Value - transformedCenter;
                        break;
                    }
                    else
                    {
                        var translate = MyEntities.FindFreePlace(outPosCenter, orientation, halfExtents,
                            1000, 50, 0.1f,
                            /* on the last try push to the surface */ true);
                        if (translate.HasValue)
                            outPos.Translation = translate.Value - transformedCenter;
                        else
                            outPos.Translation = old.AttachMatrix.Translation;
                        break;
                    }
                }

                // Final clean up with minor shift to get out of overlapping any surfaces
                var finalShift = MyEntities.FindFreePlace(outPos.Translation + transformedCenter, orientation,
                    originalHalfExtents * 1.05f, 250, 50, 0.025f, false);
                if (finalShift.HasValue)
                    outPos.Translation = finalShift.Value - transformedCenter;

                if (MyAPIGateway.Physics != null && MyAPIGateway.Physics.CastRay(outPos.Translation - gravity, outPos.Translation + (1 + shiftDistance) * gravity, out var hit))
                    outPos.Translation = hit.Position;
                Entity.PositionComp.SetWorldMatrix(outPos, Entity.Parent, true);

                // Give the player 5 seconds of health immunity when they leave a chair to prevent collisions from killing them if we couldn't
                // find a free space
                Entity?.Get<MyCharacterDamageComponent>()?.AddTemporaryHealthImmunity(5);
            }

            // Handles storing the character's position when attaching
            if (slot != null)
                _saveData.RelativeOrientation = new MyPositionAndOrientation(MatrixD.Normalize(Entity.WorldMatrix * MatrixD.Invert(slot.RawAttachMatrix)));


            // Handle keeping the physics in check
            if (Entity.Physics != null)
            {
                var wantsPhysicsEnabled = slot == null;
                if (wantsPhysicsEnabled && !Entity.Physics.Enabled)
                    Entity.Physics.Activate();
                else if (!wantsPhysicsEnabled && Entity.Physics.Enabled)
                    Entity.Physics.Deactivate();
                if (slot == null)
                {
                    var oldPhys = old?.Controllable?.Entity.ParentedPhysics();
                    if (oldPhys != null)
                        Entity.Physics.LinearVelocity = oldPhys.GetVelocityAtPoint(old.Controllable.Entity.WorldMatrix.Translation);
                }
            }

            _saveData.ControlledEntity = slot?.Controllable?.Entity.EntityId ?? 0;
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

            if (andConfigure && Entity == MySession.Static.PlayerEntity && Controlled != null)
                OpenConfiguration();
        }

        private void PerformAnimationTransition(MyAnimationControllerComponent controller,
            EquiPlayerAttachmentComponentDefinition.AnimationDesc? from,
            EquiPlayerAttachmentComponentDefinition.AnimationDesc? to)
        {
            if (from.HasValue && to.HasValue && _tracker.TryGetAnimationControllerIndex(controller, out var index)
                && index.HasDirectTransition(from.Value.Start, to.Value.Start))
            {
                this.GetLogger().Debug($"Using direct transition from {from.Value.Start} to {to.Value.Start}");
                controller.Controller?.TriggerAction(to.Value.Start);
                return;
            }

            if (from.HasValue)
            {
                this.GetLogger().Debug($"Stopping animation for {from.Value.Start} using {from.Value.Stop}");
                controller.Controller?.TriggerAction(from.Value.Stop);
            }

            if (to.HasValue)
                AddScheduledCallback(CommitAnimationStart, MyEngineConstants.UPDATE_STEP_SIZE_IN_MILLISECONDS + 3);
        }

        private static Vector3D? FindFreePlaceImproved(Vector3D pos, Quaternion orientation, Vector3 halfExtents, Vector3 gravity)
        {
            if (IsFreePlace(pos, orientation, halfExtents))
                return pos;
            var distanceStepSize = halfExtents.Length() / 10f;
            for (var distanceStep = 1; distanceStep <= 20; distanceStep++)
            {
                var distance = distanceStep * distanceStepSize;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var dir = MyUtils.GetRandomVector3HemisphereNormalized(-gravity);
                    var testPos = pos + dir * distance;
                    if (IsFreePlace(testPos, orientation, halfExtents))
                    {
                        var planet = MyPlanets.GetPlanets()[0];
                        var centerVoxData = planet.WorldPositionToStorage(testPos);
                        VoxelData.Resize(new Vector3I(1));
                        planet.Storage.ReadRange(VoxelData, MyStorageDataTypeFlags.Content, 0, centerVoxData, centerVoxData);
                        return testPos;
                    }
                }
            }

            return null;
        }

        private static bool IsFreePlace(Vector3D pos, Quaternion orientation, Vector3 halfExtents)
        {
            return MyEntities.FindFreePlace(pos, orientation, halfExtents, 0, 1, 1, false).HasValue
                   && !IntersectsVoxelSurface(new OrientedBoundingBoxD(pos, halfExtents, orientation));
        }

        private static readonly MyStorageData VoxelData = new MyStorageData(MyStorageDataTypeFlags.Content);

        private static bool IntersectsVoxelSurface(OrientedBoundingBoxD box)
        {
            var data = VoxelData;
            using (PoolManager.Get(out List<MyEntity> entities))
            {
                MyGamePruningStructure.GetTopmostEntitiesInBox(box.GetAABB(), entities, MyEntityQueryType.Static);
                foreach (var ent in entities)
                    if (ent is MyVoxelBase voxel && !(ent is MyVoxelPhysics))
                    {
                        var invWorld = voxel.PositionComp.WorldMatrixInvScaled;
                        var storageBounds = BoundingBoxD.CreateInvalid();
                        var voxelOffset = (voxel.Size >> 1) + voxel.StorageMin;
                        var storageObb = box;
                        storageObb.Transform(invWorld);
                        storageObb.HalfExtent /= voxel.VoxelSize;
                        storageObb.Center = storageObb.Center / voxel.VoxelSize + voxelOffset;
                        storageBounds.Include(storageObb.GetAABB());

                        var storageMin = Vector3I.Max(Vector3I.Floor(storageBounds.Min), voxel.StorageMin);
                        var storageMax = Vector3I.Min(Vector3I.Ceiling(storageBounds.Max), voxel.StorageMax);
                        var localBox = new BoundingBoxI(storageMin, storageMax);
                        localBox.Inflate(1);
                        var floatBox = new BoundingBox(localBox);
                        if (voxel.IntersectStorage(ref floatBox) == ContainmentType.Disjoint)
                            continue;
                        data.Resize(storageMin, storageMax);
                        voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Content, 0, storageMin, storageMax);
                        foreach (var pt in new BoundingBoxI(Vector3I.Zero, storageMax - storageMin).EnumeratePoints())
                        {
                            var voxelBox = new BoundingBoxD(storageMin + pt, storageMin + pt + 1);
                            var containment = storageObb.Contains(ref voxelBox);
                            if (containment == ContainmentType.Disjoint)
                                continue;
                            var tmpPt = pt;
                            var index = data.ComputeLinear(ref tmpPt);
                            var content = data.Content(index);
                            if (containment == ContainmentType.Intersects && content >= 127)
                                return true;
                            if (containment == ContainmentType.Contains && content > 0)
                                return true;
                        }
                    }
            }

            return false;
        }

        private const int PriorityOverride = int.MaxValue;

        private const double AutoDetachDistance = 25;

        private bool LocallyControlled => MySession.Static.PlayerEntity == Entity;

        [FixedUpdate(false)]
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

            if (!_tracker.ModifierShift || !LocallyControlled || _characterMovement == null || !slot.Definition.CanShift) return;
            var moveIndicator = _characterMovement.MoveIndicator;
            if (_characterMovement.WantsJump) moveIndicator.Y = 1;
            if (_characterMovement.WantsCrouch) moveIndicator.Y = -1;
            if (moveIndicator == Vector3.Zero && _characterMovement.RotationIndicator == Vector2.Zero) return;
            var newLinearShift = slot.LinearShift + moveIndicator / 100;
            var newAngularShift = slot.AngularShift + new Vector3(_characterMovement.RotationIndicator.X, -_characterMovement.RotationIndicator.Y, 0) * 0.0025f;
            RequestUpdateShift(newLinearShift, newAngularShift);
        }

        public void RequestUpdateShift(Vector3? linearShift = null, Vector3? angularShift = null, float? leanAngle = null)
        {
            var slot = Controlled;
            if (slot == null) return;
            if ((linearShift == null || linearShift.Value.Equals(slot.LinearShift, 1e-4f))
                && (angularShift == null || angularShift.Value.Equals(slot.AngularShift, 1e-4f))
                && (leanAngle == null || Math.Abs(leanAngle.Value - slot.LeanAngle) < 1e-4f))
                return;
            if (slot.UpdateShift(linearShift, angularShift, leanAngle))
                MyAPIGateway.Multiplayer?.RaiseEvent(this, e => e.NetUpdateShift, linearShift, angularShift, leanAngle);
        }

        public void RequestUpdateLean(bool lean)
        {
            var slot = Controlled;
            if (slot == null || slot.LeanState == lean) return;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseEvent(this, e => e.NetUpdateLean, lean);
            else
                slot.UpdateLeanState(lean);
        }

        [Event]
        [Reliable]
        [Server]
        [Broadcast]
        private void NetUpdateShift(Vector3? linearShift, Vector3? angularShift, float? leanAngle)
        {
            var controlled = Controlled;
            if (controlled == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }

            if (CheckNetworkCall() && controlled.UpdateShift(linearShift, angularShift, leanAngle))
                return;
            // Revert on sender
            MyAPIGateway.Multiplayer?.RaiseEvent(
                this, e => e.NetRevertShift,
                controlled.LinearShift, controlled.AngularShift, controlled.LeanAngle,
                MyEventContext.Current.Sender);
            MyEventContext.ValidationFailed();
        }

        [Event]
        [Reliable]
        [Client]
        private void NetRevertShift(Vector3 linearShift, Vector3 angularShift, float leanAngle)
        {
            Controlled?.UpdateShift(linearShift, angularShift, leanAngle);
        }

        [Event]
        [Reliable]
        [Server]
        [Broadcast]
        private void NetUpdateLean(bool lean)
        {
            var controlled = Controlled;
            if (!CheckNetworkCall()) return;
            controlled.UpdateLeanState(lean);
        }

        public void RequestResetPose()
        {
            var slot = Controlled;
            if (slot == null || !slot.Definition.SelectAnimation(Entity.DefinitionId ?? default(MyDefinitionId),
                    MyRandom.Instance.NextFloat(), out var poseId).HasValue)
                return;
            RequestUpdatePose(poseId, true);
        }

        public void RequestUpdatePose(int nextPose, bool reset = false)
        {
            var slot = Controlled;
            if (slot == null) return;
            if (PerformUpdatePose(nextPose, reset))
                MyAPIGateway.Multiplayer?.RaiseEvent(this, e => e.NetUpdatePose, nextPose, reset);
        }

        [Event]
        [Reliable]
        [Server]
        [Broadcast]
        private void NetUpdatePose(int pose, bool reset)
        {
            var controlled = Controlled;
            if (controlled == null)
            {
                MyEventContext.ValidationFailed();
                return;
            }
            if (CheckNetworkCall() && PerformUpdatePose(pose, reset))
                return;
            // Revert on sender
            MyAPIGateway.Multiplayer?.RaiseEvent(
                this, e => e.NetRevertPose,
                controlled.ForceAnimationId ?? _saveData.AnimationId,
                !controlled.ForceAnimationId.HasValue,
                MyEventContext.Current.Sender);
            MyEventContext.ValidationFailed();
        }

        [Event]
        [Reliable]
        [Client]
        private void NetRevertPose(int pose, bool reset) => PerformUpdatePose(pose, reset);

        public void OpenConfiguration()
        {
            if (Controlled == null)
                return;
            _openMenu?.Close();
            _openMenu = MyContextMenuScreen.OpenMenu(Entity, Controlled.Definition.ConfigurationMenu, this, Controlled);
        }

        public void CloseConfiguration()
        {
            _openMenu?.Close();
            _openMenu = null;
        }

        public void ToggleConfiguration()
        {
            if (_openMenu != null && _openMenu.Visible && MyContextMenuScreen.GetContextMenu(Controlled.Definition.ConfigurationMenu) != null)
                CloseConfiguration();
            else
                OpenConfiguration();
        }

        private bool PerformUpdatePose(int nextPose, bool reset)
        {
            var controlled = Controlled;
            if (controlled == null || !controlled.UpdatePose(nextPose)) return false;
            if (reset)
                controlled.ResetPose();
            if (_saveData.AnimationId == nextPose)
                return true;
            var oldAnimData = controlled.Definition.ByIndex(_saveData.AnimationId);
            var newAnimData = controlled.Definition.ByIndex(nextPose);
            this.GetLogger().Debug($"Updating pose to {nextPose}.  (From {oldAnimData?.Stop} to {newAnimData?.Start})");
            _saveData.AnimationId = nextPose;
            var animController = Entity.Components.Get<MyAnimationControllerComponent>();
            if (animController != null)
                PerformAnimationTransition(animController, oldAnimData, newAnimData);
            return true;
        }

        private bool CheckNetworkCall()
        {
            var controlled = Controlled;
            if (controlled == null || controlled.AttachedCharacter != Entity)
            {
                MyEventContext.ValidationFailed();
                return false;
            }

            // Clients don't do validation.
            if (!MyMultiplayerModApi.Static.IsServer)
                return true;

            if (!MyEventContext.Current.IsLocallyInvoked)
            {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(Entity);
                if (player == null || player.SteamUserId != MyEventContext.Current.Sender.Value)
                {
                    MyEventContext.ValidationFailed();
                    return false;
                }
            }

            return true;
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
                Scene.TryGetEntity(_saveData.ControlledEntity, out e);
            if (e == null)
                return;

            if (e.EntityId != _saveData.ControlledEntity)
                return;

            var ctl = e.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(_saveData.ControlledSlot);
            if (ctl != null && Vector3D.DistanceSquared(Entity.PositionComp.WorldMatrix.Translation, ctl.AttachMatrix.Translation) <
                AutoDetachDistance * AutoDetachDistance)
            {
                if (MyMultiplayerModApi.Static.IsServer)
                    RequestControl(ctl);
                else
                    ChangeSlotInternal(ctl, (float)Rand.NextDouble(), false);
            }

            MyEntities.OnEntityAdd -= CheckForEntity;
        }

        #endregion

        public override void OnRemovedFromScene()
        {
            _tracker = null;
            MyEntities.OnEntityAdd -= CheckForEntity;
            base.OnRemovedFromScene();
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