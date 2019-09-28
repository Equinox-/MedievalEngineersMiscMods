using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

// ReSharper disable ConditionIsAlwaysTrueOrFalse

namespace Equinox76561198048419394.Core.Mirz
{
    public static class MrzUtils
    {
        #region Internal Types

        public enum NotificationType
        {
            Info,
            Warning,
            Error,
        }

        #endregion

        #region Static

        public const bool Debug = false;
        public const float Epsilon = 0.00001f;

        public static Vector4 DebugColor = new Vector4(1, 0, 1, 1);

        #endregion

        #region Properties

        public static bool IsServer => MyMultiplayerModApi.Static.IsServer;

        #endregion

        #region Notifications

        /// <summary>
        /// Creates a notification. Use this if you plan to dynamically show and hide your notifications and to prevent displaying multiple notifications with the same message at the same time.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static IMyHudNotification CreateNotification(string message, NotificationType type = NotificationType.Info)
        {
            return MyAPIGateway.Utilities?.CreateNotification(message, textColor: GetNotificationColor(type));
        }

        /// <summary>
        /// Display a notification of a specific type.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        public static void ShowNotification(string message, NotificationType type = NotificationType.Info)
        {
            MyAPIGateway.Utilities?.ShowNotification(message, textColor: GetNotificationColor(type));
        }

        /// <summary>
        /// Creates a debug notification.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public static IMyHudNotification CreateNotificationDebug(string message, int time = 2000)
        {
            return MyAPIGateway.Utilities?.CreateNotification(message, time, textColor: DebugColor);
        }
        
        /// <summary>
        /// Display a notification. Only works in debug mode.
        /// </summary>
        /// <param name="message"></param>
        public static void ShowNotificationDebug(string message, int time = 2000)
        {
            if (!Debug)
#pragma warning disable 162
                return;
#pragma warning restore 162

            MyAPIGateway.Utilities?.ShowNotification(message, disappearTimeMs: time, textColor: DebugColor);
        }

        public static void ShowDebug(this IMyHudNotification notification)
        {
            if (!Debug)
#pragma warning disable 162
                return;
#pragma warning restore 162

            notification.Show();
        }

        private static Vector4 GetNotificationColor(NotificationType type)
        {
            switch (type)
            {
                case NotificationType.Info:
                    return Color.White;
                case NotificationType.Warning:
                    return Color.Yellow;
                case NotificationType.Error:
                    return Color.Red;
                default:
                    return Color.White;
            }
        }

        #endregion
    }
}
