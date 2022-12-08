using System;
using System.Xml.Serialization;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using ObjectBuilders.GUI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public interface IConditionDataSource : IMyContextMenuDataSource
    {
        bool GetData();
    }

    public interface IConditionArrayDataSource : IMyContextMenuDataSource
    {
        int Length { get; }

        bool GetData(int index);
    }

    public sealed class SimpleConditionDataSource : IConditionDataSource
    {
        private readonly Func<bool> _getter;

        public SimpleConditionDataSource(Func<bool> getter) => _getter = getter;

        public void Close()
        {
        }

        public bool GetData() => _getter();
    }

    public sealed class SimpleArrayConditionDataSource : IConditionArrayDataSource
    {
        private readonly Func<int, bool> _getter;

        public SimpleArrayConditionDataSource(int length, Func<int, bool> getter)
        {
            Length = length;
            _getter = getter;
        }

        public void Close()
        {
        }

        public int Length { get; }

        public bool GetData(int index) => _getter(index);
    }

    [MyContextMenuConditionType(typeof(MyObjectBuilder_EquiDataSourceCondition))]
    public class EquiDataSourceCondition : MyContextMenuCondition
    {
        private MyStringId _dataSource;
        private int _index;

        public override void Init(MyContextMenuController parent, MyObjectBuilder_ContextMenuCondition builder)
        {
            base.Init(parent, builder);
            var ob = (MyObjectBuilder_EquiDataSourceCondition)builder;

            _dataSource = MyStringId.GetOrCompute(ob.DataSource);
            _index = ob.Index;
        }

        public override bool IsValid()
        {
            var context = m_parent.Menu.Context;
            var array = context.GetDataSource<IConditionArrayDataSource>(_dataSource);
            if (array != null)
                return _index >= 0 && _index < array.Length && array.GetData(_index);
            return _index == 0 && (context.GetDataSource<IConditionDataSource>(_dataSource)?.GetData() ?? false);
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiDataSourceCondition : MyObjectBuilder_ContextMenuCondition
    {
        [XmlAttribute]
        public string DataSource;

        [XmlAttribute]
        public int Index;

        [XmlAttribute]
        public string Reason
        {
            get => DisabledDescription;
            set => DisabledDescription = value;
        }
    }
}