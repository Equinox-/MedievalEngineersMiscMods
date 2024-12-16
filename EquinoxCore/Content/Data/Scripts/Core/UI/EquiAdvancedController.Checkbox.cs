using System;
using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class CheckboxData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox>
    {
        private readonly DataSourceValueAccessor<bool> _dataSource;
        private readonly MyGuiControlCheckbox _checkbox;

        internal CheckboxData(MyContextMenuController ctl, EquiAdvancedControllerDefinition owner,
            MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox def) : base(ctl, owner, def)
        {
            _dataSource = new DataSourceValueAccessor<bool>(ctl, def.DataId, def.DataIndex);
            _checkbox = new MyGuiControlCheckbox(toolTip: MyTexts.GetString(def.TooltipId));
            _checkbox.ApplyStyle(ContextMenuStyles.CheckboxStyle(def.StyleNameId));
            _checkbox.OnCheckedChanged += _ =>
            {
                if (Owner.AutoCommit)
                    _dataSource.SetValue(_checkbox.IsChecked);
            };
            _checkbox.LayoutStyle = MyGuiControlLayoutStyle.Fixed;
            MakeHorizontalRoot(_checkbox);
        }

        protected override void SyncToControlInternal()
        {
            var value = _dataSource.GetValue();
            _checkbox.IsChecked = value ?? false;
            Root.Enabled = value.HasValue;
        }

        protected override void SyncFromControlInternal()
        {
            _dataSource.SetValue(_checkbox.IsChecked);
        }
    }

    internal sealed class CheckboxFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox _checkDef;

        public CheckboxFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox checkDef)
        {
            _owner = owner;
            _checkDef = checkDef;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new CheckboxData(ctl, _owner, _checkDef);
    }
}