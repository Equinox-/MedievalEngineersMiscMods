using System.Collections.Generic;
using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Gui;
using Sandbox.Gui.Skins;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Session;
using VRage.Utils;

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