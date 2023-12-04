using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Equinox76561198048419394.Cartography.MapLayers;
using Equinox76561198048419394.Core.Inventory;
using Sandbox.Game.Entities.Planet;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace CartographyBaker;

public class ItemGeneratorSymbolBaker
{
    public static void Generate(string saveFile)
    {
        var subtypeMapping = new Dictionary<string, (string fmt, string icon, double scaling)>
        {
            ["OreGold"] = ("{0:F0}x Gold Ore", @"Textures\GUI\Icons\Materials\OreGold.dds", 1),
            ["OreSilver"] = ("{0:F0}x Silver Ore", @"Textures\GUI\Icons\Materials\OreSilver.dds", 1),
            ["OreCopper"] = ("{0:F0}x Copper Ore", @"Textures\GUI\Icons\Materials\OreCopper.dds", 1),
            ["OreIron"] = ("{0:F0}x Iron Ore", @"Textures\GUI\Icons\Materials\IronOre.dds", 1),
            ["OreCalamine"] = ("{0:F0}x Calamine", @"Textures\GUI\Icons\Ore_Calamine____.dds", 1),
            ["FluidCrudeOil"] = ("{0:F0}L Crude Oil", @"Textures\GUI\Icons\fluid_crude_oil.dds", 1e-2),
            ["OreCoalLignite"] = ("{0:F0}x Lignite Coal", @"Textures\GUI\Icons\LigniteCoalOre.dds", 1),
            ["OreCoalSubBituminous"] = ("{0:F0}x Sub-bituminous Coal", @"Textures\GUI\Icons\SubBituminousCoalOre.dds", 1),
            ["OreCoalBituminous"] = ("{0:F0}x Bituminous Coal", @"Textures\GUI\Icons\BituminousCoalOre.dds", 1),
            ["OreCoal"] = ("{0:F0}x Anthracite Coal", @"Textures\GUI\Icons\AnthraciteCoalOre.dds", 1),
            ["OreTin"] = ("{0:F0}x Tin Ore", @"Textures\GUI\Icons\Materials\OreTin.dds", 1),
            ["OreGalena"] = ("{0:F0}x Galena", @"Textures\GUI\Icons\Ore_Galena______.dds", 1),
            ["SulfurRockLarge"] = ("{0:F0}x Sulfur", @"Textures\GUI\Icons\Ore_Sulfur_Large.dds", 1),
            ["KNO3"] = ("{0:F0}x Saltpeter", @"Textures\GUI\Saltpeter.dds", 1),
        };
        var definition = new MyObjectBuilder_EquiSymbolsMapLayerDefinition
        {
            Symbols = new List<MyObjectBuilder_EquiSymbolsMapLayerDefinition.MyObjectBuilder_Symbol>()
        };
        SaveFileAccessor.Entities(saveFile)
            .ForEach(entity =>
            {
                foreach (var block in entity.BlockEntities)
                {
                    var itemGenerator = block.Entity.Component("MyObjectBuilder_EquiItemGeneratorComponent")
                        ?.DeserializeAs<MyObjectBuilder_EquiItemGeneratorComponent>();
                    if (!(itemGenerator?.Actions?.Count > 0)) continue;
                    var parsedActions = new List<(string subtype, int amount)>();
                    foreach (var action in itemGenerator.Actions)
                    {
                        if (action.Mode != InventoryActionBuilder.MutableInventoryActionMode.GiveItem)
                            continue;
                        if (subtypeMapping.ContainsKey(action.Subtype))
                            parsedActions.Add((action.Subtype, action.Amount));
                        else
                            Console.WriteLine($"Failed to find type mapping for {action.Subtype}");
                    }

                    if (parsedActions.Count == 0) continue;
                    Vector3D position = entity.Position.Position;
                    var interval = TimeSpan.FromTicks(itemGenerator.Interval);
                    MyEnvironmentCubemapHelper.ProjectToCube(ref position, out var face, out var texCoord);

                    parsedActions.Sort((a, b) => a.amount.CompareTo(b.amount));
                    var total = 0;
                    foreach (var action in parsedActions)
                        total += action.amount;

                    var symbol = new MyObjectBuilder_EquiSymbolsMapLayerDefinition.MyObjectBuilder_Symbol
                    {
                        Face = face,
                        Position = (Vector2)texCoord,
                        IconGroups = new List<MyObjectBuilder_EquiSymbolsMapLayerDefinition.MyObjectBuilder_Symbol.IconGroup>(),
                        Tooltip = new List<TooltipLine>()
                    };

                    void WriteGroup((string subtype, int amount) item)
                    {
                        symbol.IconGroups.Add(new MyObjectBuilder_EquiSymbolsMapLayerDefinition.MyObjectBuilder_Symbol.IconGroup
                        {
                            Scale = item.amount > total / 2 ? 1f : 0.5f,
                            Icons = new List<string> { subtypeMapping[item.subtype].icon }
                        });
                    }

                    for (var i = 1; i < parsedActions.Count; i += 2)
                        WriteGroup(parsedActions[i]);
                    for (var i = (parsedActions.Count - 1) & ~1; i >= 0; i -= 2)
                        WriteGroup(parsedActions[i]);

                    symbol.Tooltip.Add(new TooltipLine { Content = "Producing..." });
                    var intervalsPerHour = TimeSpan.FromHours(1).Ticks / (double)interval.Ticks;
                    foreach (var parsedAction in parsedActions)
                    {
                        var (format, _, scaling) = subtypeMapping[parsedAction.subtype];
                        var scaledAmount = parsedAction.amount * intervalsPerHour * scaling;
                        symbol.Tooltip.Add(new TooltipLine { Content = $" {string.Format(format, scaledAmount)}" });
                    }

                    symbol.Tooltip.Add(new TooltipLine { Content = " ..per hour" });
                    lock (definition)
                    {
                        definition.Symbols.Add(symbol);
                    }

                    break;
                }
            });

        var result = ((IMyUtilities)MyAPIUtilities.Static).SerializeToXML(definition);
        result = Regex.Replace(result, "\\s+<\\w+ xsi:nil=\"true\" \\/>", string.Empty);
        Console.WriteLine(result);
    }
}