using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Input;
using VRage.Session;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Debug
{
    [MySessionComponent(AlwaysOn = true)]
    public class ModDebugScreenRegistration : MySessionComponent
    {
        private MyInputContext _ctx;

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
                _screen.CloseScreen();
                _screen = null;
            }

            if (ActiveDebugScreen != null)
            {
                ActiveDebugScreen.CloseScreen();
                ActiveDebugScreen = null;
            }

            if (_ctx != null && _ctx.InStack)
                _ctx.Pop();
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
                _screen.CloseScreen();
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
                    callback: result =>
                    {
                        if (result == MyDialogResult.Yes)
                            ShowModDebugScreenInternal();
                    });
        }

        private void ShowModDebugScreenInternal()
        {
            _screen = new GuiScreenDebugMods(this);
            MyGuiSandbox.AddScreen(_screen);
            _screen.Closed += (screen) => _screen = null;
        }

        internal MyGuiScreenDebugBase ActiveDebugScreen;

        [FixedUpdate(true)]
        public void UpdateTextBoxes()
        {
            var active = ActiveDebugScreen;
            if (active == null || !active.Visible) return;
            if (!(active.FocusedControl is MyGuiControlTextbox textBox)) return;
            if (textBox.HasFocus) return;
            if (!textBox.CheckMouseOver()) return;
            var delta = MyAPIGateway.Input?.TextInput ?? default;
            if (delta.Count == 0) return;

            var text = new StringBuilder(textBox.Text);
            foreach (var ch in delta)
            {
                if (!char.IsControl(ch))
                    text.Append(ch);
                else if (ch == '\b' && text.Length > 0)
                    text = text.Remove(text.Length - 1, 1);
            }

            textBox.SetText(text);
        }
    }
}