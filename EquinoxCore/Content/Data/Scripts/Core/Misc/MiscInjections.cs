using System.Collections.Generic;
using Medieval.GUI.Ingame.Crafting;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI.Controls;
using Sandbox.Graphics.GUI;
using Sandbox.Gui.Layouts;
using Sandbox.Gui.Skins;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.Misc
{
    [MySessionComponent(AlwaysOn = true)]
    public class MiscInjections : MySessionComponent
    {
        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            InjectSkinStyles();
            AddScheduledCallback(FirstUpdate);
            MyAPIGateway.GuiControlCreated += OnControlCreated;
        }


        protected override void OnUnload()
        {
            MyAPIGateway.GuiControlCreated -= OnControlCreated;
            base.OnUnload();
        }

        private void OnControlCreated(object obj)
        {
            if (obj?.GetType().Name != "MyCraftingScreen") return;
            VisitControlsRecursive(obj);
        }

        private static void VisitControlsRecursive(object ctl)
        {
            switch (ctl.GetType().Name)
            {
                // Allow the additional inventories on the side of crafting screens to expand vertically to accommodate inventories with more than 3 slots.
                case "MyAdditionalInventoryControl" when ctl is MyGuiControlParent additionalInventory:
                {
                    var padding = (ctl as MyDecoratedPanel)?.Padding ?? default;
                    var layout = additionalInventory.Layout = new MyVerticalLayoutBehavior(padding: padding);
                    // Apply layout's computed size to properly update the size.  Note that the vertical layout behavior doesn't include vertical padding.
                    additionalInventory.Size = layout.ComputedSize + new Vector2(0, padding.VerticalSum);
                    // Re-position the elements, this time based on the newly computed size.
					layout.RefreshLayout();
                    return;
                }
            }
            if (ctl is IMyGuiControlsParent parent)
                foreach (var child in parent.Controls)
                    VisitControlsRecursive(child);
        }

        [Update(false)]
        private void FirstUpdate(long dt)
        {
            InjectCameraReset();
        }

        private void InjectCameraReset()
        {
            var hud = MyGuiScreenHudBase.Static;
            if (hud == null || MyAPIGateway.Session.SessionSettings.Enable3rdPersonView) return;
            var context = hud.InputContext;
            context.RegisterAction(MyStringHash.GetOrCompute("CameraSwitch"), () =>
            {
                MyHud.Crosshair.Recenter();
                var controlled = MyAPIGateway.Session.ControlledObject?.Get<MyCharacterMovementComponent>();
                if (controlled != null)
                    controlled.HeadYaw = 0;
            });
            context.Pop();
            context.Push();
        }

        private void InjectSkinStyles()
        {
            var modContrib = MyDefinitionManager.Get<MyGuiSkinDefinition>("ModContributions");
            if (modContrib == null)
                return;
            foreach (var skin in MyGuiSkinManager.Static.AvailableSkins.Values)
            {
                if (skin == modContrib)
                    continue;
                InjectModIcons(modContrib, skin);
            }
        }

        private static void InjectModIcons(MyGuiSkinDefinition modded, MyGuiSkinDefinition target)
        {
            Inject(modded.FontStyles, target.FontStyles);
            Inject(modded.Textures, target.Textures);
            Inject(modded.IconStyles, target.IconStyles);
            Inject(modded.ButtonStyles, target.ButtonStyles);
            Inject(modded.ComboboxStyles, target.ComboboxStyles);
            Inject(modded.LabelStyles, target.LabelStyles);
            Inject(modded.CheckboxStyles, target.CheckboxStyles);
            Inject(modded.SliderStyles, target.SliderStyles);
            Inject(modded.ListboxStyles, target.ListboxStyles);
            Inject(modded.TextboxStyles, target.TextboxStyles);
            Inject(modded.ImageStyles, target.ImageStyles);
            Inject(modded.GridStyles, target.GridStyles);
            Inject(modded.TableStyles, target.TableStyles);
            Inject(modded.ContextMenuStyles, target.ContextMenuStyles);
            Inject(modded.ButtonListStyles, target.ButtonListStyles);
        }

        private static void Inject<TK, TV>(Dictionary<TK, TV> from, Dictionary<TK, TV> to)
        {
            foreach (var kv in from)
                if (!to.ContainsKey(kv.Key))
                    to[kv.Key] = kv.Value;
        }
    }
}