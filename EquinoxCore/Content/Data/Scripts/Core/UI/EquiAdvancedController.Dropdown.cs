using System.Collections.Generic;
using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Library.Collections;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class DropdownData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown>
    {
        private readonly DataSourceAccessor<ContextMenuDropdownDataSource> _dataSource;
        private readonly MyGuiControlCombobox _dropdown;
        private int _lastVersion;

        public DropdownData(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown def) : base(ctl, owner, def)
        {
            _dataSource = new DataSourceAccessor<ContextMenuDropdownDataSource>(ctl, def.DataId);
            _dropdown = new MyGuiControlCombobox(toolTip: MyTexts.GetString(def.TooltipId));
            _dropdown.SetSize(new Vector2(owner.Width, _dropdown.Size.Y));
            _dropdown.ApplyStyle(ContextMenuStyles.ComboboxStyle(def.StyleNameId));
            _dropdown.ItemSelected += (_1, _2) => SyncFromControl();
            MakeVerticalRoot(_dropdown);
        }

        protected override void SyncToControlInternal()
        {
            var impl = _dataSource.DataSource;
            if (impl == null || impl.Count == 0)
            {
                Root.Enabled = false;
                return;
            }

            Root.Enabled = true;
            var version = impl.ItemsVersion;
            if (version != _lastVersion)
            {
                using (PoolManager.Get(out List<ContextMenuDropdownDataSource.DropdownItem> items))
                {
                    impl.GetItems(items);
                    _dropdown.ClearItems();
                    for (var i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        _dropdown.AddItem(key: i, value: item.Text, toolTip: item.Tooltip);
                    }
                }

                _lastVersion = version;
                _dropdown.SelectItemByIndex(impl.Selected);
            } else if (_dropdown.GetSelectedIndex() != impl.Selected)
                _dropdown.SelectItemByIndex(impl.Selected);
        }

        protected override void SyncFromControlInternal()
        {
            var impl = _dataSource.DataSource;
            if (impl != null)
            {
                impl.Selected = MathHelper.Clamp(_dropdown.GetSelectedIndex(), 0, impl.Count - 1);
            }
        }
    }

    internal sealed class DropdownFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown _checkDef;

        public DropdownFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown checkDef)
        {
            _owner = owner;
            _checkDef = checkDef;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new DropdownData(ctl, _owner, _checkDef);
    }
}