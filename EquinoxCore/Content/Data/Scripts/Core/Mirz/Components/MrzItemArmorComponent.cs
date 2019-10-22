using Equinox76561198048419394.Core.Mirz.Definitions;
using Equinox76561198048419394.Core.Mirz.Extensions;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.GameSystems.DamageSystem;
using Sandbox.Game.Inventory;
using VRage.Components;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Mirz.Components
{
    [MyObjectBuilderDefinition]
    public class MyObjectBuilder_MrzItemArmorComponent : MyObjectBuilder_EntityComponent
    {
    }

    /// <summary>
    /// Component useful for equipment item entity. Reduces damage taken.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_MrzItemArmorComponent))]
    [MyDefinitionRequired(typeof(MrzItemArmorComponentDefinition))]
    public class MrzItemArmorComponent : MyEntityComponent, IMyDamageModifier
    {
        #region Protected

        protected MrzItemArmorComponentDefinition Definition;

        protected MyCharacterDamageComponent DamageComp;
        protected MyInventoryItemComponent ItemComp;
        protected MyEquipmentItem Equipment;
        protected MyEntity Holder;

        #endregion

        #region Properties

        public override string DebugName => "Item Armor";

        #endregion

        #region Init

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);

            Definition = definition as MrzItemArmorComponentDefinition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();

            AddScheduledCallback(RegisterModifier, 16);
        }

        public override void OnBeforeRemovedFromContainer()
        {
            base.OnBeforeRemovedFromContainer();

            UnregisterModifier();
        }

        public override void OnRemovedFromScene()
        {
            base.OnRemovedFromScene();

            UnregisterModifier();
        }

        #endregion

        #region Helper Methods

        [Update(false)]
        private void RegisterModifier(long delta)
        {
            if (Container == null)
                return;

            ItemComp = this.Get<MyInventoryItemComponent>();
            if (ItemComp?.ItemContainer == null)
                return;

            Holder = ItemComp.ItemContainer.Entity;
            Equipment = ItemComp.Item as MyEquipmentItem;

            if (Holder == null)
                return;

            DamageComp = Holder.Get<MyCharacterDamageComponent>();
            if (DamageComp == null)
                return;

            DamageComp.RegisterDamageModifier(this);
            DamageComp.DamageTaken += OnDamageTaken;
        }

        private void UnregisterModifier()
        {
            if (DamageComp != null)
            {
                DamageComp.UnregisterDamageModifier(this);
                DamageComp.DamageTaken -= OnDamageTaken;
            }

            Holder = null;
            DamageComp = null;
            ItemComp = null;
        }

        // we need to call this once we know the damage has been applied, not just for random checks
        private void UpdateDurability()
        {
            if (Equipment == null)
                return;

            Equipment.Durability -= 1;

            if (Equipment.Durability > 0) return;

            if (MrzUtils.IsServer && ItemComp != null)
                ItemComp.RemoveItem();
            else
                Equipment.Durability = 0;
        }

        #endregion

        #region Bindings

        private void OnDamageTaken(MyDamageInformation myDamageInformation)
        {
            UpdateDurability();
        }

        #endregion

        #region IMyDamageModifier

        public float GetPreModifier(float damage, MyStringHash damageSource, MyHitInfo? hitInfo, MyEntity attacker = null)
        {
            return 0;
        }

        public float GetMultiplier(float damage, MyStringHash damageSource, MyHitInfo? hitInfo, MyEntity attacker = null)
        {
            if (Definition == null)
                return 1;

            float reduction;
            return Definition.DamageReduction.TryGetValue(damageSource, out reduction) ? 1 - reduction : 1;
        }

        public float GetPostModifier(float damage, MyStringHash damageSource, MyHitInfo? hitInfo, MyEntity attacker = null)
        {
            return 0;
        }

        #endregion
    }
}
