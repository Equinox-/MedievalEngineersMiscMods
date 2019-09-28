using System;
using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Medieval.Constants;
using Sandbox.Game.EntityComponents.Character;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Session;

namespace Equinox76561198048419394.Core.Mirz.Behaviors
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_MrzAddItemsBehaviorDefinition))]
    public class MrzAddItemsBehavior : MyToolBehaviorBase
    {
        #region Static

        public static IMyHudNotification InventoryFullNotification = MrzUtils.CreateNotification("Inventory full.", MrzUtils.NotificationType.Error);

        #endregion

        #region Private

        private MyInventoryBase _holdersInventory;

        #endregion

        #region Properties

        protected new MrzAddItemsBehaviorDefinition Definition => base.Definition as MrzAddItemsBehaviorDefinition;

        #endregion
        
        #region Actions

        public override void Activate()
        {
            base.Activate();
            
            _holdersInventory = Holder.Components.Get<MyInventoryBase>(MyCharacterConstants.MainInventory);
        }

        public override void Deactivate()
        {
            base.Deactivate();

            _holdersInventory = null;
        }

        protected override bool ValidateTarget()
        {
            return true;
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            MrzUtils.ShowNotificationDebug($"AddItems::{_holdersInventory}");
            if (action != MyHandItemActionEnum.Primary || _holdersInventory == null)
                return false;

            return true;
        }

        protected override void Hit()
        {
            try
            {
                MrzUtils.ShowNotificationDebug("AddItems::AddItemsFuzzyOrLoot");
                if (_holdersInventory != null && _holdersInventory.AddItemsFuzzyOrLoot(Definition.IdToAdd, Definition.AmountToAdd))
                    UpdateDurability(-1);
                else if (Holder == MySession.Static.PlayerEntity)
                    InventoryFullNotification.Show();
            }
            catch (Exception e)
            {
                MrzUtils.ShowNotificationDebug(e.Message);
            }
        }

        #endregion
    }
}
