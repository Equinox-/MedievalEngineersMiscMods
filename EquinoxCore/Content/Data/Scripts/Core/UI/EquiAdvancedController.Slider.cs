using System;
using Medieval.GUI.ContextMenu;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    internal sealed class SliderData : ControlHolder<MyObjectBuilder_EquiAdvancedControllerDefinition.Slider>
    {
        private readonly DataSourceValueAccessor<float> _dataSource;
        private readonly MyGuiControlSlider _slider;

        internal SliderData(MyContextMenuController ctl,
            EquiAdvancedControllerDefinition owner,
            SliderFactory factory) : base(ctl, owner, factory)
        {
            var def = factory.Def;
            _dataSource = new DataSourceValueAccessor<float>(ctl, def.DataId, def.DataIndex);
            ContextMenuStyles.SliderStyles(def.StyleNameId, out var sliderStyle, out var sliderValueStyle);
            var valueLabel = new MyGuiControlLabel();
            valueLabel.ApplyStyle(sliderValueStyle);
            _slider = new MyGuiControlSlider(
                width: owner.Width,
                labelDecimalPlaces: def.LabelDecimalPlaces,
                toolTip: MyTexts.GetString(def.TooltipId),
                minValue: _dataSource.Min ?? def.Min ?? 0,
                maxValue: _dataSource.Max ?? def.Max ?? 0)
            {
                Properties = CreateProperties(_dataSource)
            };

            _slider.ApplyStyle(sliderStyle);
            _slider.ValueChanged += _ =>
            {
                var value = _slider.Value;
                if (owner.AutoCommit)
                    _dataSource.SetValue(value);
                valueLabel.Text = FormatLabel(value, def.LabelDecimalPlaces, def.TextFormat);
            };
            _slider.SliderClicked += SliderClicked;
            valueLabel.Text = FormatLabel(_slider.Value, def.LabelDecimalPlaces, def.TextFormat);

            MakeVerticalRoot(_slider, valueLabel);
        }

        protected override void SyncToControlInternal()
        {
            var value = _dataSource.GetValue();
            Root.Enabled = false;
            if (!value.HasValue) return;
            var minVal = _dataSource.Min ?? Def.Min ?? float.PositiveInfinity;
            var maxVal = _dataSource.Max ?? Def.Max ?? float.NegativeInfinity;
            if (minVal >= maxVal) return;
            Root.Enabled = true;
            _slider.DefaultValue = _dataSource.Default ?? Def.Default ?? (minVal + maxVal) / 2;
            var newValue = value.Value;
            var tolerance = Math.Max(Math.Abs(minVal), Math.Abs(maxVal)) / 1000;
            if (Math.Abs(newValue - _slider.Value) > tolerance)
                _slider.Value = newValue;
        }

        protected override void SyncFromControlInternal()
        {
            _dataSource.SetValue(_slider.ValueNormalized);
        }

        private MyGuiSliderProperties CreateProperties(DataSourceValueAccessor<float> ds)
        {
            return new MyGuiSliderProperties
            {
                RatioToValue = ratio =>
                {
                    var value = RatioToValue(Min(), Max(), Exponent(), MathHelper.Clamp(ratio, 0, 1));
                    if (Def.IsInteger)
                        value = (float)Math.Round(value);
                    return value;
                },
                ValueToRatio = value => ValueToRatio(Min(), Max(), Exponent(), value),
                RatioFilter = ratio =>
                {
                    ratio = MathHelper.Clamp(ratio, 0, 1);
                    if (!Def.IsInteger)
                        return ratio;
                    var minVal = Min();
                    var maxVal = Max();
                    var exponentVal = Exponent();
                    var value = RatioToValue(minVal, maxVal, exponentVal, ratio);
                    value = (float)Math.Round(value);
                    return ValueToRatio(minVal, maxVal, exponentVal, value);
                },
                FormatLabel = _ => ""
            };
            float Min() => ds.Min ?? Def.Min ?? float.PositiveInfinity;
            float Max() => ds.Max ?? Def.Max ?? float.NegativeInfinity;
            float Exponent() => Def.Exponent ?? 1;

            float ValueToRatio(float min, float max, float exponent, float value)
            {
                var ratio = (value - min) / (max - min);
                ratio = (float)Math.Pow(ratio, 1 / exponent);
                if (float.IsNaN(ratio))
                    return 0;
                return MathHelper.Clamp(ratio, 0, 1);
            }

            float RatioToValue(float min, float max, float exponent, float ratio)
            {
                ratio = (float)Math.Pow(ratio, exponent);
                ratio = MathHelper.Clamp(ratio, 0, 1);
                if (float.IsNaN(ratio))
                    ratio = 0;
                return min + ratio * (max - min);
            }
        }

        private static string FormatLabel(float value, int decimalPlaces, string textFormat)
        {
            return string.Format(textFormat, value.ToString("N" + decimalPlaces));
        }

        private bool SliderClicked(MyGuiControlSliderBase _)
        {
            if (!MyAPIGateway.Input.IsAnyCtrlKeyDown() || !_slider.Enabled)
                return false;
            var minVal = _dataSource.Min ?? Def.Min ?? float.PositiveInfinity;
            var maxVal = _dataSource.Max ?? Def.Max ?? float.NegativeInfinity;
            var dialog = new MyFloatInputDialog(
                MyTexts.GetString(MyCommonTexts.DialogAmount_SetValueCaption),
                minVal, maxVal, _slider.Value);
            dialog.ResultCallback += v => _slider.Value = v;
            MyGuiSandbox.AddScreen(dialog);
            return true;
        }
    }

    internal sealed class SliderFactory : ControlFactory<MyObjectBuilder_EquiAdvancedControllerDefinition.Slider>
    {
        private readonly EquiAdvancedControllerDefinition _owner;

        public SliderFactory(EquiAdvancedControllerDefinition owner, MyObjectBuilder_EquiAdvancedControllerDefinition.Slider sliderDef) : base(sliderDef)
        {
            _owner = owner;
        }

        public override IControlHolder Create(MyContextMenuController ctl) => new SliderData(ctl, _owner, this);
    }
}