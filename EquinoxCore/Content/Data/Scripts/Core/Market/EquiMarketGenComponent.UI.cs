using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Equinox76561198048419394.Core.Util;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Gui;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Skins;
using Sandbox.Gui.Styles;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketGenComponent
    {
        [StaticEventOwner]
        public class MarketGenComponentScreen : MyGuiScreenDebugBase
        {
            private readonly MyEntity _entity;

            private MyGuiControlTextbox _identityControl;

            private MyGuiControlListbox _orderSetsControl;
            private MyGuiControlListbox _ordersControl;
            private MyGuiControlListbox _referencedControl;
            private MyGuiControlSlider _orderSetInterval;
            private MyGuiControlSlider _orderSetMultiplier;
            private MyGuiControlSlider _orderSetAmount;
            private MyGuiControlSlider _orderSetStock;

            private MyGuiControlBase _orderAdd;
            private MyGuiControlBase _orderRemove;
            private MyGuiControlLabel _orderDesc;
            private MyGuiControlTextbox _orderSubtype;
            private MyGuiControlSlider _orderAmount;
            private MyGuiControlSlider _orderPrice;
            private MyGuiControlMultilineText _searchResults;

            private readonly List<OrderSetContext> _orderSets = new List<OrderSetContext>();
            private string _identity;

            private MyIdentity Identity
            {
                get
                {
                    foreach (var identity in MyIdentities.Static.GetAllIdentities().Values)
                        if (identity.DisplayName.Equals(_identity))
                            return identity;
                    return null;
                }
            }

            private readonly Queue<Action> _eventQueue = new Queue<Action>();

            public MarketGenComponentScreen(MyEntity entity)
            {
                _entity = entity;
                RecreateControls(true);
                // Not a normal debug screen.
                CanHaveFocus = true;
                if (MyGuiSkinManager.Skin.Textures.TryGetValue(MyStringId.GetOrCompute("DecoratedPanel"), out var backgroundTexture))
                    BackgroundTexture = backgroundTexture;
            }

            public void RequestUiData() => MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestUiData, _entity.Id);

            internal class OrderSetContext
            {
                public uint Multiplier;
                public EquiMarketGenOrderSetDefinition Referenced;
                public TimeSpan Interval;
                public uint Amount = 1;
                public uint Stockpile = 1;
                public readonly List<OrderContext> Orders = new List<OrderContext>();
            }

            internal class OrderContext
            {
                public string ItemSubtype;
                public int Amount;
                public uint Price;

                public MyInventoryItemDefinition Definition => EquiDefinitions.TryGetItemDefinition(ItemSubtype, out var def) ? def : null;

                public string Desc => OrderDesc(Definition?.DisplayNameText ?? $"!{ItemSubtype}!", Amount, Price);
            }

            private static string OrderDesc(string itemName, int amount, uint price) =>
                $"{(amount < 0 ? "Buy " : "Sell ")}{Math.Abs(amount)}x {itemName} @ {price}/ea";


            public override string GetFriendlyName() => "MarketGenComponent";

            public override void RecreateControls(bool constructor)
            {
                base.RecreateControls(constructor);

                const float spaceBeforeHeader = .005f;
                const float headerScale = .75f;
                const float labelScale = .5f;

                m_currentPosition = -m_size.Value / 2.0f;
                AddLabel(
                    $"Market Gen: {(_entity.Components.TryGet(out EquiMarketHostComponent host) ? host.ToString() : _entity.ToString())}",
                    Color.Yellow, headerScale);

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Identity", Color.Yellow, headerScale);
                _identityControl = AddTextbox("", _ => RunEvent(UpdateIdentity), scale: labelScale);
                _identityControl.Border = new MyBorderStyle { Enabled = false, Color = Color.Red, Size = 2 };
                _identityControl.MaxLength = 128;

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Order Sets", Color.Yellow, headerScale);
                _orderSetsControl = AddListBox(4);
                _orderSetsControl.ItemsSelected += _ => RunEvent(OrderSetSelectionChanged);
                AddRow(
                    () => AddButton("Add Order Set", _ => RunEvent(AddOrderSet)),
                    () => AddButton("Remove Order Set", _ => RunEvent(RemoveOrderSet)));

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Order Set", Color.Yellow, headerScale);
                const float intervalMax = 24 * 7;
                const float intervalExp = 2;
                _orderSetInterval = AddSlider("Interval (hrs)", 1, 1 / 60f, intervalMax, _ => RunEvent(UpdateOrderSet));
                _orderSetInterval.Properties.RatioToValue = r => (float)Math.Pow(r, intervalExp) * intervalMax;
                _orderSetInterval.Properties.ValueToRatio = v => (float)Math.Pow(v / intervalMax, 1 / intervalExp);
                _orderSetMultiplier = AddSlider("Multiplier", 0, 1, 1000, _ => RunEvent(UpdateOrderSet));
                _orderSetMultiplier.IntValue = true;
                _orderSetAmount = AddSlider("Amount", 0, 1, 1000, _ => RunEvent(UpdateOrderSet));
                _orderSetAmount.IntValue = true;
                _orderSetStock = AddSlider("Stock", 0, 1, 1000, _ => RunEvent(UpdateOrderSet));
                _orderSetStock.IntValue = true;

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Referenced Sets", Color.Yellow, headerScale);
                _referencedControl = AddListBox(4);
                foreach (var def in MyDefinitionManager.GetOfType<EquiMarketGenOrderSetDefinition>())
                    _referencedControl.Add(new MyGuiControlListbox.Item(new StringBuilder(def.Id.SubtypeName), toolTip: null, userData: def));
                _referencedControl.ItemsSelected += _ => RunEvent(ReferencedOrderSetChanged);

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Orders in Set", Color.Yellow, headerScale);
                _ordersControl = AddListBox(4);
                _ordersControl.ItemsSelected += _ => RunEvent(OrderSelectionChanged);
                AddRow(
                    () => _orderAdd = AddButton("Add Order", _ => RunEvent(AddOrder)),
                    () => _orderRemove = AddButton("Remove Order", _ => RunEvent(RemoveOrder)));

                m_currentPosition.Y += spaceBeforeHeader;
                AddLabel("Order", Color.Yellow, headerScale);
                _orderDesc = AddLabel("Nothing", Color.Yellow, labelScale);
                _orderSubtype = AddTextbox("", _ => RunEvent(UpdateOrder), scale: labelScale);
                _orderSubtype.Border = new MyBorderStyle { Enabled = false, Color = Color.Red, Size = 2 };
                _orderSubtype.MaxLength = 128;
                _orderAmount = AddSlider("Amount", 0, -50, 50, _ => RunEvent(UpdateOrder));
                _orderAmount.IntValue = true;

                const float priceMax = 1e6f;
                const float priceExp = 4;
                _orderPrice = AddSlider("Price", 1, 1, priceMax, _ => RunEvent(UpdateOrder));
                _orderPrice.Properties.RatioToValue = r => (float)Math.Round(Math.Pow(r, priceExp) * priceMax);
                _orderPrice.Properties.ValueToRatio = v => (float)Math.Pow(v / priceMax, 1 / priceExp);
                _orderPrice.IntValue = true;
                var end = m_size.Value.Y / 2 - spaceBeforeHeader * 5;
                _searchResults = AddMultilineText(size: new Vector2(m_size.Value.X, end - m_currentPosition.Y), textScale: labelScale);

                m_currentPosition.Y = end;
                AddRow(
                    () => AddButton("Save", _ => RunEvent(Save)),
                    () => AddButton("Close", _ => CloseScreen()));
                return;

                MyGuiControlListbox AddListBox(int rows)
                {
                    var lb = new MyGuiControlListbox
                    {
                        OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
                        Position = m_currentPosition,
                        VisibleRowsCount = rows,
                        MultiSelect = false,
                    };
                    lb.ApplyStyle(new MyGuiControlListbox.StyleDefinition
                    {
                        Texture = MyGuiConstants.TEXTURE_SCROLLABLE_LIST,
                        ItemTextureHighlight = @"Textures\GUI\Controls\item_highlight_dark.dds",
                        ItemFontNormal = MyGuiConstants.DEFAULT_FONT,
                        ItemFontHighlight = MyGuiConstants.DEFAULT_FONT,
                        ItemSize = new Vector2(0.22f, 0.02f),
                        TextScale = labelScale,
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

                void AddRow(params Func<MyGuiControlBase>[] ctls)
                {
                    var pos = m_currentPosition;
                    var controls = ctls.Select(x => x()).ToArray();
                    var posX = -controls.Select(x => x.Size.X + Spacing).Sum() / 2;
                    var sizeY = 0f;
                    foreach (var ctl in controls)
                    {
                        ctl.Position = new Vector2(posX + ctl.Size.X / 2, pos.Y);
                        posX += ctl.Size.X + Spacing;
                        sizeY = Math.Max(sizeY, ctl.Size.Y);
                    }

                    m_currentPosition.Y = pos.Y + sizeY + 0.01f + Spacing;
                }
            }

            private void RunEvent(Action evt)
            {
                if (_eventQueue.Count > 0)
                {
                    _eventQueue.Enqueue(evt);
                    return;
                }

                _eventQueue.Enqueue(evt);
                while (_eventQueue.Count > 0)
                {
                    _eventQueue.Peek()();
                    _eventQueue.Dequeue();
                }
            }

            private bool TryGetSelectedOrderSet(out MyGuiControlListbox.Item item, out OrderSetContext ctx)
            {
                item = _orderSetsControl.SelectedItems.FirstOrDefault();
                ctx = item?.UserData as OrderSetContext;
                return ctx != null;
            }

            private bool TryGetSelectedOrder(out MyGuiControlListbox.Item item, out OrderContext ctx)
            {
                item = _ordersControl.SelectedItems.FirstOrDefault();
                ctx = item?.UserData as OrderContext;
                return ctx != null;
            }

            private void Save()
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => SaveUiHeader, new RpcHeader
                {
                    Entity = _entity.Id,
                    Identity = Identity?.Id ?? 0,
                });
                foreach (var set in _orderSets)
                    if (set.Referenced != null)
                        MyMultiplayerModApi.Static.RaiseStaticEvent(x => SaveRefOrderSet, new RpcReferencedOrderSet
                        {
                            Entity = _entity.Id,
                            Multiplier = set.Multiplier,
                            Referenced = set.Referenced.Id,
                        });
                    else if (set.Orders.All(x => x.Definition != null))
                        MyMultiplayerModApi.Static.RaiseStaticEvent(x => SaveInlineOrderSet, new RpcInlineOrderSet
                        {
                            Entity = _entity.Id,
                            Multiplier = set.Multiplier,
                            Inline = new MyObjectBuilder_EquiMarketGenOrderSetConfig
                            {
                                Interval = set.Interval,
                                Amount = set.Amount,
                                Stockpile = set.Stockpile,
                                Orders = set.Orders.Select(x => new MyObjectBuilder_EquiMarketGenOrderSetConfig.EquiMarketOrderGen
                                {
                                    Id = x.Definition.Id,
                                    Amount = x.Amount,
                                    Price = x.Price,
                                }).ToList(),
                            }
                        });
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => SaveUiTrailer, new RpcTrailer
                {
                    Entity = _entity.Id,
                });
            }

            private void UpdateIdentity()
            {
                _identity = _identityControl.Text;
                _identityControl.Border.Enabled = Identity == null;
                _searchResults.Clear();
                if (string.IsNullOrEmpty(_identity)) return;
                foreach (var id in MyIdentities.Static.GetAllIdentities().Values)
                    if (id.DisplayName.IndexOf(_identity, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var faction = MyFactionManager.Instance.GetFactionForPlayer(id.Id);
                        if (faction != null)
                            _searchResults.AppendText($"[{faction.FactionTag}] ");
                        _searchResults.AppendText($"{id.DisplayName} ({MyPlayers.Static.GetPlayer(id)?.Id.SteamId.ToString() ?? "NPC"})");
                        _searchResults.AppendLine();
                    }
            }

            private void OrderSetSelectionChanged()
            {
                var good = TryGetSelectedOrderSet(out _, out var os);
                _ordersControl.ClearItems();
                _referencedControl.ClearItems();
                if (good)
                {
                    _orderSetMultiplier.Value = os.Multiplier;
                    _orderSetAmount.Value = os.Amount;
                    _orderSetStock.Value = os.Stockpile;
                    _orderSetInterval.Value = (float)os.Interval.TotalHours;

                    if (os.Referenced != null)
                        _referencedControl.SelectedItems.AddRange(_referencedControl.Items.Where(x => x.UserData == os.Referenced));
                    else
                        foreach (var order in os.Orders)
                        {
                            var item = new MyGuiControlListbox.Item(new StringBuilder(), toolTip: null, userData: order);
                            RecreateOrderItem(item);
                            _ordersControl.Add(item);
                        }
                }

                _referencedControl.Enabled = good;
                _orderSetMultiplier.Enabled = good;
            }

            private void AddOrderSet()
            {
                var os = new OrderSetContext();
                _orderSets.Add(os);
                var item = new MyGuiControlListbox.Item(new StringBuilder(), tooltips: new MyTooltip(), userData: os);
                RecreateOrderSetItem(item);
                _orderSetsControl.Add(item);
            }

            private void RemoveOrderSet()
            {
                if (!TryGetSelectedOrderSet(out _, out var os)) return;
                _orderSets.Remove(os);
                _orderSetsControl.Remove(i => i.UserData == os);
            }

            private void UpdateOrderSet()
            {
                if (!TryGetSelectedOrderSet(out _, out var os)) return;
                os.Interval = TimeSpan.FromHours(_orderSetInterval.Value);
                os.Multiplier = (uint)Math.Round(_orderSetMultiplier.Value);
                os.Amount = (uint)Math.Round(_orderSetAmount.Value);
                os.Stockpile = (uint)Math.Round(_orderSetStock.Value);
            }

            private void ReferencedOrderSetChanged()
            {
                var good = TryGetSelectedOrderSet(out var item, out var os);
                if (good)
                {
                    os.Referenced = _referencedControl.SelectedItems.FirstOrDefault()?.UserData as EquiMarketGenOrderSetDefinition;
                    RecreateOrderSetItem(item);
                    good = os.Referenced == null;
                }

                _ordersControl.Enabled = good;
                _orderAdd.Enabled = good;
                _orderRemove.Enabled = good;
                _orderSetAmount.Enabled = good;
                _orderSetStock.Enabled = good;
                _orderSetInterval.Enabled = good;
            }

            private void OrderSelectionChanged()
            {
                var good = TryGetSelectedOrder(out _, out var order);
                if (good)
                {
                    _orderSubtype.Text = order.ItemSubtype ?? "";
                    _orderSubtype.Border.Enabled = order.Definition == null;
                    _orderAmount.Value = order.Amount;
                    _orderPrice.Value = order.Price;
                }

                _orderSubtype.Enabled = good;
                _orderAmount.Enabled = good;
                _orderPrice.Enabled = good;
            }

            private void UpdateOrder()
            {
                if (!TryGetSelectedOrderSet(out var osItem, out _)) return;
                if (!TryGetSelectedOrder(out var orderItem, out var order)) return;
                var subtype = _orderSubtype.Text;
                order.ItemSubtype = subtype;
                _orderSubtype.Border.Enabled = order.Definition == null;
                order.Amount = (int)Math.Round(_orderAmount.Value);
                order.Price = (uint)Math.Round(_orderPrice.Value);
                _orderDesc.Text = order.Desc;
                _searchResults.Clear();
                if (!string.IsNullOrEmpty(subtype))
                    foreach (var def in MyDefinitionManager.GetOfType<MyInventoryItemDefinition>())
                        if (def.Id.SubtypeName.IndexOf(subtype, StringComparison.OrdinalIgnoreCase) >= 0
                            || def.DisplayNameText.IndexOf(subtype, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _searchResults.AppendText($"{def.Id.SubtypeName} ({def.DisplayNameText})");
                            _searchResults.AppendLine();
                        }


                RecreateOrderSetItem(osItem);
                RecreateOrderItem(orderItem);
            }

            private void AddOrder()
            {
                if (!TryGetSelectedOrderSet(out var osItem, out var os)) return;
                var order = new OrderContext();
                os.Orders.Add(order);
                var item = new MyGuiControlListbox.Item(new StringBuilder(), toolTip: null, userData: order);
                RecreateOrderItem(item);
                _ordersControl.Add(item);
                RecreateOrderSetItem(osItem);
            }

            private void RemoveOrder()
            {
                if (!TryGetSelectedOrderSet(out var osItem, out var os)) return;
                if (!TryGetSelectedOrder(out _, out var order)) return;
                os.Orders.Remove(order);
                _ordersControl.Remove(i => i.UserData == order);
                RecreateOrderSetItem(osItem);
            }

            private void RecreateOrderSetControl()
            {
                _orderSetsControl.ClearItems();
                foreach (var os in _orderSets)
                {
                    var item = new MyGuiControlListbox.Item(new StringBuilder(), new MyTooltip(), userData: os);
                    RecreateOrderSetItem(item);
                    _orderSetsControl.Add(item);
                }
            }

            private static void RecreateOrderItem(MyGuiControlListbox.Item item)
            {
                var order = item?.UserData as OrderContext;
                if (order == null) return;
                item.Text.Clear();
                item.Text.Append(order.Desc);
            }

            private static void RecreateOrderSetItem(MyGuiControlListbox.Item item)
            {
                var os = item?.UserData as OrderSetContext;
                if (os == null) return;
                using (item.Tooltip.OpenBatch(true))
                {
                    item.Text.Clear();
                    if (os.Referenced != null)
                    {
                        item.Text.Append(os.Referenced.Id.SubtypeName);
                        item.Tooltip.AddTitle(os.Referenced.Id.SubtypeName);
                        foreach (var order in os.Referenced.Orders.Values)
                            item.Tooltip.AddLine(OrderDesc(order.Item.DisplayNameText, order.Amount, order.Price));
                    }
                    else
                    {
                        foreach (var order in os.Orders)
                            if (order.Amount > 0)
                            {
                                item.Text.Append(order.Desc);
                                break;
                            }

                        foreach (var order in os.Orders)
                            item.Tooltip.AddLine(order.Desc);
                    }

                    if (item.Text.Length == 0 && os.Orders.Count > 0)
                        item.Text.Append(os.Orders[0].Desc);
                    if (item.Text.Length == 0)
                        item.Text.Append("Untitled");
                }
            }

            #region RPC

            [Event]
            [Reliable]
            [Server]
            internal static void RequestUiData(EntityId id)
            {
                if (!MySession.Static.Scene.TryGetEntity(id, out var entity)) return;
                var sender = MyEventContext.Current.Sender;
                if (entity.Components.TryGet(out EquiMarketGenComponent component))
                {
                    MyMultiplayerModApi.Static.RaiseStaticEvent(x => DeliverUiHeader, new RpcHeader
                    {
                        Entity = id,
                        Identity = component._identityId,
                    }, sender);
                    foreach (var os in component._orderSets)
                        if (os.Inline)
                            MyMultiplayerModApi.Static.RaiseStaticEvent(
                                x => DeliverInlineOrderSet, new RpcInlineOrderSet
                                {
                                    Entity = id,
                                    Multiplier = os.Multiplier,
                                    Inline = os.Definition.SerializeConfig()
                                },
                                sender);
                        else
                            MyMultiplayerModApi.Static.RaiseStaticEvent(
                                x => DeliverRefOrderSet, new RpcReferencedOrderSet
                                {
                                    Entity = id,
                                    Multiplier = os.Multiplier,
                                    Referenced = os.Definition.Id,
                                },
                                sender);
                }
                else
                    MyMultiplayerModApi.Static.RaiseStaticEvent(x => DeliverUiHeader, new RpcHeader
                    {
                        Entity = id,
                    }, sender);

                MyMultiplayerModApi.Static.RaiseStaticEvent(x => DeliverUiTrailer, new RpcTrailer
                {
                    Entity = id,
                }, sender);
            }

            internal static MarketGenComponentScreen Active(EntityId entity)
            {
                var screen = MyScreenManager.GetFirstScreenOfType<MarketGenComponentScreen>();
                return screen?._entity?.Id == entity ? screen : null;
            }

            internal struct RpcHeader
            {
                public EntityId Entity;
                public long Identity;
            }

            internal struct RpcTrailer
            {
                public EntityId Entity;
            }

            internal struct RpcReferencedOrderSet
            {
                public EntityId Entity;
                public uint Multiplier;
                public SerializableDefinitionId Referenced;
            }

            internal struct RpcInlineOrderSet
            {
                public EntityId Entity;
                public uint Multiplier;
                public MyObjectBuilder_EquiMarketGenOrderSetConfig Inline;
            }

            [Event]
            [Reliable]
            [Client, Server]
            internal static void DeliverUiHeader(RpcHeader payload)
            {
                var ctx = Active(payload.Entity);
                if (ctx == null) return;
                var id = MyIdentities.Static.GetIdentity(payload.Identity);
                ctx._identity = id?.DisplayName;
                ctx._identityControl.Text = ctx._identity;
                ctx._identityControl.Border.Enabled = ctx.Identity == null;
                ctx._orderSets.Clear();
            }

            [Event]
            [Reliable]
            [Client, Server]
            internal static void DeliverUiTrailer(RpcTrailer payload)
            {
                var ctx = Active(payload.Entity);
                ctx?.RecreateOrderSetControl();
            }

            [Event]
            [Reliable]
            [Client, Server]
            internal static void DeliverRefOrderSet(RpcReferencedOrderSet payload)
            {
                var ctx = Active(payload.Entity);
                ctx?._orderSets.Add(new OrderSetContext
                {
                    Multiplier = payload.Multiplier,
                    Referenced = MyDefinitionManager.Get<EquiMarketGenOrderSetDefinition>(payload.Referenced)
                });
            }

            [Event]
            [Reliable]
            [Client, Server]
            internal static void DeliverInlineOrderSet(RpcInlineOrderSet payload)
            {
                var ctx = Active(payload.Entity);
                if (ctx == null) return;
                var def = new EquiMarketGenOrderSetDefinition();
                def.InitInternal(payload.Inline);
                var os = new OrderSetContext
                {
                    Multiplier = payload.Multiplier,
                    Interval = def.Interval,
                    Stockpile = def.Stockpile,
                    Amount = def.Amount,
                };
                os.Orders.Clear();
                foreach (var order in def.Orders.Values)
                    os.Orders.Add(new OrderContext
                    {
                        ItemSubtype = order.Item.Id.SubtypeName,
                        Amount = order.Amount,
                        Price = order.Price,
                    });
                ctx._orderSets.Add(os);
            }

            private static EquiMarketGenComponent SaveTarget(EntityId id)
            {
                // Only admins can save market generation info.
                if (!MyEventContext.Current.IsLocallyInvoked
                    && !MyAPIGateway.Session.IsAdminModeEnabled(MyEventContext.Current.Sender.Value)) return null;
                if (!MySession.Static.Scene.TryGetEntity(id, out var entity)) return null;
                // Only operate on market entities.
                if (!entity.Components.Contains<EquiMarketStorageComponent>()) return null;
                // Create the generator component if one doesn't exist.
                return entity.Components.GetOrAdd<EquiMarketGenComponent>();
            }

            [Event]
            [Reliable]
            [Server]
            internal static void SaveUiHeader(RpcHeader payload)
            {
                var ctx = SaveTarget(payload.Entity);
                if (ctx == null) return;
                ctx._identityId = payload.Identity;
                // Clear existing order sets.
                foreach (var set in ctx._orderSets)
                foreach (var state in set.State.Values)
                    state.RemoveOrders(ctx._storage);
                ctx._orderSets.Clear();
            }

            [Event]
            [Reliable]
            [Server]
            internal static void SaveUiTrailer(RpcTrailer payload)
            {
                var ctx = SaveTarget(payload.Entity);
                if (ctx == null) return;
                ctx.Reschedule();
            }

            [Event]
            [Reliable]
            [Server]
            internal static void SaveRefOrderSet(RpcReferencedOrderSet payload)
            {
                var ctx = SaveTarget(payload.Entity);
                if (ctx == null) return;
                if (!MyDefinitionManager.TryGet(payload.Referenced, out EquiMarketGenOrderSetDefinition referenced)) return;
                ctx._orderSets.Add(new EquiMarketGenOrderSet(referenced, false)
                {
                    Multiplier = payload.Multiplier,
                });
            }

            [Event]
            [Reliable]
            [Server]
            internal static void SaveInlineOrderSet(RpcInlineOrderSet payload)
            {
                var ctx = SaveTarget(payload.Entity);
                if (ctx == null) return;
                var def = new EquiMarketGenOrderSetDefinition();
                def.InitInternal(payload.Inline);
                ctx._orderSets.Add(new EquiMarketGenOrderSet(def, true)
                {
                    Multiplier = payload.Multiplier,
                });
            }

            #endregion
        }
    }
}