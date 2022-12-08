using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions.Animation;
using VRage.Game.Input;
using VRage.Network;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Controller
{
    [StaticEventOwner]
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    public class EquiEntityControllerTracker : MySessionComponent
    {
        private readonly Dictionary<long, ControlledId> _controllerToControlled = new Dictionary<long, ControlledId>();
        private readonly Dictionary<ControlledId, long> _controlledToController = new Dictionary<ControlledId, long>();

        private readonly Dictionary<MyDefinitionId, IndexedAnimationController> _indexedAnimationControllers =
            new Dictionary<MyDefinitionId, IndexedAnimationController>(MyDefinitionId.Comparer);

        internal void Link(long controller, ControlledId key)
        {
            if (_controlledToController.TryGetValue(key, out var existingController))
            {
                if (existingController == controller)
                    return;
                this.GetLogger().Warning($"Controllable slot {key} is already controlled by {existingController}");
                Unlink(existingController);
            }

            if (_controllerToControlled.TryGetValue(controller, out var existingControlled))
            {
                if (existingControlled.Equals(key))
                    return;
                this.GetLogger().Warning($"Controller {controller} is already controlling {existingControlled}");
                Unlink(controller);
            }

            _controlledToController.Add(key, controller);
            _controllerToControlled.Add(controller, key);
        }

        internal void Unlink(long controller)
        {
            if (_controllerToControlled.TryGetValue(controller, out var existingControlled))
                _controlledToController.Remove(existingControlled);
            _controllerToControlled.Remove(controller);
        }

        public delegate void DelControlledChanged(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlledOld,
            EquiPlayerAttachmentComponent.Slot controlledNew);

        public event DelControlledChanged ControlledChanged;
        public bool ModifierShift { get; private set; }

        internal void RaiseControlledChange(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlledOld,
            EquiPlayerAttachmentComponent.Slot controlledNew)
        {
            ControlledChanged?.Invoke(controller, controlledOld, controlledNew);

            if (controller.Entity == MyAPIGateway.Session.ControlledObject)
                Scheduler.AddScheduledCallback(CheckAttachedControls);
        }

        private bool RequestControlInternal(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlled, bool andConfigure)
        {
            var desiredControlling = new ControlledId(controlled);
            if (_controlledToController.ContainsKey(desiredControlling))
                return false;

            if (_controllerToControlled.TryGetValue(controller.Entity.EntityId, out var currentlyControlling))
            {
                if (currentlyControlling.Equals(controlled))
                    return true;
                if (!ReleaseControlInternal(controller))
                    return false;
            }

            return controller.RequestControlInternal(controlled, andConfigure);
        }

        private bool ReleaseControlInternal(EquiEntityControllerComponent controller)
        {
            ControlledId currentlyControlling;
            if (!_controllerToControlled.TryGetValue(controller.Entity.EntityId, out currentlyControlling))
                return false;

            return controller.ReleaseControlInternal();
        }

        public void RequestControl(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlled, bool andConfigure = false)
        {
            if (controller?.Entity == null || !controller.Entity.InScene)
                return;
            if (controlled?.Controllable?.Entity == null || !controlled.Controllable.Entity.InScene)
                return;
            if (!MyMultiplayerModApi.Static.IsServer && controller.Entity != MyAPIGateway.Session.ControlledObject)
                return;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseStaticEvent(x => RequestControlServer, controller.Entity.EntityId, 
                    controlled.Controllable.Entity.EntityId, controlled.Definition.Name, andConfigure);
            else
                RequestControlServer(controller.Entity.EntityId, controlled.Controllable.Entity.EntityId, controlled.Definition.Name, andConfigure);
        }

        public void ReleaseControl(EquiEntityControllerComponent controller)
        {
            if (controller?.Entity == null || !controller.Entity.InScene)
                return;
            if (!MyMultiplayerModApi.Static.IsServer && controller.Entity != MyAPIGateway.Session.ControlledObject)
                return;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseStaticEvent(x => ReleaseControlServer, controller.Entity.EntityId);
            else
                ReleaseControlServer(controller.Entity.EntityId);
        }

        #region MP Code

        /// <summary>
        /// Clients can't request to be attached to something outside this distance.
        /// </summary>
        private const double TrustedDistance = 50d;

        [Event]
        [Server]
        private static void RequestControlServer(long controller, long entity, string slot, bool andConfigure)
        {
            var controllerComponent = MyEntities.GetEntityByIdOrDefault(controller)?.Components.Get<EquiEntityControllerComponent>();
            if (controllerComponent == null)
                return;
            var controllableSlot = MyEntities.GetEntityByIdOrDefault(entity)?.Components.Get<EquiPlayerAttachmentComponent>()?.GetSlotOrDefault(slot);
            if (controllableSlot == null)
                return;

            if (!MyEventContext.Current.IsLocallyInvoked)
            {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(controllerComponent.Entity);
                if (player == null || player.SteamUserId != MyEventContext.Current.Sender.Value)
                    return;

                if (Vector3D.DistanceSquared(controllerComponent.Entity.WorldMatrix.Translation, controllableSlot.Controllable.Entity.WorldMatrix.Translation) >
                    TrustedDistance * TrustedDistance)
                    return;
            }

            MySession.Static?.Components.Get<EquiEntityControllerTracker>()?.RequestControlInternal(controllerComponent, controllableSlot, andConfigure);
        }

        [Event]
        [Server]
        private static void ReleaseControlServer(long controller)
        {
            var controllerComponent = MyEntities.GetEntityByIdOrDefault(controller)?.Components.Get<EquiEntityControllerComponent>();
            if (controllerComponent == null)
                return;

            if (!MyEventContext.Current.IsLocallyInvoked)
            {
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(controllerComponent.Entity);
                if (player == null || player.SteamUserId != MyEventContext.Current.Sender.Value)
                    return;
            }


            MySession.Static?.Components.Get<EquiEntityControllerTracker>()?.ReleaseControlInternal(controllerComponent);
        }

        #endregion


        #region Controls

        private readonly MyInputContext _attachedControls = new MyInputContext("Attachment controls");

        private readonly MyInputContext _attachedInSeatControls = new MyInputContext("Attachment in-seat controls");

        public EquiEntityControllerTracker()
        {
            _attachedControls.UnregisterAllActions();
            _attachedControls.RegisterAction(MyStringHash.GetOrCompute("LeaveAttached"),  HandleLeave);
            _attachedInSeatControls.UnregisterAllActions();
            _attachedInSeatControls.RegisterAction(MyStringHash.GetOrCompute("ConfigureAttached"), HandleConfigureAttached);
            _attachedInSeatControls.RegisterAction(MyStringHash.GetOrCompute("LeanAttached"), HandleLeanAttached);
        }

        private void HandleLeave(ref MyInputContext.ActionEvent evt)
        {
            var playerEntity = MyAPIGateway.Session.ControlledObject;
            var controller = playerEntity?.Components.Get<EquiEntityControllerComponent>();
            if (controller?.Controlled == null)
            {
                evt.Captured = false;
                return;
            }

            if (playerEntity.Get<MyCharacterDetectorComponent>()?.UseObject != null)
            {
                // Don't release control when a use object is targeted.
                evt.Captured = false;
                return;
            }
            controller.ReleaseControl();
        }

        private void HandleConfigureAttached(ref MyInputContext.ActionEvent evt)
        {
            var controller = MyAPIGateway.Session.ControlledObject?.Components.Get<EquiEntityControllerComponent>();
            controller?.OpenConfiguration();
        }

        private void HandleLeanAttached(ref MyInputContext.ActionEvent evt)
        {
            var controller = MyAPIGateway.Session.ControlledObject?.Components.Get<EquiEntityControllerComponent>();
            var controlled = controller?.Controlled;
            if (controlled == null)
                return;
            controller.RequestUpdateLean(!controlled.LeanState);
        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            Scheduler.AddScheduledCallback(CheckAttachedControls);
            _indexedAnimationControllers.Clear();
            foreach (var def in MyDefinitionManager.GetOfType<MyAnimationControllerDefinition>())
            {
                _indexedAnimationControllers[def.Id] = new IndexedAnimationController(def);
            }
        }

        public bool TryGetAnimationControllerIndex(MyAnimationControllerComponent controller, out IndexedAnimationController index)
        {
            return _indexedAnimationControllers.TryGetValue(controller.SourceId, out index);
        }

        protected override void OnUnload()
        {
            if (_attachedInSeatControls.InStack)
                _attachedInSeatControls.Pop();
            if (_attachedControls.InStack)
                _attachedControls.Pop();
            base.OnUnload();
        }

        [Update(false)]
        private void CheckAttachedControls(long dt)
        {
            var controlling = MyAPIGateway.Session.ControlledObject?.Get<EquiEntityControllerComponent>()?.Controlled != null;
            if (controlling)
            {
                if (!_attachedControls.InStack)
                    _attachedControls.Push();
                if (!_attachedInSeatControls.InStack)
                    _attachedInSeatControls.Push();
            }
            else
            {
                if (_attachedInSeatControls.InStack)
                    _attachedInSeatControls.Pop();
                if (_attachedControls.InStack)
                    _attachedControls.Pop();
                ModifierShift = false;
            }
        }

        #endregion
    }
}