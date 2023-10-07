using Medieval.GUI.ContextMenu;
using Sandbox.Graphics.GUI;

namespace Equinox76561198048419394.Core.UI
{
    internal interface IControlHolder
    {
        MyGuiControlBase Root { get; }

        void SyncToControl();

        void SyncFromControl();
    }

    internal abstract class ControlFactory
    {
        public abstract IControlHolder Create(MyContextMenuController ctl);
    }
}