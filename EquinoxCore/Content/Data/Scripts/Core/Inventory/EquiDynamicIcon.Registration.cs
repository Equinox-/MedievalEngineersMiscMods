using System;
using System.Collections.Generic;
using Medieval.Inventory.Items;
using Medieval.ObjectBuilders.Definitions.Inventory;
using Medieval.ObjectBuilders.Items;
using Sandbox.Definitions.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Controls;
using Sandbox.Gui.Styles;
using Sandbox.Gui.Utility;
using VRage.Components;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders;
using VRage.ObjectBuilders.Inventory;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Inventory
{
    [MySessionComponent(AlwaysOn = true, AllowAutomaticCreation = true)]
    public class EquiDynamicIconRegistration : MySessionComponent
    {
        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            // Unfortunately the BaseType of System.Type is not whitelisted.
            var knownSubtypes = new Dictionary<Type, Type>
            {
                [typeof(MyObjectBuilder_SeedBagHandItem)] = typeof(MyObjectBuilder_HandItem),
                [typeof(MyObjectBuilder_HandItem)] = typeof(MyObjectBuilder_EquipmentItem),
                [typeof(MyObjectBuilder_DurableItem)] = typeof(MyObjectBuilder_InventoryItem),
                [typeof(MyObjectBuilder_TreasureMapItem)] = typeof(MyObjectBuilder_EquipmentItem),
                [typeof(MyObjectBuilder_BlockItem)] = typeof(MyObjectBuilder_PhysicalObject),
                [typeof(MyObjectBuilder_PhysicalObject)] = typeof(MyObjectBuilder_InventoryItem),
                [typeof(MyObjectBuilder_QuestItem)] = typeof(MyObjectBuilder_UsableItem),
                [typeof(MyObjectBuilder_SchematicItem)] = typeof(MyObjectBuilder_UsableItem),
                [typeof(MyObjectBuilder_ConsumableItem)] = typeof(MyObjectBuilder_UsableItem),
                [typeof(MyObjectBuilder_UsableItem)] = typeof(MyObjectBuilder_PhysicalObject),
                [typeof(MyObjectBuilder_ProjectileItem)] = typeof(MyObjectBuilder_InventoryItem),
            };

            foreach (var mapping in MyDefinitionManager.GetOfType<MyItemRendererMappingDefinition>())
            foreach (var dynamicIcon in MyDefinitionManager.GetOfType<EquiDynamicIconDefinition>())
                if (!mapping.ItemRenderers.ContainsKey(dynamicIcon.Id))
                {
                    var delegateRenderer = GetRendererNameFor(mapping, dynamicIcon.Id);
                    var dynamicRenderer = EquiDynamicIconRenderer.DynamicIconRendererPrefix + delegateRenderer;
                    if (MyItemRendererFactory.TryGetRenderer(dynamicRenderer, out _))
                        MyItemRendererFactory.Assign(mapping.Id, dynamicIcon.Id, dynamicRenderer);
                    else
                        this.GetLogger()
                            .Warning($"Item renderer type {delegateRenderer} has no corresponding dynamic renderer {dynamicRenderer} registered");
                }

            return;


            // Mostly a copy of MyItemRendererFactory.GetRenderer. Anywhere this doesn't work will require 
            string GetRendererNameFor(MyItemRendererMappingDefinition mapping, MyDefinitionId item)
            {
                if (mapping.ItemRenderers.TryGetValue(item, out var renderer))
                    return renderer;
                Type type = item.TypeId;
                do
                {
                    if (mapping.ItemRenderers.TryGetValue(new MyDefinitionId(type), out renderer))
                        return renderer;
                } while (knownSubtypes.TryGetValue(type, out type));

                return "ItemBase";
            }
        }
    }

    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "AbstractBase")]
    public abstract class EquiDynamicIconRenderer : MyGridItemRendererBase
    {
        public const string DynamicIconRendererPrefix = "EquiDynamicIcon_";
        private readonly MyGridItemRendererBase _delegate;
        private static readonly MyFontStyle InvisibleFontStyle = MyFontStyle.Default.Clone(size: 0, color: Color.Transparent);

        protected EquiDynamicIconRenderer(string delegateName) => _delegate = MyItemRendererFactory.GetRenderer(delegateName);

        public override void Draw(MyGrid.Item item, MyGrid.GridItemState state, RectangleF itemRect, Color colormask, MyStateBase style, float transitionAlpha)
        {
            var inventoryItem = item.UserData as MyInventoryItem;
            if (inventoryItem == null || !MyDefinitionManager.TryGet(inventoryItem.DefinitionId, out EquiDynamicIconDefinition def))
            {
                _delegate.Draw(item, state, itemRect, colormask, style, transitionAlpha);
                return;
            }

            var originalIcons = item.Icons;
            var originalFontStyle = style.Font;
            string textOverride = null;

            if (def.TryGetDynamicIcons(inventoryItem, out var dynamicIcons))
                item.Icons = dynamicIcons;

            if (def.TryGetDynamicLabel(inventoryItem, out var dynamicLabel))
            {
                textOverride = dynamicLabel;
                style.Font = InvisibleFontStyle;
            }

            _delegate.Draw(item, state, itemRect, colormask, style, transitionAlpha);

            style.Font = originalFontStyle;
            item.Icons = originalIcons;

            if (textOverride != null)
            {
                var enabled = item.Enabled && state != MyGrid.GridItemState.Disabled;
                var font = style.Font;
                var normalizedCoord = itemRect.Position + (new Vector2(0.0f, itemRect.Size.Y) - MyGuiConstants.DEFAULT_ITEM_COUNT_OFFSET);
                var color = ApplyColorMaskModifiers(font.Color, enabled, transitionAlpha);
                MyFontHelper.DrawString(font.Font, textOverride, normalizedCoord, font.Size, color,
                    MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM, maxTextWidth: itemRect.Size.X);
            }
        }
    }

// ReSharper disable InconsistentNaming
    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "InventoryItem")]
    public sealed class EquiDynamicIconRenderer_InventoryItem : EquiDynamicIconRenderer
    {
        public EquiDynamicIconRenderer_InventoryItem() : base("InventoryItem")
        {
        }
    }

    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "DurableItem")]
    public sealed class EquiDynamicIconRenderer_DurableItem : EquiDynamicIconRenderer
    {
        public EquiDynamicIconRenderer_DurableItem() : base("DurableItem")
        {
        }
    }

    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "EquipmentItem")]
    public sealed class EquiDynamicIconRenderer_EquipmentItem : EquiDynamicIconRenderer
    {
        public EquiDynamicIconRenderer_EquipmentItem() : base("EquipmentItem")
        {
        }
    }

    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "ToolbarInventoryItem")]
    public sealed class EquiDynamicIconRenderer_ToolbarInventoryItem : EquiDynamicIconRenderer
    {
        public EquiDynamicIconRenderer_ToolbarInventoryItem() : base("ToolbarInventoryItem")
        {
        }
    }

    [MyItemRendererDescriptor(DynamicIconRendererPrefix + "ToolbarEquipmentItem")]
    public sealed class EquiDynamicIconRenderer_ToolbarEquipmentItem : EquiDynamicIconRenderer
    {
        public EquiDynamicIconRenderer_ToolbarEquipmentItem() : base("ToolbarEquipmentItem")
        {
        }
    }
}