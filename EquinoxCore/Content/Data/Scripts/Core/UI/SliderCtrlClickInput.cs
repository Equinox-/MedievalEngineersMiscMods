using System;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage;

namespace Equinox76561198048419394.Core.UI
{
    /// <summary>
    /// Allows ctrl-clicking on a slider to input an arbitrary value.
    /// </summary>
    public static class SliderCtrlClickInput
    {
        public static void Bind(MyGuiControlSliderBase slider) => slider.SliderClicked += _handler;

        private static readonly Func<MyGuiControlSliderBase, bool> _handler = slider =>
        {
            if (!MyAPIGateway.Input.IsAnyCtrlKeyDown() || !slider.Enabled)
                return false;
            var minVal = slider.Properties.RatioToValue(0);
            var maxVal = slider.Properties.RatioToValue(1);
            var dialog = new MyFloatInputDialog(
                MyTexts.GetString(MyCommonTexts.DialogAmount_SetValueCaption),
                minVal, maxVal, slider.Value);
            dialog.ResultCallback += v => slider.Value = v;
            MyGuiSandbox.AddScreen(dialog);
            return true;
        };
    }
}