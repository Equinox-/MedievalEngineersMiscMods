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
            CheckboxFactory factory) : base(ctl, owner, factory)
        {
            var def = factory.Def;
            _dataSource = new DataSourceValueAccessor<bool>(ctl, def.DataId, def.DataIndex);
            _checkbox = new MyGuiControlCheckbox(toolTip: MyTexts.GetString(def.TooltipId));
            _checkbox.ApplyStyle(ContextMenuStyles.CheckboxStyle(def.StyleNameId));
            _checkbox.OnCheckedChanged += _ => SyncFromControl();
            _checkbox.LayoutStyle = MyGuiControlLayoutStyle.Fixed;
            MakeHorizontalRoot(_checkbox);
            SyncToControlInternal();
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

    internal sealed class CheckboxFactory : ControlFactory<MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox>
    {
        private readonly EquiAdvancedControllerDefinition _owner;

        public CheckboxFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox checkDef) : base(checkDef)
        {
            _owner = owner;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new CheckboxData(ctl, _owner, this);
    }
}