using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Components;
using VRage.Game.Input;
using VRage.Library.Memory;
using VRage.Logging;
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

        internal void Link(long controller, ControlledId key)
        {
            long existingController;
            if (_controlledToController.TryGetValue(key, out existingController))
            {
                if (existingController == controller)
                    return;
                this.GetLogger().Warning($"Controllable slot {key} is already controlled by {existingController}");
                Unlink(existingController);
            }

            ControlledId existingControlled;
            if (_controllerToControlled.TryGetValue(controller, out existingControlled))
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
            ControlledId existingControlled;
            if (_controllerToControlled.TryGetValue(controller, out existingControlled))
                _controlledToController.Remove(existingControlled);
            _controllerToControlled.Remove(controller);
        }

        public delegate void DelControlledChanged(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlledOld,
            EquiPlayerAttachmentComponent.Slot controlledNew);

        public event DelControlledChanged ControlledChanged;

        internal void RaiseControlledChange(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlledOld,
            EquiPlayerAttachmentComponent.Slot controlledNew)
        {
            ControlledChanged?.Invoke(controller, controlledOld, controlledNew);

            if (controller.Entity == MyAPIGateway.Session.ControlledObject)
                CheckAttachedControls();
        }

        private bool RequestControlInternal(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlled)
        {
            var desiredControlling = new ControlledId(controlled);
            if (_controlledToController.ContainsKey(desiredControlling))
                return false;

            ControlledId currentlyControlling;
            if (_controllerToControlled.TryGetValue(controller.Entity.EntityId, out currentlyControlling))
            {
                if (currentlyControlling.Equals(controlled))
                    return true;
                if (!ReleaseControlInternal(controller))
                    return false;
            }

            return controller.RequestControlInternal(controlled);
        }

        private bool ReleaseControlInternal(EquiEntityControllerComponent controller)
        {
            ControlledId currentlyControlling;
            if (!_controllerToControlled.TryGetValue(controller.Entity.EntityId, out currentlyControlling))
                return false;

            return controller.ReleaseControlInternal();
        }

        public void RequestControl(EquiEntityControllerComponent controller, EquiPlayerAttachmentComponent.Slot controlled)
        {
            if (controller?.Entity == null || !controller.Entity.InScene)
                return;
            if (controlled?.Controllable?.Entity == null || !controlled.Controllable.Entity.InScene)
                return;
            if (!MyMultiplayerModApi.Static.IsServer && controller.Entity != MyAPIGateway.Session.ControlledObject)
                return;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseStaticEvent(x => DelRequestControlServer, controller.Entity.EntityId, controlled.Controllable.Entity.EntityId,
                    controlled.Definition.Name);
            else
                RequestControlServer(controller.Entity.EntityId, controlled.Controllable.Entity.EntityId, controlled.Definition.Name);
        }

        public void ReleaseControl(EquiEntityControllerComponent controller)
        {
            if (controller?.Entity == null || !controller.Entity.InScene)
                return;
            if (!MyMultiplayerModApi.Static.IsServer && controller.Entity != MyAPIGateway.Session.ControlledObject)
                return;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.RaiseStaticEvent(x => DelReleaseControlServer, controller.Entity.EntityId);
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
        private static void RequestControlServer(long controller, long entity, string slot)
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

            MySession.Static?.Components.Get<EquiEntityControllerTracker>()?.RequestControlInternal(controllerComponent, controllableSlot);
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

        private static readonly Action<long, long, string> DelRequestControlServer = RequestControlServer;
        private static readonly Action<long> DelReleaseControlServer = ReleaseControlServer;

        #endregion


        #region Controls

        private readonly MyInputContext _attachedControls = new MyInputContext("Attachment controls");

        public EquiEntityControllerTracker()
        {
            _attachedControls.UnregisterAllActions();
            _attachedControls.RegisterAction(MyStringHash.GetOrCompute("LeaveAttached"),
                () => MyAPIGateway.Session.ControlledObject?.Components.Get<EquiEntityControllerComponent>()?.ReleaseControl());
        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            CheckAttachedControls();
        }

        protected override void OnUnload()
        {
            if (_attachedControls.InStack)
                _attachedControls.Pop();
            base.OnUnload();
        }

        private void CheckAttachedControls()
        {
            var controlling = MyAPIGateway.Session.ControlledObject?.Get<EquiEntityControllerComponent>()?.Controlled != null;
            if (controlling && !_attachedControls.InStack)
                _attachedControls.Push();
            else if (!controlling && _attachedControls.InStack)
                _attachedControls.Pop();
        }

        #endregion
    }
}