using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Misc
{
    public partial class EquiCurrencySystems
    {
        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            var isAdmin = MyAPIGateway.Session.IsAdminModeEnabled(sender);
            if (!MyAPIGateway.Session.CreativeMode && !isAdmin)
                return Respond("You need to enable Medieval Master to use this command in survival.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerInventory = player?.ControlledEntity?.GetInventory();
            if (playerInventory == null)
                return Respond("You must have a character to use this command");
            var tokens = message.Split(' ');

            const string modeGive = "give";
            const string modeTake = "take";
            const string modeCount = "count";
            const string modeDefault = "default";

            if (tokens.Length < 2) return HelpListModes();
            switch (tokens[1])
            {
                case modeGive: return ModeGive();
                case modeTake: return ModeTake();
                case modeCount: return ModeCount();
                case modeDefault: return ModeDefault();
                default: return HelpListModes();
            }

            bool Respond(string response)
            {
                _chat.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, response);
                return true;
            }

            bool HelpListModes() => Respond($"{tokens[0]} {modeGive}|{modeTake}|{modeCount}");

            bool GetSystem(out EquiCurrencySystemDefinition def)
            {
                var systemName = tokens[2];
                def = systemName == "default" ? Default : MyDefinitionManager.Get<EquiCurrencySystemDefinition>(systemName);
                return def == null && Respond($"Unknown currency system: {systemName}");
            }

            bool GetSystemAndAmount(out EquiCurrencySystemDefinition def, out ulong amount)
            {
                amount = 0ul;
                if (GetSystem(out def))
                    return true;
                if (!ulong.TryParse(tokens[3], out amount))
                    return Respond($"Failed to parse currency amount {tokens[3]}");
                return false;
            }

            bool ModeGive()
            {
                if (tokens.Length < 4)
                    return Respond($"{tokens[0]} {tokens[1]} default|currencySystemSubtype amount onlyIfFits? roundUp?");
                if (GetSystemAndAmount(out var def, out var amount))
                    return true;
                var onlyIfFits = true;
                if (tokens.Length > 4 && !bool.TryParse(tokens[4], out onlyIfFits))
                    return Respond($"Failed to parse onlyIfFits: {tokens[4]}");
                var roundUp = false;
                if (tokens.Length > 5 && !bool.TryParse(tokens[5], out roundUp))
                    return Respond($"Failed to parse roundUp: {tokens[5]}");
                var given = def.GiveCurrency(playerInventory, amount, onlyIfFits, roundUp);
                return Respond($"Gave {def.Format(given)} ({given}) currency");
            }

            bool ModeTake()
            {
                if (tokens.Length < 4)
                    return Respond($"{tokens[0]} {tokens[1]} default|currencySystemSubtype amount onlyIfEnough? giveChange?");
                if (GetSystemAndAmount(out var def, out var amount))
                    return true;
                var onlyIfEnough = true;
                if (tokens.Length > 4 && !bool.TryParse(tokens[4], out onlyIfEnough))
                    return Respond($"Failed to parse onlyIfEnough: {tokens[4]}");
                var giveChange = false;
                if (tokens.Length > 5 && !bool.TryParse(tokens[5], out giveChange))
                    return Respond($"Failed to parse giveChange: {tokens[5]}");
                var taken = def.TakeCurrency(playerInventory, amount, onlyIfEnough, giveChange);
                return Respond($"Took {def.Format(taken)} ({taken}) currency");
            }

            bool ModeCount()
            {
                if (tokens.Length < 3)
                    return Respond($"{tokens[0]} {tokens[1]} default|currencySystemSubtype");
                if (GetSystem(out var def))
                    return true;
                var amount = def.TotalValue(playerInventory);
                return Respond($"Counted {def.Format(amount)} ({amount}) currency");
            }

            bool ModeDefault()
            {
                if (tokens.Length <= 2)
                {
                    var currentMode = _defaultOverride.HasValue
                        ? $"{_defaultOverride.Value.SubtypeName} (overrides {_defaultFromDefinitions.Id.SubtypeName})"
                        : Default.Id.SubtypeName;
                    var allSystems = string.Join("|", MyDefinitionManager.GetOfType<EquiCurrencySystemDefinition>().Select(x => x.Id.SubtypeName));
                    if (_defaultOverride.HasValue)
                        allSystems += "|reset";
                    Respond($"Default currency system: {currentMode}");
                    return Respond($"To change: {tokens[0]} {tokens[1]} {allSystems}");
                }

                if (tokens[2] == "reset")
                {
                    ChangeDefault(null);
                    return Respond($"Reset default currency system to {Default.Id.SubtypeName}");
                }

                if (GetSystem(out var def))
                    return true;
                ChangeDefault(def.Id);
                return Respond($"Overrode default currency system to {def.Id.SubtypeName}");
            }
        }
    }
}