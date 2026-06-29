using System;
using System.Globalization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ObjectBuilder;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Market
{
    public partial class EquiMarketManager
    {
        private bool HandleLocationalMarkets(ulong sender, string message, MyChatCommandType handledAsType)
        {
            var isAdmin = MyAPIGateway.Session.IsAdminModeEnabled(sender);
            if (!MyAPIGateway.Session.CreativeMode && !isAdmin)
                return Respond("You need to enable Medieval Master to use this command in survival.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPos = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPos == null)
                return Respond("You must have a character to use this command");

            var tokens = message.Split(' ');

            const string modeInfo = "info";
            const string modeBuy = "buy";
            const string modeSell = "sell";
            const string modeCancel = "cancel";
            const string modeCollect = "collect";
            const string modeHistory = "history";
            const string modeMode = "mode";
            const string modeGenerator = "generator";

            if (tokens.Length < 2) return HelpListModes();
            switch (tokens[1])
            {
                case modeInfo: return ModeInfo();
                case modeBuy: return ModeBuy();
                case modeSell: return ModeSell();
                case modeCancel: return ModeCancel();
                case modeCollect: return ModeCollect();
                case modeHistory: return ModeHistory();
                case modeMode: return ModeMode();
                case modeGenerator: return ModeGenerator();
                default: return HelpListModes();
            }

            bool Respond(string response)
            {
                var channel = MyStringHash.GetOrCompute("System");
                if (handledAsType == MyChatCommandType.Client)
                    _chat.HandleLocalMessage(channel, response);
                else
                    _chat.SendMessageToClient(sender, channel, 0, response);
                return true;
            }

            bool HelpListModes() => Respond($"{tokens[0]} {modeInfo}|{modeBuy}|{modeSell}|{modeCancel}|{modeCollect}|{modeHistory}|{modeMode}|{modeGenerator}");

            bool GetMarket(out EquiMarketStorageComponent marketStorage, bool createIfMissing = false)
            {
                marketStorage = null;
                if (!TryGetLocationalMarket(in playerPos.WorldMatrixRef.Translation(), out var market, createIfMissing))
                    return Respond("Location does not have an initialized market.");

                if (!market.Components.TryGet(out marketStorage))
                    return Respond($"Location market storage entity {market} does not have a market storage component");

                return false;
            }

            bool GetPlayerInventory(out MyInventoryBase inventory)
            {
                inventory = player.ControlledEntity?.GetInventory();
                if (inventory == null)
                    return Respond("Player does not have an inventory");
                return false;
            }

            bool ModeInfo()
            {
                if (GetMarket(out var marketStorage)) return true;
                Respond($"Area market: {marketStorage}");
                foreach (var order in marketStorage.Orders)
                    Respond(
                        $" {order.Type} {order.Item} @ {order.DesiredPricePerItem} each ({order.DesiredItemAmount - order.RemainingItemAmount}/{order.DesiredItemAmount}), stored: {order.StoredItemAmount} items, {order.StoredMoneyAmount} money, id: {order.LocalId}");
                return true;
            }

            bool ModeMode()
            {
                if (tokens.Length <= 2)
                {
                    var currentMode = LocationalMarketsOverride.HasValue
                        ? $"{LocationalMarkets} (overrides {Definition.LocationalMarkets})"
                        : LocationalMarkets.ToString();
                    var allModes = string.Join("|", Enum.GetNames(typeof(LocationalMarketsMode)));
                    if (LocationalMarketsOverride.HasValue)
                        allModes += "|reset";
                    Respond($"Locational markets mode: {currentMode}");
                    return Respond($"To change: {tokens[0]} {tokens[1]} {allModes}");
                }

                if (handledAsType == MyChatCommandType.Client) return Respond("Not supported on the client.");

                var modeOverride = tokens[2];
                if (modeOverride == "reset")
                {
                    LocationalMarketsOverride = null;
                    return Respond($"Reset locational markets mode to {Definition.LocationalMarkets}");
                }

                if (!Enum.TryParse(modeOverride, out LocationalMarketsMode mode))
                    return Respond($"Unknown locational market mode {modeOverride}");
                LocationalMarketsOverride = mode;
                return Respond($"Overrode locational markets mode to {mode}");
            }

            bool ModeGenerator()
            {
                if (handledAsType != MyChatCommandType.Client) return Respond("Only supported on the client.");
                if (GetMarket(out var marketStorage, true)) return true;
                var screen = new EquiMarketGenComponent.MarketGenComponentScreen(marketStorage.Entity);
                MyScreenManager.AddScreenNow(screen);
                screen.RequestUiData();
                return Respond($"Opened market generator UI");
            }

            bool GetItemArg(out MyInventoryItemDefinition item)
            {
                item = null;
                if (tokens.Length < 3)
                    return Respond($"{tokens[0]} {tokens[1]} item");
                var itemTokens = tokens[2].Split('/');
                if (itemTokens.Length == 1)
                    EquiDefinitions.TryGetItemDefinition(itemTokens[0], out item);
                else if (MyObjectBuilderType.TryParse(itemTokens[0], out var itemType))
                    item = MyDefinitionManager.Get<MyInventoryItemDefinition>(new MyDefinitionId(itemType, itemTokens[1]));

                if (item == null)
                    return Respond($"Unknown item {tokens[2]}, should be subtype or type/subtype.");
                return false;
            }

            bool GetBuySellArgs(out MyInventoryItemDefinition item, out uint pricePerItem, out uint amount)
            {
                item = null;
                amount = 0;
                pricePerItem = 0;
                if (tokens.Length < 5)
                    return Respond($"{tokens[0]} {tokens[1]} item pricePerItem amount");
                if (GetItemArg(out item))
                    return false;
                var itemTokens = tokens[2].Split('/');
                if (itemTokens.Length == 1)
                    EquiDefinitions.TryGetItemDefinition(itemTokens[0], out item);
                else if (MyObjectBuilderType.TryParse(itemTokens[0], out var itemType))
                    item = MyDefinitionManager.Get<MyInventoryItemDefinition>(new MyDefinitionId(itemType, itemTokens[1]));

                if (item == null)
                    return Respond($"Unknown item {tokens[2]}, should be subtype or type/subtype.");
                if (!uint.TryParse(tokens[3], out pricePerItem))
                    return Respond($"Unable to parse price per item {tokens[3]}");
                if (!uint.TryParse(tokens[4], out amount))
                    return Respond($"Unable to parse item amount {tokens[4]}");
                return false;
            }

            bool ModeBuy()
            {
                if (GetMarket(out var marketStorage, true)) return true;
                if (GetBuySellArgs(out var item, out var pricePerItem, out var amount)) return true;
                if (handledAsType == MyChatCommandType.Server)
                {
                    var id = marketStorage.CreateBuyOrder(player.Identity, item, pricePerItem, amount, pricePerItem * amount);
                    Respond($"Created order, id={id}");
                    return true;
                }

                if (GetPlayerInventory(out var inventory)) return true;
                var result = marketStorage.CanCreateBuyOrder(inventory, item, pricePerItem, amount);
                if (result != EquiMarketStorageComponent.CanCreateBuyOrderResult.Okay)
                    return Respond($"Cannot create buy order: {result}");
                marketStorage.RequestCreateBuyOrder(inventory, item, pricePerItem, amount);
                return Respond("Requested buy order creation");
            }

            bool ModeSell()
            {
                if (GetMarket(out var marketStorage, true)) return true;
                if (GetBuySellArgs(out var item, out var pricePerItem, out var amount)) return true;
                if (handledAsType == MyChatCommandType.Server)
                {
                    var id = marketStorage.CreateSellOrder(player.Identity, item, pricePerItem, amount);
                    Respond($"Created order, id={id}");
                    return true;
                }

                if (GetPlayerInventory(out var inventory)) return true;
                var result = marketStorage.CanCreateSellOrder(inventory, item, pricePerItem, amount);
                if (result != EquiMarketStorageComponent.CanCreateSellOrderResult.Okay)
                    return Respond($"Cannot create sell order: {result}");
                marketStorage.RequestCreateSellOrder(inventory, item, pricePerItem, amount);
                return Respond("Requested sell order creation");
            }

            bool GetManageOrderArgs(out MarketOrderLocalId id)
            {
                id = default;
                if (tokens.Length < 3)
                    return Respond($"{tokens[0]} {tokens[1]} orderId");
                if (!ulong.TryParse(tokens[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rawOrderId))
                    return Respond($"Unable to parse order ID {tokens[2]}");
                id = rawOrderId;
                return false;
            }

            bool ModeCancel()
            {
                if (GetMarket(out var marketStorage)) return true;
                if (GetManageOrderArgs(out var id)) return true;
                if (handledAsType == MyChatCommandType.Server)
                {
                    var cancelled = marketStorage.CancelOrder(id);
                    return Respond(cancelled ? "Order was cancelled" : "Order did not exist");
                }

                var result = marketStorage.CanCancelOrder(id);
                if (result != EquiMarketStorageComponent.CanCancelOrderResult.Okay)
                    return Respond($"Cannot cancel order: {result}");
                marketStorage.RequestCancelOrder(id);
                return Respond("Requested order cancellation");
            }

            bool ModeCollect()
            {
                if (GetMarket(out var marketStorage)) return true;
                if (GetManageOrderArgs(out var id)) return true;
                if (handledAsType == MyChatCommandType.Server)
                {
                    var collectResult = marketStorage.CollectOrder(id,
                        ref sender, (ref ulong senderCaptured, in MyDefinitionId item, int amount) =>
                        {
                            var granted = (amount + 1) / 2;
                            MyChatSystem.Static.SendMessageToClient(senderCaptured, MyStringHash.GetOrCompute("System"),
                                0, $"Collected {item}, {granted} of {amount}.");
                            return granted;
                        },
                        (ref ulong senderCaptured, int amount) =>
                        {
                            var granted = (amount + 1) / 2;
                            MyChatSystem.Static.SendMessageToClient(senderCaptured, MyStringHash.GetOrCompute("System"),
                                0, $"Collected money, {granted} of {amount}");
                            return granted;
                        });
                    return Respond($"Collect result: {collectResult}");
                }

                if (GetPlayerInventory(out var inventory)) return true;
                var result = marketStorage.CanCollectOrder(inventory, id);
                if (result != EquiMarketStorageComponent.CanCollectOrderResult.Okay)
                    return Respond($"Cannot collect order: {result}");
                marketStorage.RequestCollectOrder(inventory, id);
                return Respond("Requested order collection");
            }

            bool ModeHistory()
            {
                if (GetMarket(out var marketStorage)) return true;
                if (GetItemArg(out var itemDef)) return true;
                Respond($"Current: {marketStorage.HistoryAt(itemDef.Id, DateTime.UtcNow)}");
                Respond($"Last day: {marketStorage.HistoryOver(itemDef.Id, DateTime.UtcNow - TimeSpan.FromDays(1), TimeSpan.FromDays(1))}");
                Respond($"Last week: {marketStorage.HistoryOver(itemDef.Id, DateTime.UtcNow - TimeSpan.FromDays(7), TimeSpan.FromDays(7))}");
                return Respond($"Full history: {marketStorage.History(itemDef.Id).BucketsMerged}");
            }
        }
    }
}