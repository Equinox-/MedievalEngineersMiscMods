using System;
using System.Text;
using System.Xml.Serialization;
using Medieval.GUI.ContextMenu.Attributes;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public interface IEquiIconGridItem
    {
        string Name { get; }
        string[] UiIcons { get; }
    }

    public interface IEquiIconGridItemSearchable : IEquiIconGridItem
    {
        bool Test(string query);
    }

    public interface IEquiIconGridItemTooltip : IEquiIconGridItem
    {
        bool DynamicTooltip { get; }
        void BindTooltip(MyTooltip tooltip);
    }

    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiIconGridController))]
    public class EquiIconGridController : EquiIconGridControllerBase<IEquiIconGridItem>
    {
        private static readonly string[] FallbackIcons = { "Textures/GUI/Icons/buttons/Other.png" };

        protected override bool Filter(in IEquiIconGridItem item, string query) => item is IEquiIconGridItemSearchable searchable
            ? searchable.Test(query)
            : item.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        protected override void ItemData(in IEquiIconGridItem item, MyGrid.Item target)
        {
            target.Enabled = item != null;
            target.UserData = item;
            target.Icons = item?.UiIcons ?? FallbackIcons;
            target.Text = item?.UiIcons?.Length > 0 ? "" : item?.Name ?? "";
            if (item is IEquiIconGridItemTooltip withTooltip)
            {
                target.Tooltip = new MyTooltip();
                using (target.Tooltip.OpenBatch(true))
                {
                    withTooltip.BindTooltip(target.Tooltip);
                }
            }
            else if (!string.IsNullOrEmpty(item?.Name))
                target.Tooltip = new MyTooltip(item.Name);
        }

        protected override void RefreshTooltip(in IEquiIconGridItem item, MyGrid.Item target)
        {
            if (!(item is IEquiIconGridItemTooltip withTooltip) || !withTooltip.DynamicTooltip) return;
            if (target.Tooltip == null) target.Tooltip = new MyTooltip();
            using (target.Tooltip.OpenBatch(true))
            {
                withTooltip.BindTooltip(target.Tooltip);
            }
        }

        public static string NameFromId(MyStringHash id)
        {
            if (id == MyStringHash.NullOrEmpty)
                return "Default";
            var idStr = id.String;
            var sb = new StringBuilder(idStr.Length + 4);
            var space = false;
            foreach (var ch in idStr)
            {
                if ((char.IsUpper(ch) || char.IsDigit(ch)) && space)
                {
                    sb.Append(' ');
                    space = false;
                }

                if (char.IsLower(ch))
                    space = true;
                sb.Append(ch);
            }

            return sb.ToString();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiIconGridController : MyObjectBuilder_EquiIconGridControllerBase
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiIconGridControllerDefinition))]
    public class EquiIconGridControllerDefinition : EquiIconGridControllerBaseDefinition
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiIconGridControllerDefinition : MyObjectBuilder_EquiIconGridControllerBaseDefinition
    {
    }
}