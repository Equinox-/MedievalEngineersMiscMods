using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.GUI;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Controllers;
using ObjectBuilders.Definitions.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiAdvancedController))]
    public class EquiAdvancedController : MyContextMenuController, IMyCommitableController
    {
        private readonly List<IControlHolder> _controls = new List<IControlHolder>();

        private MyGuiControlParent _container;
        private bool _autoCommit;

        public override MyGuiControlParent CreateControl()
        {
            var definition = (EquiAdvancedControllerDefinition)Definition;
            _autoCommit = definition.AutoCommit;
            _container = new MyGuiControlParent
            {
                Layout = new MyVerticalLayoutBehavior(
                    padding: new MyGuiBorderThickness(Margin.X, Margin.X, Margin.Y, Margin.Y),
                    spacing: Margin.Y * 2),
                Size = new Vector2(definition.Width + Margin.X * 2, 0)
            };
            _controls.Clear();
            foreach (var factory in definition.Controls)
                _controls.Add(factory.Create(this));
            SyncToControls();
            return _container;
        }

        public void CommitDataSource()
        {
            foreach (var control in _controls)
                if (control.Root.Enabled)
                    control.SyncFromControl();
        }

        private void SyncToControls()
        {
            var rebuildLayout = false;
            foreach (var control in _controls)
            {
                control.SyncToControl();
                rebuildLayout = rebuildLayout || control.Root.Enabled != _container.Controls.Contains(control.Root);
            }

            if (rebuildLayout)
            {
                _container.Controls.Clear();
                foreach (var control in _controls)
                    if (control.Root.Enabled)
                    {
                        control.Root.LayoutStyle = MyGuiControlLayoutStyle.DynamicX;
                        _container.Controls.Add(control.Root);
                    }
            }

            _container.Size = _container.Layout.ComputedSize + new Vector2(0, Margin.Y);
        }

        public override void Update()
        {
            base.Update();

            if (_autoCommit)
                SyncToControls();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiAdvancedController : MyObjectBuilder_ContextMenuController
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiAdvancedControllerDefinition))]
    public class EquiAdvancedControllerDefinition : MyContextMenuControllerDefinition
    {
        public bool AutoCommit { get; private set; }
        internal ListReader<ControlFactory> Controls { get; private set; }

        public float Width { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_EquiAdvancedControllerDefinition)builder;
            AutoCommit = ob.AutoCommit ?? false;

            Width = (ob.Width ?? 330) / MyGuiConstants.GUI_OPTIMAL_SIZE.X;

            var controls = new List<ControlFactory>();
            if (ob.Controls != null)
                foreach (var ctl in ob.Controls)
                    switch (ctl)
                    {
                        case MyObjectBuilder_EquiAdvancedControllerDefinition.Slider slider:
                            controls.Add(new SliderFactory(this, slider));
                            break;
                        case MyObjectBuilder_EquiAdvancedControllerDefinition.Dropdown dropdown:
                            controls.Add(new DropdownFactory(this, dropdown));
                            break;
                        case MyObjectBuilder_EquiAdvancedControllerDefinition.Checkbox checkbox:
                            controls.Add(new CheckboxFactory(this, checkbox));
                            break;
                        case MyObjectBuilder_EquiAdvancedControllerDefinition.Embedded embedded:
                            controls.Add(new EmbeddedControllerFactory(this, embedded));
                            break;
                    }

            Controls = controls;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiAdvancedControllerDefinition : MyObjectBuilder_ContextMenuControllerDefinition
    {
        [XmlElement("Slider", typeof(Slider))]
        [XmlElement("Dropdown", typeof(Dropdown))]
        [XmlElement("Checkbox", typeof(Checkbox))]
        [XmlElement("Embedded", typeof(Embedded))]
        public List<ControlBase> Controls;

        /// <summary>
        /// Width of the controller to show.
        /// </summary>
        public float? Width;

        /// <summary>
        /// Should the datasource be updated after every change.
        /// </summary>
        public bool? AutoCommit;

        public class ControlBase : LabelDefinition
        {
            /// <summary>
            /// Index in a vector / array data source to edit.
            /// </summary>
            [XmlAttribute]
            public int DataIndex;

            /// <summary>
            /// Text to show when the control is disabled.
            /// </summary>
            [XmlElement]
            public string DisabledReason;
        }

        public class Slider : ControlBase
        {
            /// <summary>
            /// Should the slider be restricted to integer values.
            /// </summary>
            [XmlAttribute]
            public bool IsInteger = false;

            /// <summary>
            /// Minimum value for the slider, used if the data source doesn't provide a minimum.
            /// </summary>
            [XmlElement]
            public float? Min;

            /// <summary>
            /// Maximum value for the slider, used if the data source doesn't provide a maximum.
            /// </summary>
            [XmlElement]
            public float? Max;

            /// <summary>
            /// Default value for the slider, used if the data source doesn't provide a default.
            /// </summary>
            [XmlElement]
            public float? Default;

            /// <summary>
            /// Exponent the normalized slider value (0, 1) is raised to before sending to the data source.
            /// </summary>
            [XmlElement]
            public float? Exponent;

            /// <summary>
            /// How many decimal places to show in the label.
            /// </summary>
            [XmlElement]
            public int LabelDecimalPlaces;
        }

        public class Dropdown : ControlBase
        {
        }

        public class Checkbox : ControlBase
        {
        }

        public class Embedded : ControlBase
        {
            /// <summary>
            /// ID of the embedded Context Menu Controller.
            /// </summary>
            public SerializableDefinitionId Id;
        }
    }
}