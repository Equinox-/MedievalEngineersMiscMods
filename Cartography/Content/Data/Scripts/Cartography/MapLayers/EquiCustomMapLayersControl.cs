using System.Collections.Generic;
using System.Linq;
using Medieval.GUI.Ingame.Map;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using VRage.Game;
using VRage.Logging;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Cartography.MapLayers
{
    public class EquiCustomMapLayersControl : MyGuiControlParent
    {
        private readonly MyMapScreen _owner;

        private MyTooltip _boundTooltip;
        private ICustomMapLayer _boundTooltipOwner;
        private int _boundTooltipDelay;

        public EquiCustomMapLayersControl(MyMapScreen screen) : base(position: new Vector2(-.28f, .35f), size: new Vector2(.32f, .05f))
        {
            _owner = screen;
            Layout = new MyHorizontalLayoutBehavior();
            var buttonSize = new Vector2(50, 50) / MyGuiConstants.GUI_OPTIMAL_SIZE;
            var space = new MyGuiControlLabel(size: buttonSize)
            {
                LayoutStyle = MyGuiControlLayoutStyle.DynamicX
            };
            Controls.Add(space);
            var mapControl = _owner.MapControl;
            foreach (var definition in MyDefinitionManager.GetOfType<EquiCustomMapLayerDefinition>().OrderBy(x => x.Order))
            {
                // exclude non-public definitions that aren't part of a local mod
                if (!definition.Public && !(definition.Package is MyModContext mod && mod.DataPath.Contains("AppData")))
                    continue;

                ICustomMapLayer kingdomLayer = null;
                if (definition.IsSupported(mapControl.Planet, MyPlanetMapZoomLevel.Kingdom))
                {
                    kingdomLayer = definition.CreateLayer(mapControl, mapControl.KingdomView);
                    kingdomLayer.Visible = definition.VisibleByDefaultInKingdoms;
                    mapControl.KingdomView.AddLayer(kingdomLayer);
                }

                ICustomMapLayer regionLayer = null;
                if (definition.IsSupported(mapControl.Planet, MyPlanetMapZoomLevel.Region))
                {
                    regionLayer = definition.CreateLayer(mapControl, mapControl.RegionView);
                    regionLayer.Visible = definition.VisibleByDefaultInRegions;
                    mapControl.RegionView.AddLayer(regionLayer);
                }

                if (kingdomLayer == null && regionLayer == null)
                {
                    MyLog.DefaultLogger.Warning($"Custom map layer {definition.Id} isn't supported for {mapControl.Planet.DefinitionId}");
                    continue;
                }

                var button = new CustomMapLayerButton(definition)
                {
                    Size = buttonSize,
                    CanHaveFocus = false,
                };
                button.SetToolTip(definition.DescriptionEnum ?? MyStringId.GetOrCompute(definition.DescriptionString));
                button.ApplyStyle(MyStringId.GetOrCompute("MapButtonDefault"));
                button.LayoutStyle = MyGuiControlLayoutStyle.Fixed;
                Controls.Add(button);
                button.ButtonClicked += _ =>
                {
                    var wasVisible = (kingdomLayer?.Visible ?? false) || (regionLayer?.Visible ?? false);
                    var desired = !wasVisible;
                    button.Checked = desired;
                    if (kingdomLayer != null)
                        kingdomLayer.Visible = desired;
                    if (regionLayer != null)
                        regionLayer.Visible = desired;
                };
            }
        }

        public static EquiCustomMapLayersControl CustomLayers(MyPlanetMapControl control)
        {
            IMyGuiControlsOwner test = control;
            while (test != null)
            {
                if (test is MyMapScreen screen)
                {
                    foreach (var child in screen.Controls)
                        if (child is EquiCustomMapLayersControl custom)
                            return custom;
                }

                test = test.Owner;
            }

            return null;
        }
        
        public override void Draw(float transitionAlpha, float backgroundTransitionAlpha)
        {
            base.Draw(transitionAlpha, backgroundTransitionAlpha);

            if (_boundTooltip != null
                && _boundTooltipOwner.Visible
                && _boundTooltipOwner.BoundTo == _owner.MapControl.CurrentView
                && MyGuiManager.TotalTimeInMilliseconds > _boundTooltipDelay)
            {
                var timeToFullAlpha = _boundTooltipDelay + MyGuiConstants.SHOW_ALPHA_ANIMATION_TOOLTIP_TIME - MyGuiManager.TotalTimeInMilliseconds;
                var alphaModifier = (float)timeToFullAlpha / (MyGuiConstants.SHOW_ALPHA_ANIMATION_TOOLTIP_TIME);

                alphaModifier = 1 - alphaModifier;
                alphaModifier = MathHelper.Clamp(alphaModifier, 0f, 1f);
                _boundTooltip.Draw(MyGuiManager.MouseCursorPosition, alphaModifier);
            }
        }

        public void BindTooltip(ICustomMapLayer owner, MyTooltip tooltip)
        {
            if (tooltip == null && _boundTooltipOwner == owner)
            {
                _boundTooltip = null;
                _boundTooltipOwner = null;
                return;
            }
            if (_boundTooltip != tooltip)
                _boundTooltipDelay = MyGuiManager.TotalTimeInMilliseconds + MyGuiConstants.SHOW_CONTROL_TOOLTIP_DELAY;
            _boundTooltip = tooltip;
            _boundTooltipOwner = owner;
        }

        private sealed class CustomMapLayerButton : MyGuiControlImageButton
        {
            private readonly EquiCustomMapLayerDefinition _definition;

            public CustomMapLayerButton(EquiCustomMapLayerDefinition definition)
            {
                _definition = definition;
            }

            public override void Draw(float transitionAlpha, float backgroundTransitionAlpha)
            {
                base.Draw(transitionAlpha, backgroundTransitionAlpha);
                if (_definition.Icons == null || _definition.Icons.Length == 0)
                    return;
                var styleDefinition = CurrentStyle;
                var topLeft = GetPositionAbsoluteTopLeft() + styleDefinition.Padding.TopLeftOffset;
                var iconSize = Size - styleDefinition.Padding.SizeChange;
                var iconCenter = GetPositionAbsoluteCenter();

                if (styleDefinition.Icon.Margin.HasValue)
                {
                    iconSize = Size - styleDefinition.Padding.SizeChange - styleDefinition.Icon.Margin.Value.SizeChange;
                    iconCenter = topLeft + styleDefinition.Icon.Margin.Value.TopLeftOffset;
                    iconCenter += iconSize * 0.5f;
                }

                var posPx = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(iconCenter - 0.5f * iconSize);
                var sizePx = MyGuiManager.GetScreenSizeFromNormalizedSize(iconSize);

                var destination = new RectangleF(posPx, sizePx);
                Rectangle? source = null;
                var color = ApplyColorMaskModifiers(
                    styleDefinition.Icon.Color ?? ColorMask,
                    Enabled, transitionAlpha * (ColorMask.A / 255f) * Alpha);
                foreach (var icon in _definition.Icons)
                    MyRenderProxy.DrawSprite(icon, ref destination, false,
                        ref source, color, 0, Vector2.UnitX, ref Vector2.Zero, VRageRender.SpriteEffects.None, 0);
            }
        }

        public static void BindTo(MyMapScreen screen)
        {
            var control = new EquiCustomMapLayersControl(screen);
            screen.Controls.Add(control);
        }
    }
}