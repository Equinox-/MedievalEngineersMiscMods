using System;
using Sandbox.Game.Gui;
using VRage.Session;

namespace Equinox76561198048419394.Core.Debug
{
    public abstract class ModDebugScreenComponent : MySessionComponent
    {
        public abstract string FriendlyName { get; }

        public abstract Type ScreenType { get; }

        public abstract MyGuiScreenDebugBase Construct();
    }
}