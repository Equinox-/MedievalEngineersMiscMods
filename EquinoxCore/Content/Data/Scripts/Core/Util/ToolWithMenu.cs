using Medieval.GUI.ContextMenu;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.Input;
using VRage.Input.Input;
using VRage.Utils;

namespace Equinox76561198048419394.Core.Util
{
    public interface IToolWithMenu
    {
        MyEntity Holder { get; }
        string ToolContextMenuId { get; }
        object[] ToolContextMenuArguments { get; }
    }

    public static class ToolWithMenuExt
    {
        private static readonly MyInputContext InputContext = new MyInputContext("Tools Menu");

        static ToolWithMenuExt()
        {
            InputContext.RegisterAction(MyStringHash.GetOrCompute("ToolMenu"), MyInputStateFlags.Pressed,
                (ref MyInputContext.ActionEvent evt) =>
                {
                    var menuHolder = MyAPIGateway.Session?.ControlledObject?.GetHeldBehavior() as IToolWithMenu;
                    menuHolder?.ToggleMenu();
                });
        }

        public static void OnActivateWithMenu(this IToolWithMenu tool)
        {
            if (MyAPIGateway.Session?.ControlledObject == tool.Holder)
                InputContext.Push();
        }

        public static void OnDeactivateWithMenu(this IToolWithMenu tool)
        {
            if (MyAPIGateway.Session?.ControlledObject == tool.Holder)
                InputContext.Pop();
        }

        public static MyContextMenu CurrentlyOpenMenu(this IToolWithMenu tool) => MyContextMenuScreen.GetContextMenu(tool.ToolContextMenuId);

        public static void ToggleMenu(this IToolWithMenu tool)
        {
            var open = tool.CurrentlyOpenMenu();
            if (open != null)
                open.Close();
            else
                MyContextMenuScreen.OpenMenu(tool.Holder, tool.ToolContextMenuId, tool.ToolContextMenuArguments);
        }

        public static void OpenMenu(this IToolWithMenu tool)
        {
            tool.CloseMenu();
            MyContextMenuScreen.OpenMenu(tool.Holder, tool.ToolContextMenuId, tool.ToolContextMenuArguments);
        }

        public static void CloseMenu(this IToolWithMenu tool) => tool.CurrentlyOpenMenu()?.Close();
    }
}