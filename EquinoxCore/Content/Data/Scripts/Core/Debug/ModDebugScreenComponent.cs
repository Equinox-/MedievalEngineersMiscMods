using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Gui;
using VRage.Session;

namespace Equinox76561198048419394.Core.Debug
{
    public abstract class ModDebugScreenComponent : MySessionComponent
    {
        public virtual string FriendlyName => null;

        public virtual Type ScreenType => null;

        public virtual MyGuiScreenDebugBase Construct() => null;

        protected virtual IEnumerable<DebugScreen> ScreensInternal => Enumerable.Empty<DebugScreen>();

        public IEnumerable<DebugScreen> Screens
        {
            get
            {
                foreach (var screen in ScreensInternal)
                    yield return screen;
                var legacy = new DebugScreen(FriendlyName, ScreenType, Construct);
                if (legacy.FriendlyName != null && legacy.ScreenType != null)
                    yield return legacy;
            }
        }

        protected static DebugScreen CreateDebugScreen<T>(string friendlyName = null) where T : MyGuiScreenDebugBase, new() =>
            new DebugScreen(friendlyName ?? typeof(T).Name, typeof(T), () => new T());

        public struct DebugScreen
        {
            public string FriendlyName;
            public Type ScreenType;
            public Func<MyGuiScreenDebugBase> Construct;

            public DebugScreen(string friendlyName, Type screenType, Func<MyGuiScreenDebugBase> construct)
            {
                FriendlyName = friendlyName;
                ScreenType = screenType;
                Construct = construct;
            }
        }
    }
}