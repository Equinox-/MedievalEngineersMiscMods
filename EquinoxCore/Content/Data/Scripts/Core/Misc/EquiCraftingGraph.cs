using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.Block;
using Medieval.Definitions.Crafting;
using Medieval.Definitions.Inventory;
using Medieval.Entities.Components.Crafting.Recipes;
using Sandbox.ModAPI;
using VRage;
using VRage.Definitions.Block;
using VRage.Definitions.Inventory;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Equinox76561198048419394.Core.Misc
{
    public class EquiCraftingGraph : IDisposable
    {
        private readonly bool _blocks;
        private readonly bool _collapseTags;
        private readonly bool _collapseRecipes;

        private readonly TextWriter _edgeWriter;
        private readonly TextWriter _nodeWriter;

        private readonly Dictionary<MyVisualDefinitionBase, string> _nodes =
            new Dictionary<MyVisualDefinitionBase, string>(ReferenceEqualityComparer<MyVisualDefinitionBase>.Instance);

        private readonly Dictionary<MyVisualDefinitionBase, MyVisualDefinitionBase> _substitute =
            new Dictionary<MyVisualDefinitionBase, MyVisualDefinitionBase>(ReferenceEqualityComparer<MyVisualDefinitionBase>.Instance);


        public EquiCraftingGraph(string[] args)
        {
            _blocks = args.Contains("block") || args.Contains("blocks");
            _collapseTags = args.Contains("collapse-tags");
            _collapseRecipes = args.Contains("collapse-recipes");
            _nodeWriter = ((IMyUtilities)MyAPIUtilities.Static).WriteFileInGlobalStorage("crafting-graph.nodes.csv");
            _edgeWriter = ((IMyUtilities)MyAPIUtilities.Static).WriteFileInGlobalStorage("crafting-graph.edges.csv");
            _nodeWriter.WriteLine("id;type;label");
            _edgeWriter.WriteLine("source;target;type;value");
        }

        public void Export()
        {
            IndexCollapse();

            foreach (var item in MyDefinitionManager.GetOfType<MyInventoryItemDefinition>())
                WriteNode(item);
            foreach (var tag in MyDefinitionManager.GetOfType<MyItemTagDefinition>())
            {
                var tagId = WriteNode(tag);
                foreach (var item in tag.Items)
                    if (_nodes.TryGetValue(item, out var itemId) && itemId != tagId)
                        WriteEdge(itemId, tagId, "tagged", "1");
            }

            foreach (var recipe in MyDefinitionManager.GetOfType<MyCraftingRecipeDefinition>())
                WriteRecipe(recipe);

            foreach (var block in MyDefinitionManager.GetOfType<MyBuildableBlockDefinition>())
                if (_blocks && !(block is MyGeneratedBlockDefinition) && Substitute(block) == block)
                {
                    var blockId = WriteNode(block);
                    foreach (var item in block.Components)
                        if (TryGetItemId(item.Id, out var itemId))
                            WriteEdge(itemId, blockId, "component", $"{item.Count}");

                    if (!_collapseRecipes && MyDefinitionManager.TryGet(block.Id, out MyContainerDefinition container))
                    {
                        var crafting = container.Get<MyConstantRecipeProviderComponentDefinition>();
                        if (crafting != null)
                            foreach (var recipe in crafting.Recipes)
                                if (_nodes.TryGetValue(recipe, out var recipeId))
                                    WriteEdge(blockId, recipeId, "crafter", "0");
                    }
                }

            if (!_collapseRecipes)
                foreach (var item in MyDefinitionManager.GetOfType<MyToolheadItemDefinition>())
                    if (_nodes.TryGetValue(item, out var itemId) && item.CraftingCategory != null)
                        foreach (var recipe in item.CraftingCategory.Recipes)
                            if (_nodes.TryGetValue(recipe, out var recipeId))
                                WriteEdge(itemId, recipeId, "toolhead", "0");
        }

        private void WriteRecipe(MyCraftingRecipeDefinition def)
        {
            var inputs = new Dictionary<string, int>();
            var outputs = new Dictionary<string, int>();
            foreach (var item in def.Prerequisites)
                if (TryGetItemId(item.Id, out var itemId))
                    inputs[itemId] = inputs.GetValueOrDefault(itemId, 0) + item.Amount;
                else return;
            foreach (var item in def.Results)
                if (TryGetItemId(item.Id, out var itemId))
                    outputs[itemId] = outputs.GetValueOrDefault(itemId, 0) + item.Amount;
                else return;

            if (_collapseRecipes)
            {
                foreach (var output in outputs)
                foreach (var input in inputs)
                    WriteEdge(input.Key, output.Key, "recipe", $"{input.Value / (float)output.Value}");
            }
            else
            {
                var recipeNode = WriteNode(def);
                foreach (var item in inputs)
                    WriteEdge(item.Key, recipeNode, "ingredient", $"{item.Value}");
                foreach (var item in outputs)
                    WriteEdge(recipeNode, item.Key, "output", $"{item.Value}");
            }
        }

        private bool TryGetItemId(MyDefinitionId id, out string nodeId)
        {
            if (MyDefinitionManager.TryGet(id, out MyItemTagDefinition tag))
                return _nodes.TryGetValue(tag, out nodeId);
            if (MyDefinitionManager.TryGet(id, out MyInventoryItemDefinition item))
                return _nodes.TryGetValue(item, out nodeId);
            nodeId = null;
            return false;
        }

        private void IndexCollapse()
        {
            foreach (var variant in MyDefinitionManager.GetOfType<MyBlockVariantsDefinition>())
            foreach (var block in variant.Blocks)
                if (block.GridDataDefinitionId.SubtypeName.Contains("Small"))
                    _substitute[block] = MyInventoryBlockItem.GetOrCreateDefinition(variant.Blocks[0].Id);

            foreach (var block in MyDefinitionManager.GetOfType<MyBlockDefinition>())
                if (!_substitute.ContainsKey(block) && block.GridDataDefinitionId.SubtypeName.Contains("Small"))
                    _substitute[block] = MyInventoryBlockItem.GetOrCreateDefinition(block.Id);

            if (_collapseTags)
                foreach (var tag in MyDefinitionManager.GetOfType<MyItemTagDefinition>())
                foreach (var item in tag.Items)
                    if (!_substitute.TryGetValue(item, out var existing)
                        // Substitute with the most narrow tag possible.
                        || (existing is MyItemTagDefinition existingTag && existingTag.Items.Count > tag.Items.Count))
                        _substitute[item] = tag;
        }

        private static string NodeId(MyDefinitionId def) => def.TypeId.ShortName + "_" + def.SubtypeName.AsAlphaNumeric();

        private MyVisualDefinitionBase Substitute(MyVisualDefinitionBase def)
        {
            while (_substitute.TryGetValue(def, out var newDef))
                def = newDef;
            return def;
        }

        private string WriteNode(MyVisualDefinitionBase def)
        {
            def = Substitute(def);
            if (_nodes.TryGetValue(def, out var id))
                return id;
            id = NodeId(def.Id);
            _nodeWriter.WriteLine($"{id};{def.Id.TypeId.ShortName};{def.DisplayNameText}");
            _nodes.Add(def, id);
            return id;
        }

        private void WriteEdge(string from, string to, string type, string value)
        {
            _edgeWriter.WriteLine($"{from};{to};{type};{value}");
        }

        public void Dispose()
        {
            _nodeWriter.Dispose();
            _edgeWriter.Dispose();
        }
    }
}