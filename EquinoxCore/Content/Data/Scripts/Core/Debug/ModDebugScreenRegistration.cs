using System.Collections.Generic;
using Sandbox.Game.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Game.Input;
using VRage.Input;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Debug
{
    [MySessionComponent(AlwaysOn = true)]
    public class ModDebugScreenRegistration : MySessionComponent
    {
        private MyInputContext _ctx;
        private readonly List<MyGuiScreenBase> _screens = new List<MyGuiScreenBase>();

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            _ctx = new MyInputContext("ModDebugScreenRegistration");
            _ctx.RegisterAction(MyStringHash.GetOrCompute("DeveloperScreenMods"), ShowModDebugScreen);
            if (!_ctx.InStack)
                _ctx.Push();
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            if (_screen != null)
            {
                RemoveScreen(_screen);
                _screen = null;
            }

            if (_ctx != null && _ctx.InStack)
                _ctx.Pop();

            foreach (var screen in _screens)
            {
                screen.CloseScreenNow();
                MyGuiSandbox.RemoveScreen(screen);
            }
            _screens.Clear();
        }

        private GuiScreenDebugMods _screen;

        private void ShowModDebugScreen(ref MyInputContext.ActionEvent action)
        {
            // Disable developer screen on public unless they are in a creative game
            if (MyAPIGateway.Input == null || !MyAPIGateway.Input.ENABLE_DEVELOPER_KEYS)
            {
                if (MySession.Static == null || !MyAPIGateway.Session.CreativeMode)
                    return;
            }

            if (_screen != null)
            {
                RemoveScreen(_screen);
                _screen = null;
                return;
            }

            if (MyAPIGateway.Input != null && MyAPIGateway.Input.ENABLE_DEVELOPER_KEYS)
                ShowModDebugScreenInternal();
            else
                MyMessageBox.Show(
                    MyCommonTexts.MessageBoxTextF12Question,
                    MyCommonTexts.MessageBoxF12Question,
                    MyMessageBoxButtons.YesNo,
                    MyMessageBoxIcon.Question,
                    callback: (result) =>
                    {
                        if (result == MyDialogResult.Yes)
                            ShowModDebugScreenInternal();
                    });
        }

        private void ShowModDebugScreenInternal()
        {
            _screen = new GuiScreenDebugMods(this);
            AddScreen(_screen);
            _screen.Closed += (screen) => _screen = null;
        }

        public void AddScreen(MyGuiScreenBase screen)
        {
            if (!Loaded)
                return;
            MyGuiSandbox.AddScreen(screen);
            _screens.Add(screen);
        }

        public void RemoveScreen(MyGuiScreenBase screen)
        {
            Scheduler.AddScheduledCallback(dt =>
            {
                screen.CloseScreenNow();
                MyGuiSandbox.RemoveScreen(screen);
            }, 0);
            _screens.Remove(screen);
        }
    }
}