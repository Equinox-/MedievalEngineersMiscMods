using System.Xml.Serialization;
using Medieval.Definitions.GUI.Controllers;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Controllers;
using ObjectBuilders.Definitions.GUI;
using ObjectBuilders.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using VRage;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Equinox76561198048419394.Core.Mesh
{
    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiDecorativeDecalsController))]
    public class EquiDecorativeDecalsController : MyGridController<EquiDecorativeDecalToolDefinition.DecalDef>
    {
        private int _appliedIndex;

        public override MyGuiControlParent CreateControl()
        {
            var control = base.CreateControl();

            m_grid.EnableSelectEmptyCell = false;
            return control;
        }

        public override void Update()
        {
            base.Update();
            var selected = DataSource.SelectedIndex;
            if (!selected.HasValue || _appliedIndex == selected)
                return;
            _appliedIndex = selected.Value;
            LoadPage(selected.Value / m_grid.MaxItemCount);
            m_grid.SelectedIndex = selected.Value % m_grid.MaxItemCount;
        }

        private static readonly string[] FallbackIcons = { "Textures/GUI/Icons/buttons/Other.png" };
        protected override MyGrid.Item CreateGridItem(int index)
        {
            if (DataSource == null)
                return base.CreateGridItem(index);

            var item = DataSource.GetData(index);
            return new MyGrid.Item
            {
                Icons = item.UiIcons ?? FallbackIcons,
                Text = item.UiIcons == null ? item.Name : null,
                Tooltip = new MyTooltip(item.Name),
                Enabled = true,
                UserData = item,
            };
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeDecalsController : MyObjectBuilder_GridController
    {
    }

    [MyDefinitionType(typeof(MyObjectBuilder_EquiDecorativeDecalsControllerDefinition))]
    public class EquiDecorativeDecalsControllerDefinition : MyGridControllerDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_EquiDecorativeDecalsControllerDefinition)builder;
            base.Init(new MyObjectBuilder_GridControllerDefinition
            {
                Id = ob.Id,
                Enabled = ob.Enabled,
                Title = ob.Title,
                CloseOnEsc = ob.CloseOnEsc,
                Margin = ob.Margin,
                DataId = ob.DataId,
                Grid = ob.Grid,
                GridSize = ob.GridSize,
                PagingButtonSize = ob.PagingButtonSize,
                PagingButtonStyle = ob.PagingButtonStyle,
                LabelStyle = ob.LabelStyle,
            });
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDecorativeDecalsControllerDefinition : MyObjectBuilder_ContextMenuControllerDefinition
    {
        public string DataId;
        public GridDefinition Grid;
        public SerializableVector2 GridSize;
        public SerializableVector2 PagingButtonSize;
        public string PagingButtonStyle;
        public string LabelStyle;
    }
}
