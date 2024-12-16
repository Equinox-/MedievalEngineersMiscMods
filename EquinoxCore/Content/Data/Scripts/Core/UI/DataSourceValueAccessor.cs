using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.DataSources;
using VRage.Utils;

namespace Equinox76561198048419394.Core.UI
{
    public readonly struct DataSourceAccessor<T> where T : class, IMyContextMenuDataSource
    {
        private readonly object _raw;
        private readonly MyStringId _id;

        public DataSourceAccessor(MyContextMenuController controller, MyStringId id)
        {
            _raw = controller;
            _id = id;
        }

        public T DataSource
        {
            get
            {
                switch (_raw)
                {
                    case MyContextMenuController controller:
                        return controller.Menu?.Context?.GetDataSource<T>(_id);
                    case MyContextMenu menu:
                        return menu.Context?.GetDataSource<T>(_id);
                    case MyContextMenuContext ctx:
                        return ctx.GetDataSource<T>(_id);
                    case T ds:
                        return ds;
                    default:
                        return null;
                }
            }
        }
    }

    public readonly struct DataSourceValueAccessor<T> where T : struct
    {
        private readonly DataSourceAccessor<IMyContextMenuDataSource> _ds;
        private readonly int _index;

        public DataSourceValueAccessor(MyContextMenuController controller, MyStringId id, int index)
        {
            _ds = new DataSourceAccessor<IMyContextMenuDataSource>(controller, id);
            _index = index;
        }

        public DataSourceValueAccessor(MyContextMenuController controller, MyObjectBuilder_EquiAdvancedControllerDefinition.DataSourceReference dsr)
        {
            _ds = new DataSourceAccessor<IMyContextMenuDataSource>(controller, dsr.Id);
            _index = dsr.Index;
        }

        public DataSourceValueAccessor(DataSourceAccessor<IMyContextMenuDataSource> ds, int index)
        {
            _ds = ds;
            _index = index;
        }

        public T? GetValue()
        {
            switch (_ds.DataSource)
            {
                case IMySingleValueDataSource<T> a:
                    return a.GetData();
                case IMyArrayDataSource<T> b when _index < b.Length:
                    return b.GetData(_index);
                default:
                    return null;
            }
        }

        public void SetValue(T value)
        {
            switch (_ds.DataSource)
            {
                case IMySingleValueDataSource<T> a:
                    a.SetData(value);
                    return;
                case IMyArrayDataSource<T> b when _index < b.Length:
                    b.SetData(_index, value);
                    return;
                default:
                    return;
            }
        }

        public T? Min
        {
            get
            {
                switch (_ds.DataSource)
                {
                    case IBoundedSingleValueDataSource<T> a:
                        return a.Min;
                    case IBoundedArrayDataSource<T> b when _index < b.Length:
                        return b.GetMin(_index);
                    default:
                        return null;
                }
            }
        }

        public T? Max
        {
            get
            {
                switch (_ds.DataSource)
                {
                    case IBoundedSingleValueDataSource<T> a:
                        return a.Max;
                    case IBoundedArrayDataSource<T> b when _index < b.Length:
                        return b.GetMax(_index);
                    default:
                        return null;
                }
            }
        }

        public T? Default
        {
            get
            {
                switch (_ds.DataSource)
                {
                    case IBoundedSingleValueDataSource<T> a:
                        return a.Default;
                    case IBoundedArrayDataSource<T> b when _index < b.Length:
                        return b.GetDefault(_index);
                    default:
                        return null;
                }
            }
        }
    }
}