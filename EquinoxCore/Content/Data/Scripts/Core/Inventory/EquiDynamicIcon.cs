using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util.Memory;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Inventory
{
    [MyDefinitionType(typeof(MyObjectBuilder_EquiDynamicIconDefinition))]
    public class EquiDynamicIconDefinition : MyDefinitionBase
    {
        private EqReadOnlySpan<DynamicIcon> _dynamicIcons;
        private EqReadOnlySpan<DynamicLabel> _dynamicLabels;
        private ulong _currencyMultiplier;

        protected override void Init(MyObjectBuilder_DefinitionBase def)
        {
            base.Init(def);
            var ob = (MyObjectBuilder_EquiDynamicIconDefinition)def;
            _currencyMultiplier = 1;
            _dynamicIcons = DynamicIcon.Of(ob.DynamicIcons);
            _dynamicLabels = DynamicLabel.Of(ob.DynamicLabels);
        }

        internal void OverrideForCurrency(ulong preMultiplier, EqReadOnlySpan<DynamicLabel> labels)
        {
            _currencyMultiplier = preMultiplier;
            _dynamicLabels = labels;
        }

        /// <summary>
        /// Gets the dynamic icon definition for the given inventory item.
        /// </summary>
        /// <param name="item">inventory item</param>
        /// <param name="icons">dynamic icons</param>
        /// <returns>true if the icons were populated dynamically</returns>
        public bool TryGetDynamicIcons(MyInventoryItem item, out string[] icons)
        {
            ref readonly var iconDef = ref DynamicIcon.Access(in _dynamicIcons, item.Amount, (item as MyDurableItem)?.Durability ?? 0, out var okay);
            icons = okay ? iconDef.Icons : null;
            return okay;
        }

        /// <summary>
        /// Gets the dynamic label for the given number of items.
        /// </summary>
        /// <param name="item">inventory item</param>
        /// <param name="label">output label text</param>
        /// <returns>true if the label was populated dynamically</returns>
        public bool TryGetDynamicLabel(MyInventoryItem item, out string label)
        {
            var labelAmount = (ulong)item.Amount * _currencyMultiplier;
            ref readonly var labelDef = ref DynamicLabel.Access(in _dynamicLabels, labelAmount, out var okay);
            label = okay ? labelDef.Format(labelAmount) : null;
            return okay;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDynamicIconDefinition : MyObjectBuilder_DefinitionBase
    {
        [XmlElement("DynamicLabel")]
        public List<MyObjectBuilder_DynamicLabel> DynamicLabels;

        [XmlElement("DynamicIcon")]
        public List<MyObjectBuilder_DynamicIcon> DynamicIcons;
    }
}