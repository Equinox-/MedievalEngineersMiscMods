using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Medieval.Entities.UseObject;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzInventoryInteractionComponent : MyObjectBuilder_EntityComponent
    {

    }

    [MyComponent(typeof(MyObjectBuilder_MrzInventoryInteractionComponent))]
    [MyDependency(typeof(MyInventoryBase))]
    [MyDependency(typeof(MyUseObjectsComponentBase))]
    [MyDefinitionRequired(typeof(MrzInventoryInteractionComponentDefinition))]
    public class MrzInventoryInteractionComponent : MyEntityComponent, IMyGenericUseObjectInterface, IMyEventProxy
    {
        private MyInventoryBase _inventory;
        private MyUseObjectGeneric _useObj;

        private MrzInventoryInteractionComponentDefinition _definition;
        private MyStringId _interactionMessage;

        private static readonly IMyHudNotification _actionDebugNotification = MrzUtils.CreateNotificationDebug("InvInteraction::GetActionInfo", 200);
        private IMyHudNotification _successNotification;
        private IMyHudNotification _failureNotification;

        public UseActionEnum PrimaryAction => UseActionEnum.Manipulate;
        public UseActionEnum SecondaryAction => UseActionEnum.None;
        public UseActionEnum SupportedActions => PrimaryAction;

        public bool ContinuousUsage => false;
        
        public bool AppliesTo(string dummyName)
        {
            return dummyName == _definition.UseObject;
        }

        #region Init

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            _definition = definition as MrzInventoryInteractionComponentDefinition;
            if (_definition == null)
                return;

            _interactionMessage = _definition.InteractionMessage;

            MrzUtils.ShowNotificationDebug($"InvInteraction::IsServer = {MrzUtils.IsServer}", 500);

            _successNotification = MrzUtils.CreateNotification(MyTexts.GetString(_definition.SuccessNotification));
            _failureNotification = MrzUtils.CreateNotification(MyTexts.GetString(_definition.FailureNotification), MrzUtils.NotificationType.Error);
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            if (_definition == null)
                return;

            _inventory = this.Get<MyInventoryBase>(_definition.Inventory) ?? this.Get<MyInventoryBase>();
            MrzUtils.ShowNotificationDebug($"InvInteraction::Inventory {_inventory}");

            var useObjComp = this.Get<MyUseObjectsComponentBase>();
            if (useObjComp == null)
                return;

            var useObject = useObjComp.GetInteractiveObject("Generic");
            _useObj = useObject as MyUseObjectGeneric;
            if (_useObj == null)
                return;

            _useObj.Interface = this;
        }

        #endregion

        #region Use

        public void Use(string dummyName, UseActionEnum actionEnum, MyEntity user)
        {
            MrzUtils.ShowNotificationDebug("InvInteraction::Use", 200);
            if (!actionEnum.HasFlags(actionEnum))
                return;

            if (MrzUtils.IsServer)
            {
                MrzUtils.ShowNotificationDebug("InvInteraction::Server-side Use", 200);
                RequestInventoryInteraction(user.EntityId, !MyAPIGateway.Input.IsAnyShiftKeyDown());
            }
            else
            {
                MrzUtils.ShowNotificationDebug("InvInteraction::Client-side Use", 200);
                MyAPIGateway.Multiplayer.RaiseEvent(this, x => RequestInventoryInteraction, user.EntityId, !MyAPIGateway.Input.IsAnyShiftKeyDown());
            }
        }

        public MyActionDescription GetActionInfo(string dummyName, UseActionEnum actionEnum)
        {
            _actionDebugNotification.ShowDebug();

            return new MyActionDescription()
            {
                Text = _interactionMessage,
                FormatParams = new object[] { "F", Entity.DisplayNameText },
                IsTextControlHint = false,
            };
        }

        #endregion

        #region Multiplayer

        [Event, Reliable, Server]
        private void RequestInventoryInteraction(long entityId, bool insertion = true)
        {
            if (insertion && _definition.InteractionMode == InventoryInteractionMode.Output)
                return;

            if (!insertion && _definition.InteractionMode == InventoryInteractionMode.Input)
                return;

            MyEntity user;
            MyEntities.TryGetEntityById(entityId, out user);

            var userInv = user?.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
            if (userInv == null || _inventory == null)
                return;

            var from = insertion ? userInv : _inventory;
            var to = insertion ? _inventory : userInv;

            if (_definition.StartSearchFrom == InventorySearchStart.Front)
            {
                for (var i = 0; i < from.Items.Count; i++)
                {
                    var item = from.Items.ItemAt(i);
                    if (to.CanAddItems(item.DefinitionId, item.Amount) &&
                        to.TransferItemsFrom(from, item, item.Amount))
                    {
                        MrzUtils.ShowNotificationDebug($"InvInteraction::Transferred {item} from {from} to {to}.");
                        MyAPIGateway.Multiplayer.RaiseEvent(this, x => NotifyInventoryInteractionSuccess, entityId, (SerializableDefinitionId)item.DefinitionId, item.Amount);
                        return;
                    }
                }
            }
            else
            {
                for (var i = from.Items.Count - 1; i >= 0; i--)
                {
                    var item = from.Items.ItemAt(i);
                    if (to.CanAddItems(item.DefinitionId, item.Amount) &&
                        to.TransferItemsFrom(from, item, item.Amount))
                    {
                        MrzUtils.ShowNotificationDebug($"InvInteraction::Transferred {item} from {from} to {to}.");
                        MyAPIGateway.Multiplayer.RaiseEvent(this, x => NotifyInventoryInteractionSuccess, entityId, (SerializableDefinitionId)item.DefinitionId, item.Amount);
                        return;
                    }
                }
            }

            MyAPIGateway.Multiplayer.RaiseEvent(this, x => NotifyInventoryInteractionFailure, entityId);
        }

        [Event, Reliable, Client]
        private void NotifyInventoryInteractionSuccess(long entityId, SerializableDefinitionId id, int amount)
        {
            if (MySession.Static.PlayerEntity == null || MySession.Static.PlayerEntity.EntityId != entityId)
                return;

            var text = MyTexts.GetString(_definition.SuccessNotification);
            var itemDefinition = MyDefinitionManager.Get<MyInventoryItemDefinition>(id);

            _successNotification.Text = string.Format(text, itemDefinition?.DisplayNameText ?? id.ToString(), amount);
            _successNotification?.Show();
        }

        [Event, Reliable, Client]
        private void NotifyInventoryInteractionFailure(long entityId)
        {
            if (MySession.Static.PlayerEntity != null && MySession.Static.PlayerEntity.EntityId == entityId)
                _failureNotification?.Show();
        }

        #endregion
    }
}
