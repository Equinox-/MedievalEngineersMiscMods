using System;
using System.Collections.Generic;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.GUI.Controllers;
using Medieval.GUI.ContextMenu;
using Medieval.GUI.ContextMenu.Attributes;
using Medieval.GUI.ContextMenu.Controllers;
using Medieval.GUI.ContextMenu.DataSources;
using Medieval.GUI.Controls;
using ObjectBuilders.Definitions.GUI;
using ObjectBuilders.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using Sandbox.Gui.Layouts;
using VRage;
using VRage.Game;
using VRage.Library.Collections;
using VRage.Logging;
using VRageMath;

namespace Equinox76561198048419394.Core.UI
{
    // Not actually used, but needed to suppress the inherited attribute.
    [MyContextMenuControllerType(typeof(MyObjectBuilder_EquiIconGridControllerBase))]
    public abstract class EquiIconGridControllerBase<T> : MyGridController<T>
    {
        private EquiIconGridControllerBaseDefinition EquiDef => (EquiIconGridControllerBaseDefinition)m_definition;
        private int _appliedIndex;

        public override bool BeforeAddedToMenu(MyContextMenu menu, int position)
        {
            var result = base.BeforeAddedToMenu(menu, position);
            var backing = m_menu.Context.GetDataSource<IMyGridDataSource<T>>(EquiDef.DataId);
            if (backing == null)
            {
                this.GetLogger().Warning($"Context menu data source {EquiDef.DataId} was not present");
                return result;
            }
            if (m_dataSource is IEquiDataSourceWithChangeEvent dscOld) dscOld.OnChange -= OnChange;
            m_dataSource = new SearchableGridDataSource(this, backing);
            if (m_dataSource is IEquiDataSourceWithChangeEvent dscNew) dscNew.OnChange += OnChange;
            return result;
        }

        private void OnChange()
        {
            RecreatePaging();
            EnforceSelection(DataSource.SelectedIndex ?? 0, true);
        }

        public override void AfterRemovedFromMenu(MyContextMenu menu)
        {
            if (m_dataSource is IEquiDataSourceWithChangeEvent dscOld) dscOld.OnChange -= OnChange;
            if (m_dataSource is SearchableGridDataSource searchable)
            {
                searchable.Close();
                m_dataSource = null;
            }
            base.AfterRemovedFromMenu(menu);
        }

        public override MyGuiControlParent CreateControl()
        {
            var grid = base.CreateControl();
            grid.LayoutStyle = MyGuiControlLayoutStyle.DynamicX;
            m_grid.EnableSelectEmptyCell = false;

            var query = new MySearchBox(size: new Vector2(grid.Size.X, 0.04f))
            {
                LayoutStyle = MyGuiControlLayoutStyle.DynamicX
            };
            query.OnTextChanged += Filter;

            EquiDef.CreateEventActions(this, out var onClick, out var onDoubleClick);
            if (onClick != null)
            {
                onClick.SetAssociatedControl(m_grid);
                m_grid.ItemClickedWithoutDoubleClick += (_1, _2) => onClick.Run();
            }

            if (onDoubleClick != null)
            {
                onDoubleClick.SetAssociatedControl(m_grid);
                m_grid.ItemDoubleClicked += (_1, _2) => onDoubleClick.Run();
            }


            return new MyGuiControlParent
            {
                Layout = new MyVerticalLayoutBehavior(spacing: Margin.Y * 2),
                Size = new Vector2(grid.Size.X, grid.Size.Y + Margin.Y * 2 + query.Size.Y),
                Controls = { query, grid }
            };
        }

        protected abstract bool Filter(in T item, string query);
        protected abstract void ItemData(in T item, MyGrid.Item target);

        protected virtual void RefreshTooltip(in T item, MyGrid.Item target)
        {
        }

        private void Filter(string query) => (m_dataSource as SearchableGridDataSource)?.Filter(query);

        public override void Update()
        {
            // Don't call base.Update(), it has weird WASD navigation of the grid that isn't compatible with the search bar.
            // base.Update();

            var hoveredItem = m_grid.TryGetItemAt(m_grid.MouseOverIndex);
            if (hoveredItem?.UserData is T val)
                RefreshTooltip(in val, hoveredItem);
            var selected = DataSource.SelectedIndex;
            if (!selected.HasValue || _appliedIndex == selected) return;
            EnforceSelection(selected.Value, false);
        }

        private void EnforceSelection(int selected, bool refresh)
        {
            _appliedIndex = selected;
            LoadPage(selected / m_grid.MaxItemCount, refresh);
            m_grid.SelectedIndex = selected % m_grid.MaxItemCount;
        }

        protected override MyGrid.Item CreateGridItem(int index)
        {
            if (DataSource == null)
                return base.CreateGridItem(index);

            var item = DataSource.GetData(index);
            var target = new MyGrid.Item();
            ItemData(in item, target);
            return target;
        }

        private class SearchableGridDataSource : IMyGridDataSource<T>, IEquiDataSourceWithChangeEvent
        {
            private readonly EquiIconGridControllerBase<T> _owner;
            private readonly IMyGridDataSource<T> _upstream;
            private string _filter;
            private readonly Dictionary<int, int> _upstreamToFiltered = new Dictionary<int, int>();
            private readonly List<FilterResult> _filterResults = new List<FilterResult>();

            private bool Filtered => !string.IsNullOrEmpty(_filter);

            public SearchableGridDataSource(EquiIconGridControllerBase<T> owner, /* will not close */ IMyGridDataSource<T> upstream)
            {
                _upstream = upstream;
                _owner = owner;
                if (_upstream is IEquiDataSourceWithChangeEvent dsc)
                    dsc.OnChange += OnUpstreamChanged;
            }

            /// <summary>
            /// Filters the grid data source.
            /// </summary>
            /// <returns>true if the data source changed</returns>
            public bool Filter(string query)
            {
                var result = ApplyFilter(query);
                if (result) OnChange?.Invoke();
                return result;
            }

            private bool ApplyFilter(string query)
            {
                if (string.IsNullOrEmpty(query))
                {
                    var changed = Filtered;
                    _filter = query;
                    _upstreamToFiltered.Clear();
                    _filterResults.Clear();
                    return changed;
                }

                using (PoolManager.Get<HashSet<int>>(out var prevVisible))
                {
                    var changed = !Filtered;

                    _filter = query;
                    _filterResults.Clear();
                    foreach (var prev in _upstreamToFiltered.Keys)
                        prevVisible.Add(prev);
                    _upstreamToFiltered.Clear();
                    for (var i = 0; i < _upstream.Length; i++)
                    {
                        var item = _upstream.GetData(i);
                        if (!_owner.Filter(in item, query))
                        {
                            changed = changed || prevVisible.Contains(i);
                            continue;
                        }

                        _upstreamToFiltered.Add(i, _filterResults.Count);
                        _filterResults.Add(new FilterResult { UpstreamIndex = i, Value = item });
                        changed = changed || !prevVisible.Contains(i);
                    }
                    return changed;
                }
            }

            public void Close()
            {
                if (_upstream is IEquiDataSourceWithChangeEvent dsc)
                    dsc.OnChange -= OnUpstreamChanged;
            }

            public T GetData(int index)
            {
                if (!Filtered)
                    return _upstream.GetData(index);
                return index < _filterResults.Count ? _filterResults[index].Value : default;
            }

            public void SetData(int index, T value)
            {
                if (!Filtered)
                {
                    _upstream.SetData(index, value);
                    return;
                }

                if (index >= _filterResults.Count)
                    return;
                var result = _filterResults[index];
                _upstream.SetData(result.UpstreamIndex, value);
                result.Value = value;
                _filterResults[index] = result;
            }

            public int Length => Filtered ? _filterResults.Count : _upstream.Length;

            public int? FilteredToUpstream(int? index)
            {
                if (!Filtered)
                    return index;
                if (index == null || index < 0 || index >= _filterResults.Count)
                    return null;
                return _filterResults[index.Value].UpstreamIndex;
            }

            public int? UpstreamToFiltered(int? index)
            {
                if (!Filtered || index == null)
                    return index;
                return _upstreamToFiltered.TryGetValue(index.Value, out var filteredIndex) ? (int?)filteredIndex : null;
            }

            public int? SelectedIndex
            {
                get => UpstreamToFiltered(_upstream.SelectedIndex);
                set => _upstream.SelectedIndex = FilteredToUpstream(value);
            }

            private struct FilterResult
            {
                public int UpstreamIndex;
                public T Value;
            }

            public event Action OnChange;

            private void OnUpstreamChanged()
            {
                ApplyFilter(_filter);
                OnChange?.Invoke();
            }
        }
    }

    public class MyObjectBuilder_EquiIconGridControllerBase : MyObjectBuilder_GridController
    {
    }

    public abstract class EquiIconGridControllerBaseDefinition : MyGridControllerDefinition
    {
        private MyObjectBuilder_ContextMenuAction _onClick;
        private MyObjectBuilder_ContextMenuAction _onDoubleClick;

        public void CreateEventActions(MyContextMenuController owner, out MyContextMenuAction onClick, out MyContextMenuAction onDoubleClick)
        {
            onClick = _onClick != null ? MyContextMenuFactory.CreateContextMenuAction(owner, _onClick) : null;
            onDoubleClick = _onDoubleClick != null ? MyContextMenuFactory.CreateContextMenuAction(owner, _onDoubleClick) : null;
        }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_EquiIconGridControllerBaseDefinition)builder;
            ob.Grid = ob.Grid ?? new MyObjectBuilder_ContextMenuControllerDefinition.GridDefinition();
            if (ob.Grid.StyleName == null) ob.Grid.StyleName = "ContextMenuInventoryGrid";
            if (ob.Grid.Columns == 0) ob.Grid.Columns = 4;
            if (ob.Grid.Rows == 0) ob.Grid.Rows = 5;
            if (ob.Grid.MaxItems == 0) ob.Grid.MaxItems = ob.Grid.Columns * ob.Grid.Rows;
            base.Init(new MyObjectBuilder_GridControllerDefinition
            {
                Id = ob.Id,
                Enabled = ob.Enabled,
                Title = ob.Title,
                CloseOnEsc = ob.CloseOnEsc,
                Margin = ob.Margin,
                DataId = ob.DataId,
                Grid = ob.Grid,
                GridSize = ob.GridSize ?? new SerializableVector2(320, 400),
                PagingButtonSize = ob.PagingButtonSize ?? new SerializableVector2(32, 32),
                PagingButtonStyle = ob.PagingButtonStyle ?? "ContextButtonPage",
                LabelStyle = ob.LabelStyle ?? "ContextMenuLabel",
                Buttons = AbstractXmlProxy.UnwrapArray(ob.Buttons),
            });

            _onClick = ob.OnClick;
            _onDoubleClick = ob.OnDoubleClick;
        }
    }

    public abstract class MyObjectBuilder_EquiIconGridControllerBaseDefinition : MyObjectBuilder_ContextMenuControllerDefinition
    {
        public string DataId;
        public GridDefinition Grid;
        public SerializableVector2? GridSize;
        public SerializableVector2? PagingButtonSize;
        public string PagingButtonStyle;
        public string LabelStyle;
        public AbstractXmlProxy<ButtonDefinition>[] Buttons;
        public AbstractXmlProxy<MyObjectBuilder_ContextMenuAction> OnClick;
        public AbstractXmlProxy<MyObjectBuilder_ContextMenuAction> OnDoubleClick;
    }
}