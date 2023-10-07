using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class CheckboxData : IControlHolder
    {
        private bool _commitPermitted;
        public DataSourceValueAccessor<bool> DataSource;
        public MyGuiControlCheckbox Checkbox;
        public MyGuiControlBase Root { get; set; }

        public void SyncToControl()
        {
            _commitPermitted = false;
            var value = DataSource.GetValue();
            Checkbox.IsChecked = value ?? false;
            Root.Enabled = value.HasValue;
            _commitPermitted = true;
        }

        public void SyncFromControl()
        {
            if (!_commitPermitted || !Root.Enabled) return;
            DataSource.SetValue(Checkbox.IsChecked);
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

        public override IControlHolder Create(MyContextMenuController ctl)
        {
            var ds = new DataSourceValueAccessor<bool>(ctl, _checkDef.DataId, _checkDef.DataIndex);
            var label = new MyGuiControlLabel(text: MyTexts.GetString(_checkDef.TextId));
            label.SetToolTip(_checkDef.Tooltip);
            label.ApplyStyle(ContextMenuStyles.LabelStyle());
            var checkbox = new MyGuiControlCheckbox(toolTip: MyTexts.GetString(_checkDef.TooltipId));
            checkbox.ApplyStyle(ContextMenuStyles.CheckboxStyle(_checkDef.StyleNameId));
            checkbox.OnCheckedChanged += _ =>
            {
                if (_owner.AutoCommit)
                    ds.SetValue(checkbox.IsChecked);
            };
            var labeledCheckbox = new MyGuiControlParent(size: checkbox.Size + new Vector2(0.0f, label.Size.Y));
#pragma warning disable CS0618 // Type or member is obsolete
            var layout = new MyLayoutVertical(labeledCheckbox, ctl.MarginPx.X);
#pragma warning restore CS0618 // Type or member is obsolete
            layout.Add(label, checkbox);
            return new CheckboxData
            {
                DataSource = ds,
                Root = labeledCheckbox,
                Checkbox = checkbox
            };
        }
    }
}