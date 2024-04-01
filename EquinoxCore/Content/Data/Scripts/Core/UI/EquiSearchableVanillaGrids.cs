using System;
using System.Xml.Serialization;
using Medieval.GameSystems;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.UI
{
    #region Signpost

    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiSignpostIconController))]
    public class EquiSignpostIconController : EquiIconGridControllerBase<MySignpostIconDataSource.SignpostIconData>
    {
        protected override bool Filter(in MySignpostIconDataSource.SignpostIconData item, string query) =>
            item.DisplayName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        protected override void ItemData(in MySignpostIconDataSource.SignpostIconData item, MyGrid.Item target)
        {
            target.Icons = item.Icons;
            target.Tooltip = string.IsNullOrEmpty(item.DisplayName) ? null : new MyTooltip(item.DisplayName);
            target.Enabled = true;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSignpostIconController : MyObjectBuilder_EquiIconGridControllerBase
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiSignpostIconControllerDefinition))]
    public class EquiSignpostIconControllerDefinition : EquiIconGridControllerBaseDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiSignpostIconControllerDefinition : MyObjectBuilder_EquiIconGridControllerBaseDefinition
    {
    }

    #endregion


    #region Bonus Pattern

    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiBonusPatternController))]
    public class EquiBonusPatternController : EquiIconGridControllerBase<MyBannerComponent.BonusPatternDefinition>
    {
        protected override bool Filter(in MyBannerComponent.BonusPatternDefinition item, string query) =>
            MyTexts.GetString(item.Description)?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        protected override void ItemData(in MyBannerComponent.BonusPatternDefinition item, MyGrid.Item target)
        {
            var tooltip = MyTexts.GetString(item.Description);
            target.Icons = new[] { item.Icon };
            target.Tooltip = string.IsNullOrEmpty(tooltip) ? null : new MyTooltip(tooltip);
            target.Enabled = item.IsUnlocked(MyAPIGateway.Session.LocalHumanPlayer?.IdentityId ?? 0);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBonusPatternController : MyObjectBuilder_EquiIconGridControllerBase
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiBonusPatternControllerDefinition))]
    public class EquiBonusPatternControllerDefinition : EquiIconGridControllerBaseDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiBonusPatternControllerDefinition : MyObjectBuilder_EquiIconGridControllerBaseDefinition
    {
    }

    #endregion
}