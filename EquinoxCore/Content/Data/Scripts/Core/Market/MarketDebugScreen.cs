using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Equinox76561198048419394.Core.Debug;
using Sandbox.Game.Gui;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketManager : IModDebugScreenSessionComponent
    {
        public IEnumerable<ModDebugScreenComponent.DebugScreen> Screens => new[]
            { new ModDebugScreenComponent.DebugScreen("Markets", typeof(MarketDebugScreen), () => new MarketDebugScreen(this)) };
    }

    public class MarketDebugScreen : MyGuiScreenDebugBase
    {
        private readonly EquiMarketManager _manager;
        private MarketFilter _marketFilter;
        private MarketOrderFilter _orderFilter;
        private MyGuiControlListbox _markets;
        private MyGuiControlListbox _orders;

        public override string GetFriendlyName() => "EquiCoreDebugDraw";

        public MarketDebugScreen(EquiMarketManager manager)
        {
            _manager = manager;
            RecreateControls(true);
        }

        private void FiltersChanged()
        {
            var markets = _manager.Markets.Filter(in _marketFilter);
            var orders = markets.Orders().Filter(in _orderFilter);
            _markets.ClearItems();
            foreach (var market in markets)
            {
                var tooltip = new MyTooltip();
                var title = market.Entity.Get<EquiMarketHostComponent>()?.ToString() ?? market.Entity.ToString();
                tooltip.AddTitle(title);
                tooltip.AddLine($"Entity, {market.Entity.Id}");
                tooltip.AddLine($"Market storage, {market.OrderCount} orders");
                if (market.Entity.Components.TryGet(out EquiMarketHistoryComponent history))
                {
                    tooltip.AddLine($"Market history, {history.Items.Count} unique items");
                    MarketItemHistoryEntry merged = default;
                    foreach (var item in history.Items)
                    foreach (var bucket in item.Value.History)
                        merged.MergeWith(in bucket.Value);
                    tooltip.AddLine($"...{merged.MeanPricePerItem * (ulong) merged.Volume} total currency traded");
                    tooltip.AddLine($"...{merged.Volume} total items traded");
                }

                _markets.Add(new MyGuiControlListbox.Item(
                    new StringBuilder(title),
                    tooltip,
                    userData: market));
            }

            _orders.ClearItems();
            foreach (var remoteOrder in orders)
            {
                var market = remoteOrder.Storage;
                ref readonly var order = ref remoteOrder.Order;
                var item = MyDefinitionManager.Get<MyInventoryItemDefinition>(order.Item);
                var tooltip = new MyTooltip();
                var itemName = item?.DisplayNameText ?? order.Item.ToString();
                var title = $"{order.Type} {itemName} @ {order.DesiredPricePerItem} each";
                tooltip.AddTitle(title);
                tooltip.AddLine($"Type: {order.Type}");
                tooltip.AddLine($"Item: {itemName}");
                tooltip.AddLine($"Price per: {order.DesiredPricePerItem}");
                tooltip.AddLine($"Creator: {MyIdentities.Static?.GetIdentity(order.CreatorId)?.DisplayName ?? order.CreatorId.ToString()}");
                tooltip.AddLine($"Created at: {order.CreatedAt}");
                tooltip.AddLine($"Item progress: {order.RemainingItemAmount}/{order.DesiredItemAmount}");
                tooltip.AddLine($"Stored items: {order.StoredItemAmount}");
                tooltip.AddLine($"Stored money: {order.StoredMoneyAmount}");
                tooltip.AddLine($"Id: {order.LocalId}");
                tooltip.AddLine($"Market: {market.Entity.Get<EquiMarketHostComponent>()?.ToString() ?? market.Entity.ToString()}");

                _orders.Add(new MyGuiControlListbox.Item(
                    new StringBuilder(title),
                    tooltips: tooltip,
                    icon: item?.Icons?.FirstOrDefault(),
                    userData: remoteOrder));
            }
        }

        public override bool Draw()
        {
            // Shim tooltip support into this debug menu.
            var result = base.Draw();
            if (this != MyScreenManager.GetScreenWithFocus() && m_inputContext.ConsumesAllInput)
                foreach (var myGuiControlBase in Controls.GetVisibleControls())
                    myGuiControlBase.ShowToolTip();
            return result;
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.02f, 0.10f);
            m_currentPosition.Y += 0.01f;
            m_scale = 0.7f;

            AddCaption("Markets Debug", Color.Yellow.ToVector4());
            AddShareFocusHint();

            AddLabel("Market Filter", Color.Yellow, 1);

            m_currentPosition.Y += 0.01f;
            AddLabel("Market List", Color.Yellow, 1);
            _markets = AddListBox(4);

            m_currentPosition.Y += 0.01f;
            AddLabel("Order Filter", Color.Yellow, 1);
            AddLabel("Type", Color.Yellow, 0.75f);
            var typeCombo = AddCombo();
            typeCombo.AddItem(long.MaxValue, "Any Type");
            foreach (var item in Enum.GetValues(typeof(MarketOrderType)))
                typeCombo.AddItem((long)(MarketOrderType)item, Enum.GetName(typeof(MarketOrderType), item));
            typeCombo.SelectItemByIndex(0);
            typeCombo.ItemSelected += (_, id) =>
            {
                _orderFilter.Type = id == long.MaxValue ? null : (MarketOrderType?)(MarketOrderType)id;
                FiltersChanged();
            };

            AddLabel("Creator", Color.Yellow, 0.75f);
            var creatorCombo = AddCombo();
            creatorCombo.AddItem(0, "Anybody");
            foreach (var player in MyPlayers.Static.GetAllPlayers().Values)
                if (player.Identity != null)
                    creatorCombo.AddItem(player.Identity.Id, player.Identity.DisplayName);
            creatorCombo.SelectItemByIndex(0);
            creatorCombo.ItemSelected += (_, id) =>
            {
                _orderFilter.CreatorId = id == 0 ? null : (long?)id;
                FiltersChanged();
            };

            AddValueSlider("Min price per item", false);
            AddValueSlider("Max price per item", true);

            m_currentPosition.Y += 0.01f;
            AddLabel("Order List", Color.Yellow, 1);
            _orders = AddListBox(8);

            FiltersChanged();
            return;

            MyGuiControlListbox AddListBox(int rows)
            {
                var lb = new MyGuiControlListbox
                {
                    OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
                    Position = m_currentPosition,
                    VisibleRowsCount = rows,
                };
                lb.ApplyStyle(new MyGuiControlListbox.StyleDefinition
                {
                    Texture = MyGuiConstants.TEXTURE_SCROLLABLE_LIST,
                    ItemTextureHighlight = @"Textures\GUI\Controls\item_highlight_dark.dds",
                    ItemFontNormal = MyGuiConstants.DEFAULT_FONT,
                    ItemFontHighlight = MyGuiConstants.DEFAULT_FONT,
                    ItemSize = new Vector2(0.22f, 0.02f),
                    TextScale = 0.5f,
                    TextOffset = 3f / 500f,
                    DrawScroll = true,
                    PriorityCaptureInput = false,
                    XSizeVariable = false,
                    ScrollbarMargin = new MyGuiBorderThickness
                    {
                        Left = 2f / MyGuiConstants.GUI_OPTIMAL_SIZE.X,
                        Right = 1f / MyGuiConstants.GUI_OPTIMAL_SIZE.X,
                        Top = 3f / MyGuiConstants.GUI_OPTIMAL_SIZE.Y,
                        Bottom = 3f / MyGuiConstants.GUI_OPTIMAL_SIZE.Y
                    }
                });
                m_currentPosition.Y += lb.Size.Y + 0.01f + Spacing;
                Controls.Add(lb);
                return lb;
            }

            void AddValueSlider(string name, bool maxPrice)
            {
                const float max = 1e6f;
                const float exp = 4;
                var slider = AddSlider(name, 0, max, () => 0, _ => { });
                slider.Properties.RatioToValue = r => (float)(Math.Pow(r, exp) * max);
                slider.Properties.ValueToRatio = v => (float)Math.Pow(v / max, 1 / exp);
                slider.Value = maxPrice ? max : 0;
                slider.ValueChanged += _ =>
                {
                    if (maxPrice)
                        _orderFilter.MaxPricePerItem = slider.Value < max ? (uint?)slider.Value : null;
                    else
                        _orderFilter.MinPricePerItem = slider.Value > 0 ? (uint?)slider.Value : null;
                    FiltersChanged();
                };
            }
        }
    }
}