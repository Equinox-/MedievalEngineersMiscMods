using System.Collections.Generic;
using System.Xml.Serialization;
using Medieval.Definitions.GUI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Controllers;
using ObjectBuilders.Definitions.GUI;
using Sandbox.Game.GUI.Dialogs;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using Sandbox.Gui.Skins;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiAdvancedSliderController))]
    public class EquiAdvancedSliderController : MyContextMenuController, IMyCommitableController
    {
        private struct DataSourceAccessor
        {
            private IBoundedSingleValueDataSource<float> _singleFloat;
            private IBoundedArrayDataSource<float> _arrayFloat;
            private int _sourceIndex;
            private int _arrayIndex;
            private readonly MyContextMenuContext _ctx;
            private readonly ListReader<MyStringId> _sources;

            public DataSourceAccessor(MyContextMenuContext ctx, ListReader<MyStringId> sources)
            {
                _singleFloat = null;
                _arrayFloat = null;
                _sourceIndex = -1;
                _arrayIndex = 0;
                _ctx = ctx;
                _sources = sources;
            }

            public bool MoveNext()
            {
                do
                {
                    _arrayIndex++;
                    var len = _arrayFloat?.Length ?? (_singleFloat != null ? 1 : 0);
                    if (_arrayIndex < len)
                        return true;
                    _sourceIndex++;
                    if (_sourceIndex >= _sources.Count)
                        return false;
                    var id = _sources[_sourceIndex];
                    var source = _ctx.GetDataSource<IMyContextMenuDataSource>(id);
                    _singleFloat = source as IBoundedSingleValueDataSource<float>;
                    _arrayFloat = source as IBoundedArrayDataSource<float>;
                    _arrayIndex = -1;
                } while (true);
            }

            public float GetData() => _arrayFloat?.GetData(_arrayIndex) ?? _singleFloat.GetData();

            public void SetData(float value)
            {
                if (_arrayFloat != null)
                    _arrayFloat.SetData(_arrayIndex, value);
                else
                    _singleFloat.SetData(value);
            }

            public float GetDefault() => _arrayFloat?.GetDefault(_arrayIndex) ?? _singleFloat.Default;

            public float GetMin() => _arrayFloat?.GetMin(_arrayIndex) ?? _singleFloat.Min;

            public float GetMax() => _arrayFloat?.GetMax(_arrayIndex) ?? _singleFloat.Max;
        }

        private DataSourceAccessor Accessor => new DataSourceAccessor(Menu.Context, ((EquiAdvancedSliderControllerDefinition)Definition).DataIds);

        private readonly List<(MyGuiControlParent, MyGuiControlSlider)> _sliders = new List<(MyGuiControlParent, MyGuiControlSlider)>();
        private MyGuiControlParent _container;
        private bool _init;
        private bool _autoCommit;
        private bool _commitPermitted;

        public override MyGuiControlParent CreateControl()
        {
            _init = true;
            var definition = (EquiAdvancedSliderControllerDefinition)Definition;
            _autoCommit = definition.AutoCommit;
            _container = new MyGuiControlParent
            {
                Layout = new MyVerticalLayoutBehavior(
                    padding: new MyGuiBorderThickness(Margin.X, Margin.X, Margin.Y, Margin.Y),
                    spacing: Margin.Y * 2),
                Size = new Vector2(definition.SliderSizeGui.X + Margin.X * 2, 0)
            };
            _sliders.Clear();
            foreach (var sliderDef in definition.Sliders)
            {
                MyGuiControlSliderBase.StyleDefinition sliderStyle = null;
                MyGuiControlLabel.StyleDefinition labelStyle = null;
                if (MyGuiSkinManager.Skin != null)
                {
                    MyGuiSkinManager.Skin.SliderStyles.TryGetValue(sliderDef.StyleNameId, out sliderStyle);
                    if (sliderStyle?.LabelStyle != null)
                        MyGuiSkinManager.Skin.LabelStyles.TryGetValue(sliderStyle.LabelStyle.Value, out labelStyle);
                }

                var leftControl = new MyGuiControlLabel(text: MyTexts.GetString(sliderDef.TextId));
                leftControl.ApplyStyle(labelStyle);
                var valueLabel = new MyGuiControlLabel();
                valueLabel.ApplyStyle(labelStyle);
                var slider = new MyGuiControlSlider(width: definition.SliderSizeGui.X,
                    labelDecimalPlaces: sliderDef.LabelDecimalPlaces,
                    toolTip: MyTexts.GetString(sliderDef.TooltipId), intValue: sliderDef.IsInteger);
                slider.ApplyStyle(sliderStyle);
                slider.ValueChanged += _ =>
                {
                    SelectionChanged();
                    valueLabel.Text = FormatLabel(SafeValue(slider), sliderDef.LabelDecimalPlaces, sliderDef.TextFormat);
                };
                slider.SliderClicked += SliderClicked;
                var labeledSlider = new MyGuiControlParent(size: slider.Size + new Vector2(0.0f, leftControl.Size.Y));
                var layout = new MyLayoutVertical(labeledSlider, MarginPx.X);
                layout.Add(leftControl, valueLabel);
                layout.Add(slider, MyAlignH.Center);
                _sliders.Add((labeledSlider, slider));
            }

            SetSlidersToDataSource();
            _init = false;
            return _container;
        }

        public void CommitDataSource()
        {
            if (!_commitPermitted) return;

            var access = Accessor;
            foreach (var (_, slider) in _sliders)
            {
                if (!access.MoveNext())
                    break;
                if (!slider.Enabled)
                    continue;
                access.SetData(SafeValue(slider));
            }
        }

        private static float SafeValue(MyGuiControlSlider slider)
        {
            var value = slider.Value;
            if (float.IsNaN(value))
                value = (slider.MinValue + slider.MaxValue) / 2;
            return MathHelper.Clamp(value, slider.MinValue, slider.MaxValue);
        }

        private void SetSlidersToDataSource()
        {
            _commitPermitted = false;

            var access = Accessor;
            var rebuildLayout = false;
            foreach (var (labeledSlider, slider) in _sliders)
            {
                if (access.MoveNext())
                {

                    slider.MinValue = access.GetMin();
                    slider.MaxValue = access.GetMax();
                    slider.DefaultValue = access.GetDefault();
                    slider.Value = access.GetData();
                    slider.Enabled = slider.MaxValue > slider.MinValue;
                }
                else
                    slider.Enabled = false;

                rebuildLayout = rebuildLayout || slider.Enabled != _container.Controls.Contains(labeledSlider);
            }

            if (rebuildLayout)
            {
                _container.Controls.Clear(false);
                foreach (var (labeledSlider, slider) in _sliders)
                    if (slider.Enabled)
                        _container.Controls.Add(labeledSlider);
            }
            _container.Size = _container.Layout.ComputedSize;

            _commitPermitted = true;
        }

        public override void Update()
        {
            base.Update();

            if (_autoCommit)
                SetSlidersToDataSource();
        }

        private void SelectionChanged()
        {
            if (!_init && _autoCommit)
                CommitDataSource();
        }

        private static string FormatLabel(float value, int decimalPlaces, string textFormat)
        {
            return string.Format(textFormat, value.ToString("N" + decimalPlaces));
        }

        private static bool SliderClicked(MyGuiControlSliderBase obj)
        {
            var slider = (MyGuiControlSlider)obj;
            if (!MyAPIGateway.Input.IsAnyCtrlKeyDown() || !obj.Enabled)
                return false;
            var dialog = new MyFloatInputDialog(
                MyTexts.GetString(MyCommonTexts.DialogAmount_SetValueCaption),
                slider.MinValue, slider.MaxValue, slider.Value);
            dialog.ResultCallback += v => slider.Value = v;
            MyGuiSandbox.AddScreen(dialog);
            return true;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiAdvancedSliderController : MyObjectBuilder_ContextMenuController
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiAdvancedSliderControllerDefinition))]
    public class EquiAdvancedSliderControllerDefinition : MyContextMenuControllerDefinition
    {
        public ListReader<MyStringId> DataIds { get; private set; }
        public bool AutoCommit { get; private set; }
        public ListReader<MyObjectBuilder_EquiAdvancedSliderControllerDefinition.EquiSliderDefinition> Sliders { get; private set; }
        public Vector2 SliderSizeGui { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiAdvancedSliderControllerDefinition)builder;
            var data = new List<MyStringId>();
            if (ob.DataIds != null)
                foreach (var id in ob.DataIds)
                    data.Add(MyStringId.GetOrCompute(id));
            DataIds = data;
            AutoCommit = ob.AutoCommit ?? false;
            SliderSizeGui = (ob.SliderSize ?? new SerializableVector2(330, 75)) / MyGuiConstants.GUI_OPTIMAL_SIZE;

            var sliders = new List<MyObjectBuilder_EquiAdvancedSliderControllerDefinition.EquiSliderDefinition>();
            if (ob.Sliders != null)
                foreach (var slider in ob.Sliders)
                    sliders.Add(slider);
            Sliders = sliders;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiAdvancedSliderControllerDefinition : MyObjectBuilder_ContextMenuControllerDefinition
    {
        [XmlElement("DataId")]
        public List<string> DataIds;

        [XmlElement("Slider")]
        public List<EquiSliderDefinition> Sliders;

        public SerializableVector2? SliderSize;

        public bool? AutoCommit;

        public class EquiSliderDefinition : LabelDefinition
        {
            [XmlAttribute]
            public bool IsInteger = false;

            public int LabelDecimalPlaces;
            public int LabelSpaceWidth = 32;

            [XmlElement]
            public string DisabledReason;
        }
    }
}