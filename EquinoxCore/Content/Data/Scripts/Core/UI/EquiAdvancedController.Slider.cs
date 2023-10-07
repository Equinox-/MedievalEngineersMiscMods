using System;
using Medieval.GUI.ContextMenu;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class SliderData : IControlHolder
    {
        public MyObjectBuilder_EquiAdvancedControllerDefinition.Slider SliderDef;
        private bool _commitPermitted;
        public DataSourceValueAccessor<float> DataSource;
        public MyGuiControlSlider Slider;
        public MyGuiControlBase Root { get; set; }

        public void SyncToControl()
        {
            _commitPermitted = false;
            var value = DataSource.GetValue();
            Root.Enabled = false;
            if (value.HasValue)
            {
                var minVal = DataSource.Min ?? SliderDef.Min ?? float.PositiveInfinity;
                var maxVal = DataSource.Max ?? SliderDef.Max ?? float.NegativeInfinity;
                if (minVal < maxVal)
                {
                    Root.Enabled = true;
                    Slider.DefaultValue = SliderFactory.ValueToRatio(minVal, maxVal, SliderDef.Exponent ?? 1,
                        DataSource.Default ?? SliderDef.Default ?? ((minVal + maxVal) / 2));
                    Slider.Value = SliderFactory.ValueToRatio(minVal, maxVal, SliderDef.Exponent ?? 1, value.Value);
                }
            }
            _commitPermitted = true;
        }

        public void SyncFromControl()
        {
            if (!_commitPermitted || !Root.Enabled) return;
            DataSource.SetValue(SliderFactory.ReadValue(SliderDef, Slider, DataSource));
        }
    }
    
    internal sealed class SliderFactory : ControlFactory
    {
        private readonly EquiAdvancedControllerDefinition _owner;
        private readonly MyObjectBuilder_EquiAdvancedControllerDefinition.Slider _sliderDef;

        internal static float ValueToRatio(float min, float max, float exponent, float value)
        {
            var ratio = (value - min) / (max - min);
            ratio = (float)Math.Pow(ratio, 1 / exponent);
            if (float.IsNaN(ratio))
                return 0;
            return MathHelper.Clamp(ratio, 0, 1);
        }

        private static float RatioToValue(float min, float max, float exponent, float ratio)
        {
            ratio = (float)Math.Pow(ratio, exponent);
            ratio = MathHelper.Clamp(ratio, 0, 1);
            if (float.IsNaN(ratio))
                ratio = 0;
            return min + ratio * (max - min);
        }

        internal static float ReadValue(MyObjectBuilder_EquiAdvancedControllerDefinition.Slider def, MyGuiControlSlider slider,
            DataSourceValueAccessor<float> dataSource)
        {
            var minVal = dataSource.Min ?? def.Min ?? float.PositiveInfinity;
            var maxVal = dataSource.Max ?? def.Max ?? float.NegativeInfinity;
            return RatioToValue(minVal, maxVal, def.Exponent ?? 1, slider.Value);
        }

        private static string FormatLabel(float value, int decimalPlaces, string textFormat)
        {
            return string.Format(textFormat, value.ToString("N" + decimalPlaces));
        }

        public SliderFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Slider sliderDef)
        {
            _owner = owner;
            _sliderDef = sliderDef;
        }

        public override IControlHolder Create(MyContextMenuController ctl)
        {
            var ds = new DataSourceValueAccessor<float>(ctl, _sliderDef.DataId, _sliderDef.DataIndex);
            ContextMenuStyles.SliderStyles(_sliderDef.StyleNameId, out var sliderStyle, out var sliderValueStyle);
            var label = new MyGuiControlLabel(text: MyTexts.GetString(_sliderDef.TextId));
            label.SetToolTip(_sliderDef.Tooltip);
            label.ApplyStyle(ContextMenuStyles.LabelStyle());
            var valueLabel = new MyGuiControlLabel();
            valueLabel.ApplyStyle(sliderValueStyle);
            var slider = new MyGuiControlSlider(
                width: _owner.Width,
                labelDecimalPlaces: _sliderDef.LabelDecimalPlaces,
                toolTip: MyTexts.GetString(_sliderDef.TooltipId),
                intValue: _sliderDef.IsInteger,
                minValue: 0,
                maxValue: 1);
            slider.ApplyStyle(sliderStyle);
            slider.ValueChanged += _ =>
            {
                var value = ReadValue(_sliderDef, slider, ds);
                if (_owner.AutoCommit)
                    ds.SetValue(value);
                valueLabel.Text = FormatLabel(value, _sliderDef.LabelDecimalPlaces, _sliderDef.TextFormat);
            };
            slider.SliderClicked += SliderClicked;
            var labeledSlider = new MyGuiControlParent(size: slider.Size + new Vector2(0.0f, label.Size.Y));
#pragma warning disable CS0618 // Type or member is obsolete
            var layout = new MyLayoutVertical(labeledSlider, ctl.MarginPx.X);
#pragma warning restore CS0618 // Type or member is obsolete
            layout.Add(label, valueLabel);
            layout.Add(slider, MyAlignH.Center);
            return new SliderData
            {
                SliderDef = _sliderDef,
                DataSource = ds,
                Root = labeledSlider,
                Slider = slider
            };

            bool SliderClicked(MyGuiControlSliderBase _)
            {
                if (!MyAPIGateway.Input.IsAnyCtrlKeyDown() || !slider.Enabled)
                    return false;
                var minVal = ds.Min ?? _sliderDef.Min ?? float.PositiveInfinity;
                var maxVal = ds.Max ?? _sliderDef.Max ?? float.NegativeInfinity;
                var exponent = _sliderDef.Exponent ?? 1;
                var dialog = new MyFloatInputDialog(
                    MyTexts.GetString(MyCommonTexts.DialogAmount_SetValueCaption),
                    minVal, maxVal, RatioToValue(minVal, maxVal, exponent, slider.Value));
                dialog.ResultCallback += v => slider.Value = ValueToRatio(minVal, maxVal, exponent, v);
                MyGuiSandbox.AddScreen(dialog);
                return true;
            }
        }
    }
}