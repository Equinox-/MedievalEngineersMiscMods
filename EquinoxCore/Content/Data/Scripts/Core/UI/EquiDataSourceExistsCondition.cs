using System;
using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using ObjectBuilders.GUI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    [MyContextMenuConditionType(typeof(MyObjectBuilder_EquiDataSourceExistsCondition))]
    public class EquiDataSourceExistsCondition : MyContextMenuCondition
    {
        private MyStringId _dataSource;

        public override void Init(MyContextMenuController parent, MyObjectBuilder_ContextMenuCondition builder)
        {
            base.Init(parent, builder);
            var ob = (MyObjectBuilder_EquiDataSourceExistsCondition)builder;

            _dataSource = MyStringId.GetOrCompute(ob.DataSource);
        }

        public override bool IsValid()
        {
            return m_parent.Menu.Context.GetDataSource<IMyContextMenuDataSource>(_dataSource) != null;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDataSourceExistsCondition : MyObjectBuilder_ContextMenuCondition
    {
        [XmlAttribute]
        public string DataSource;

        [XmlAttribute]
        public string Reason
        {
            get => DisabledDescription;
            set => DisabledDescription = value;
        }
    }
}