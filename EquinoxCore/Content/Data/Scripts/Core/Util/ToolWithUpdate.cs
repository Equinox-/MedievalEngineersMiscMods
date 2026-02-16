using Sandbox.ModAPI;
using VRage.Game.Entity;

namespace Equinox76561198048419394.Core.Util
{
    public interface IToolWithUpdate
    {
        MyEntity Holder { get; }

        /// <summary>
        /// Called every frame on the game of the tool's holder.
        /// </summary>
        void Update();
    }

    public static class ToolWithUpdateExt
    {
        public static void OnActivateWithUpdate(this IToolWithUpdate tool)
        {
            if (MyAPIGateway.Session?.ControlledObject == tool.Holder)
                tool.Holder?.Scene.Scheduler.AddFixedUpdate(tool.Update);
        }

        public static void OnDeactivateWithUpdate(this IToolWithUpdate tool)
        {
            if (MyAPIGateway.Session?.ControlledObject == tool.Holder)
                tool.Holder?.Scene.Scheduler.RemoveFixedUpdate(tool.Update);
        }
    }
}