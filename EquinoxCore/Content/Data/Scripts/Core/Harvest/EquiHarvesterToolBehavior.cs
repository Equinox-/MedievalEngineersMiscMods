using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Inventory;
using Equinox76561198048419394.Core.Util;
using Medieval.Constants;
using Medieval.GameSystems;
using Sandbox.Definitions.Equipment;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Inventory;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.GUI.Crosshair;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Definitions.Equipment;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Harvest
{
    [MyHandItemBehavior(typeof(MyObjectBuilder_EquiHarvesterBehaviorDefinition))]
    public class EquiHarvesterBehavior : MyToolBehaviorBase
    {
        private EquiHarvesterBehaviorDefinition _definition;

        public override void Init(MyEntity holder, MyHandItem item, MyHandItemBehaviorDefinition definition)
        {
            base.Init(holder, item, definition);
            _definition = (EquiHarvesterBehaviorDefinition) definition;
        }

        protected override bool ValidateTarget()
        {
            var harvestable = Target.Entity?.Get<EquiHarvestableComponent>();
            EquiHarvestableComponentDefinition.Data tmp;
            return harvestable != null && harvestable.CanHarvest(Item.GetDefinition(), out tmp) &&
                   (!tmp.RequiresPermission || HasPermission(MyPermissionsConstants.Interaction));
        }

        protected override bool Start(MyHandItemActionEnum action)
        {
            switch (action)
            {
                case MyHandItemActionEnum.Primary:
                    return ValidateTarget();
                case MyHandItemActionEnum.None:
                case MyHandItemActionEnum.Secondary:
                case MyHandItemActionEnum.Tertiary:
                default:
                    return false;
            }
        }

        protected override void Hit()
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                return;
            var harvestable = Target.Entity?.Get<EquiHarvestableComponent>();
            if (harvestable == null)
                return;
            EquiHarvestableComponentDefinition.Data data;
            if (!harvestable.TryHarvest(Item.GetDefinition(), out data))
                return;

            base.UpdateDurability(-1);
            if (data.LootTable == null)
                return;

            var lootBag = MyEntities.CreateEntity(_definition.LootBag);
            var inv = lootBag.Get<MyInventoryBase>();
            if (inv == null)
                return;
            inv.GenerateLuckyContent(data.LootTable, new LuckyLoot.LootContext(_definition.LuckMultiplier, _definition.LuckAddition));

            if (_definition.OutputInventory != MyStringHash.NullOrEmpty)
            {
                var myInventory = Holder.Get<MyInventoryBase>(_definition.OutputInventory);
                if (myInventory != null)
                    for (var index = 0; index < inv.Items.Count; index++)
                    {
                        var item = inv.Items[index];
                        var fits = Math.Min(item.Amount, myInventory.ComputeAmountThatFits(item.DefinitionId));
                        if (fits <= 0 || !myInventory.AddItems(item.DefinitionId, fits)) continue;
                        item.Amount -= fits;
                        if (item.Amount > 0) continue;
                        index--;
                        inv.Remove(item);
                    }
            }

            if (inv.Items.Count > 0)
                MyEntities.Add(lootBag);
        }

        private bool HasPermission(MyStringId id)
        {
            var player = MyAPIGateway.Players.GetPlayerControllingEntity(Holder);
            if (player == null)
                return false;
            return MyAreaPermissionSystem.Static == null || MyAreaPermissionSystem.Static.HasPermission(player.IdentityId, Target.Position, id);
        }


        public override IEnumerable<string> GetHintTexts()
        {
            var harvestable = Target.Entity?.Get<EquiHarvestableComponent>();
            EquiHarvestableComponentDefinition.Data tmp;
            if (harvestable == null || !harvestable.CanHarvest(Item.GetDefinition(), out tmp))
                yield break;
            if (tmp.RequiresPermission && !HasPermission(MyPermissionsConstants.Interaction))
                yield return "No Permission";
            else if (!string.IsNullOrWhiteSpace(tmp.ActionHint))
                yield return tmp.ActionHint;
        }

        public override IEnumerable<MyCrosshairIconInfo> GetIconsStates()
        {
            var harvestable = Target.Entity?.Get<EquiHarvestableComponent>();
            EquiHarvestableComponentDefinition.Data tmp;
            if (harvestable == null || !harvestable.CanHarvest(Item.GetDefinition(), out tmp))
                yield break;
            if (!tmp.RequiresPermission || HasPermission(MyPermissionsConstants.Interaction))
                yield return new MyCrosshairIconInfo(tmp.ActionIcon);
        }

        protected override void OnTargetEntityChanged(MyDetectedEntityProperties myEntityProps)
        {
            base.OnTargetEntityChanged(myEntityProps);
            SetTarget();
        }
    }


    [MyDefinitionType(typeof(MyObjectBuilder_EquiHarvesterBehaviorDefinition))]
    public class EquiHarvesterBehaviorDefinition : MyToolBehaviorDefinition
    {
        public float LuckAddition { get; private set; }
        public float LuckMultiplier { get; private set; }
        public MyStringHash OutputInventory { get; private set; }
        public SerializableDefinitionId LootBag { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiHarvesterBehaviorDefinition) builder;

            OutputInventory = MyStringHash.GetOrCompute(ob.OutputInventory);
            LuckAddition = ob.LuckAddition ?? 0f;
            LuckMultiplier = ob.LuckMultiplier ?? 1f;
            LootBag = ob.LootBag ?? new SerializableDefinitionId {TypeIdString = "MyObjectBuilder_InventoryBagEntity", SubtypeId = "LootBag"};
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiHarvesterBehaviorDefinition : MyObjectBuilder_ToolBehaviorDefinition
    {
        [XmlElement]
        public string OutputInventory;

        [XmlElement]
        public float? LuckAddition;

        [XmlElement]
        public float? LuckMultiplier;

        [XmlElement]
        public SerializableDefinitionId? LootBag;
    }
}