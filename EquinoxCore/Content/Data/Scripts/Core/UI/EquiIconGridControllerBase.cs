using System.Collections.Generic;
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
            m_dataSource = new SearchableGridDataSource(this, m_menu.Context.GetDataSource<IMyGridDataSource<T>>(EquiDef.DataId));
            return result;
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

            return new MyGuiControlParent
            {
                Layout = new MyVerticalLayoutBehavior(spacing: Margin.Y * 2),
                Size = new Vector2(grid.Size.X, grid.Size.Y + Margin.Y * 2 + query.Size.Y),
                Controls = { query, grid }
            };
        }

        protected abstract bool Filter(in T item, string query);
        protected abstract void ItemData(in T item, MyGrid.Item target);

        private void Filter(string query)
        {
            if (!(m_dataSource is SearchableGridDataSource search))
                return;
            var upstreamIndex = search.FilteredToUpstream(_appliedIndex);
            var changed = search.Filter(query);
            if (changed)
                RecreatePaging();
            var selected = search.UpstreamToFiltered(upstreamIndex);
            LoadPage((selected ?? 0) / m_grid.MaxItemCount, changed);
            m_grid.SelectedIndex = selected % m_grid.MaxItemCount;
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

        protected override MyGrid.Item CreateGridItem(int index)
        {
            if (DataSource == null)
                return base.CreateGridItem(index);

            var item = DataSource.GetData(index);
            var target = new MyGrid.Item();
            ItemData(in item, target);
            return target;
        }

        private class SearchableGridDataSource : IMyGridDataSource<T>
        {
            private readonly EquiIconGridControllerBase<T> _owner;
            private readonly IMyGridDataSource<T> _upstream;
            private bool _filtered;
            private readonly Dictionary<int, int> _upstreamToFiltered = new Dictionary<int, int>();
            private readonly List<FilterResult> _filterResults = new List<FilterResult>();

            public SearchableGridDataSource(EquiIconGridControllerBase<T> owner, IMyGridDataSource<T> upstream)
            {
                _upstream = upstream;
                _owner = owner;
            }

            /// <summary>
            /// Filters the grid data source.
            /// </summary>
            /// <returns>true if the data source changed</returns>
            public bool Filter(string query)
            {
                if (string.IsNullOrEmpty(query))
                {
                    var changed = _filtered;
                    _filtered = false;
                    _upstreamToFiltered.Clear();
                    _filterResults.Clear();
                    return changed;
                }

                using (PoolManager.Get<HashSet<int>>(out var prevVisible))
                {
                    var changed = !_filtered;

                    _filtered = true;
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

            public void Close() => _upstream.Close();

            public T GetData(int index)
            {
                if (!_filtered)
                    return _upstream.GetData(index);
                return index < _filterResults.Count ? _filterResults[index].Value : default;
            }

            public void SetData(int index, T value)
            {
                if (!_filtered)
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

            public int Length => _filtered ? _filterResults.Count : _upstream.Length;

            public int? FilteredToUpstream(int? index)
            {
                if (!_filtered)
                    return index;
                if (index == null || index < 0 || index >= _filterResults.Count)
                    return null;
                return _filterResults[index.Value].UpstreamIndex;
            }

            public int? UpstreamToFiltered(int? index)
            {
                if (!_filtered || index == null)
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
        }
    }

    public class MyObjectBuilder_EquiIconGridControllerBase : MyObjectBuilder_GridController
    {
    }

    public abstract class EquiIconGridControllerBaseDefinition : MyGridControllerDefinition
    {
        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            var ob = (MyObjectBuilder_EquiIconGridControllerBaseDefinition)builder;
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

    public abstract class MyObjectBuilder_EquiIconGridControllerBaseDefinition : MyObjectBuilder_ContextMenuControllerDefinition
    {
        public string DataId;
        public GridDefinition Grid;
        public SerializableVector2 GridSize;
        public SerializableVector2 PagingButtonSize;
        public string PagingButtonStyle;
        public string LabelStyle;
    }
}