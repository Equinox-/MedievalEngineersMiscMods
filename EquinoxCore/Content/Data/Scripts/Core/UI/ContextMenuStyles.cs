using System.Collections.Generic;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Skins;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public static class ContextMenuStyles
    {
        private static readonly MyStringId DefaultSliderStyle = MyStringId.GetOrCompute("ContextMenuSlider");
        private static readonly MyStringId DefaultSliderValueStyle = MyStringId.GetOrCompute("ContextMenuSliderLabel");
        private static readonly MyStringId DefaultLabelStyle = MyStringId.GetOrCompute("ContextMenuLabel");
        private static readonly MyStringId DefaultCheckboxStyle = MyStringId.GetOrCompute("ContextMenuCheckbox");
        private static readonly MyStringId DefaultComboboxStyle = MyStringId.GetOrCompute("ContextMenuCombobox");

        private static MyStringId Filter(MyStringId id, MyStringId fallback) => id == MyStringId.NullOrEmpty ? fallback : id;

        public static void SliderStyles(MyStringId style, out MyGuiControlSliderBase.StyleDefinition slider, out MyGuiControlLabel.StyleDefinition value)
        {
            slider = null;
            value = null;
            MyGuiSkinManager.Skin?.SliderStyles.TryGetValue(Filter(style, DefaultSliderStyle), out slider);
            MyGuiSkinManager.Skin?.LabelStyles.TryGetValue(Filter(slider?.LabelStyle ?? DefaultSliderValueStyle, DefaultSliderValueStyle), out value);
            if (slider == null)
                slider = MyGuiSkinManager.Skin?.DefaultSlider;
            if (value == null)
                value = MyGuiSkinManager.Skin?.DefaultLabel;
        }

        public static MyGuiControlCheckbox.StyleDefinition CheckboxStyle(MyStringId style = default)
        {
            return MyGuiSkinManager.Skin?.CheckboxStyles.GetValueOrDefault(Filter(style, DefaultCheckboxStyle))
                ?? MyGuiSkinManager.Skin?.DefaultCheckbox;
        }

        public static MyGuiControlLabel.StyleDefinition LabelStyle(MyStringId style = default)
        {
            return MyGuiSkinManager.Skin?.LabelStyles.GetValueOrDefault(Filter(style, DefaultLabelStyle))
                ?? MyGuiSkinManager.Skin?.DefaultLabel;
        }

        public static MyGuiControlCombobox.StyleDefinition ComboboxStyle(MyStringId style = default)
        {
            return MyGuiSkinManager.Skin?.ComboboxStyles.GetValueOrDefault(Filter(style, DefaultComboboxStyle))
                ?? MyGuiSkinManager.Skin?.DefaultCombobox;
        }
    }
}