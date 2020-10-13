using System.Collections.Generic;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Input.Devices.Keyboard;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Debug
{
    public class GuiScreenDebugMods : MyGuiScreenDebugBase
    {
        private MyGuiScreenDebugBase _activeScreen;
        private readonly ModDebugScreenRegistration _container;

        public GuiScreenDebugMods(ModDebugScreenRegistration container)
            : base(new Vector2(.5f, .5f), new Vector2(0.35f, 1.0f), 0.35f * Color.Yellow.ToVector4(), true)
        {
            _container = container;
            m_backgroundColor = null;
            EnabledBackgroundFade = true;
            m_grabInputFocus = true;
            RecreateControls(true);
            m_inputContext.ConsumesAllInput = true;
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            AddCaption("Mods developer screen", Color.Yellow.ToVector4(), new Vector2(0, .05f));

            m_scale = 0.9f;
            m_closeOnEsc = true;

            m_currentPosition = -m_size.Value / 2.0f + new Vector2(0.03f, 0.1f);

            var checkboxList = new List<MyGuiControlBase>();
            foreach (var screen in MySession.Static.Components.GetAll<ModDebugScreenComponent>())
                AddGroupBox(screen.FriendlyName, screen, checkboxList);
        }

        private void AddGroupBox(string text, ModDebugScreenComponent screenType, List<MyGuiControlBase> controlGroup)
        {
            var box = AddCheckBox(text, true, null, controlGroup: controlGroup);
            box.IsChecked = false;

            box.OnCheckedChanged += sender =>
            {
                if (sender.IsChecked)
                {
                    foreach (var myGuiControlBase in controlGroup)
                        if (myGuiControlBase != sender && myGuiControlBase is MyGuiControlCheckbox chb)
                            chb.IsChecked = false;

                    var newScreen = screenType.Construct();
                    newScreen.Closed += source => box.IsChecked = false;
                    _activeScreen?.CloseScreen();
                    _container.AddScreen(newScreen);
                    _activeScreen = newScreen;
                }
                else if (_activeScreen != null && _activeScreen.GetType() == screenType.ScreenType)
                {
                    _container.RemoveScreen(_activeScreen);
                    _activeScreen = null;
                }
            };
        }

        public override string GetFriendlyName() => nameof(GuiScreenDebugMods);

        public override void HandleInput(bool receivedFocusInThisUpdate)
        {
            base.HandleInput(receivedFocusInThisUpdate);

            if (MyAPIGateway.Input != null && MyAPIGateway.Input.IsKeyPressed(MyKeys.F12)) CloseScreen();
        }

        private void RemoveActiveScreen()
        {
            if (_activeScreen == null) return;
            _container.RemoveScreen(_activeScreen);
            _activeScreen = null;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            RemoveActiveScreen();
        }
    }
}