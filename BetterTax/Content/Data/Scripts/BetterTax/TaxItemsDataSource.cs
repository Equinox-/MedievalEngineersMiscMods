using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.UI;
using Medieval.GUI.ContextMenu.DataSources;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using Sandbox.Gui.Styles;
using Sandbox.Gui.Utility;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Core;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.BetterTax
{
    internal class TaxItemsDataSource : IMyGridDataSource<IEquiIconGridItem>, IEquiDataSourceWithChangeEvent
    {
        private readonly EquiBetterTaxComponent _tax;
        private readonly MyInventoryBase _inv;
        private readonly Func<TaxItem> _get;
        private readonly Action<TaxItem> _set;
        private readonly Func<TimeSpan> _requiredTax;

        private bool _bound;
        private bool _scheduled;
        private List<TaxItem> _items;
        private Dictionary<MyInventoryItemDefinition, int> _index;

        public event Action OnChange;

        public TaxItemsDataSource(
            EquiBetterTaxComponent tax,
            MyInventoryBase inv,
            Func<TaxItem> get,
            Action<TaxItem> set,
            Func<TimeSpan> requiredTax)
        {
            _tax = tax;
            _inv = inv;
            _get = get;
            _set = set;
            _requiredTax = requiredTax;
            PoolManager.Get(out _items);
            PoolManager.Get(out _index);
            if (inv != null) inv.ContentsChanged += OnContentsChanged;
            _bound = true;
            UpdateContents(default);
        }

        private void OnContentsChanged(MyInventoryBase _)
        {
            if (_scheduled) return;
            _scheduled = true;
            MySession.Static?.GameUpdateScheduler?.AddScheduledCallback(UpdateContents);
        }

        private void UpdateContents(long _)
        {
            _scheduled = false;
            if (!_bound) return;
            _items.Clear();
            _index.Clear();
            foreach (var item in _inv.Items)
            {
                if (item.Amount <= 0) continue;
                var def = item.GetDefinition();
                if (!_index.TryGetValue(def, out var index))
                {
                    var valuePer = _tax.GetValue(def);
                    if (valuePer <= TimeSpan.Zero) continue;
                    index = _items.Count;
                    _index.Add(def, index);
                    _items.Add(new TaxItem(def, valuePer, _requiredTax));
                }

                _items[index].AmountAvailable += item.Amount;
            }

            // Sort so the highest value is first.
            _items.Sort((a, b) => -a.ValueAvailableTicks.CompareTo(b.ValueAvailableTicks));

            // Rebuild index after sorting.
            for (var i = 0; i < _items.Count; i++)
                _index[_items[i].Definition] = i;

            OnChange?.Invoke();
        }

        public void Close()
        {
            _bound = false;
            if (_inv != null)
                _inv.ContentsChanged -= OnContentsChanged;
            PoolManager.Return(ref _items);
            PoolManager.Return(ref _index);
        }

        public IEquiIconGridItem GetData(int index) => _items[index];

        void IMyArrayDataSource<IEquiIconGridItem>.SetData(int _1, IEquiIconGridItem _2)
        {
        }

        public int Length => _items?.Count ?? 0;

        int? IMyGridDataSource<IEquiIconGridItem>.SelectedIndex
        {
            get
            {
                var def = _get();
                return def != null && _index.TryGetValue(def.Definition, out var ix) ? ix : 0;
            }
            set
            {
                if (value >= 0 && value.Value < _items.Count) _set(_items[value.Value]);
            }
        }
    }

    public class MyObjectBuilder_EquiBetterTaxItem : MyObjectBuilder_Base
    {
    }

    internal class TaxItem : IEquiIconGridItemSearchable, IEquiIconGridItemTooltip, IMyObject
    {
        private readonly Func<TimeSpan> _requiredTax;
        public readonly MyInventoryItemDefinition Definition;
        public readonly TimeSpan ValuePer;
        public int AmountAvailable;
        public int AmountRequired => (int)Math.Ceiling(_requiredTax().Ticks / (float)ValuePer.Ticks);
        public long ValueAvailableTicks => ValuePer.Ticks * AmountAvailable;
        public TimeSpan ValueAvailable => TimeSpan.FromTicks(ValueAvailableTicks);

        public TaxItem(MyInventoryItemDefinition definition, TimeSpan valuePer, Func<TimeSpan> requiredTax)
        {
            _requiredTax = requiredTax;
            Definition = definition;
            ValuePer = valuePer;
        }

        public string Name => Definition.DisplayNameText;

        public string[] UiIcons => Definition.Icons;
        public bool Test(string query) => Definition.DisplayNameText?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        public bool DynamicTooltip => true;

        public void BindTooltip(MyTooltip tooltip)
        {
            tooltip.AddTitle(Definition.DisplayNameText);
            tooltip.AddLine($"{AmountAvailable} available of {AmountRequired} required for full payment");
            tooltip.AddLine($"{EquiBetterClaimBlockInteractionContext.FormatTime(ValuePer)} each");
        }

        void IMyObject.Deserialize(MyObjectBuilder_Base builder) => throw new NotImplementedException();

        MyObjectBuilder_Base IMyObject.Serialize() => throw new NotImplementedException();

        IMyObjectIdentifier IMyObject.Id => throw new NotImplementedException();

        MyDefinitionId IMyObject.DefinitionId => new MyDefinitionId(typeof(MyObjectBuilder_EquiBetterTaxItem));

        bool IMyObject.NeedsSerialize => throw new NotImplementedException();
    }


    [MyItemRendererDescriptor("EquiBetterTaxItem")]
    internal class TaxItemRenderer : MyGridItemRendererBase
    {
        public override void Draw(MyGrid.Item item, MyGrid.GridItemState state, RectangleF itemRect, Color colormask, MyStateBase style, float transitionAlpha)
        {
            base.Draw(item, state, itemRect, colormask, style, transitionAlpha);

            var taxItem = item.UserData as TaxItem;
            if (taxItem == null) return;

            var size = itemRect.Size;
            var enabled = item.Enabled && state != MyGrid.GridItemState.Disabled;
            var font = style.Font;
            var color = ApplyColorMaskModifiers(font.Color, enabled, transitionAlpha);

            var available = taxItem.AmountAvailable;
            var required = taxItem.AmountRequired;

            MyFontHelper.DrawString(
                font.Font,
                $"{Math.Min(100, available * 100 / required)} %",
                itemRect.Position,
                font.Size,
                color,
                MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
                maxTextWidth: size.X);

            MyFontHelper.DrawString(
                font.Font,
                $"{Math.Min(available, required)}x",
                itemRect.Position + new Vector2(0, size.Y) - MyGuiConstants.DEFAULT_ITEM_COUNT_OFFSET,
                font.Size,
                color,
                MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM,
                maxTextWidth: size.X);
        }
    }
}