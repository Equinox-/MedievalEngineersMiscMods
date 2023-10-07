using System;
using System.Collections.Generic;
using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class DropdownData : IControlHolder
    {
        private bool _commitPermitted;
        public DataSourceAccessor<ContextMenuDropdownDataSource> DataSource;
        public MyGuiControlCombobox Dropdown;
        public MyGuiControlBase Root { get; set; }

        public void SyncToControl()
        {
            _commitPermitted = false;
            var impl = DataSource.DataSource;
            if (impl == null || impl.Count == 0)
            {
                Root.Enabled = false;
                return;
            }

            Root.Enabled = true;
            using (PoolManager.Get(out List<ContextMenuDropdownDataSource.DropdownItem> items))
            {
                impl.GetItems(items);
                Dropdown.ClearItems();
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    Dropdown.AddItem(key: i, value: item.Text, toolTip: item.Tooltip);
                }
            }
            Dropdown.SelectItemByIndex(impl.Selected);
            _commitPermitted = true;
        }

        public void SyncFromControl()
        {
            if (!_commitPermitted || !Root.Enabled) return;
            DropdownFactory.SyncFromControlInternal(Dropdown, DataSource);
        }
    }

    internal sealed class DropdownFactory : ControlFactory
    {
        internal static void SyncFromControlInternal(MyGuiControlCombobox combobox, DataSourceAccessor<ContextMenuDropdownDataSource> ds)
        {
            var impl = ds.DataSource;
            if (impl != null)
            {
                impl.Selected = MathHelper.Clamp(combobox.GetSelectedIndex(), 0, impl.Count - 1);
            }
        }
        
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown _checkDef;

        public DropdownFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown checkDef)
        {
            _owner = owner;
            _checkDef = checkDef;
        }

        public override IControlHolder Create(MyContextMenuController ctl)
        {
            var ds = new DataSourceAccessor<ContextMenuDropdownDataSource>(ctl, _checkDef.DataId);
            var label = new MyGuiControlLabel(text: MyTexts.GetString(_checkDef.TextId));
            label.SetToolTip(_checkDef.Tooltip);
            label.ApplyStyle(ContextMenuStyles.LabelStyle());
            var combobox = new MyGuiControlCombobox(toolTip: MyTexts.GetString(_checkDef.TooltipId));
            combobox.SetSize(new Vector2(_owner.Width, combobox.Size.Y));
            combobox.ApplyStyle(ContextMenuStyles.ComboboxStyle(_checkDef.StyleNameId));
            combobox.ItemSelected += (comboboxCaptured, _) => SyncFromControlInternal(comboboxCaptured, ds);
            var labeledDropdown = new MyGuiControlParent(size: combobox.Size + new Vector2(0.0f, label.Size.Y));
#pragma warning disable CS0618 // Type or member is obsolete
            var layout = new MyLayoutVertical(labeledDropdown, ctl.MarginPx.X);
#pragma warning restore CS0618 // Type or member is obsolete
            layout.Add(label, MyAlignH.Left);
            layout.Add(combobox, MyAlignH.Center);
            return new DropdownData
            {
                DataSource = ds,
                Root = labeledDropdown,
                Dropdown = combobox
            };
        }
    }
}