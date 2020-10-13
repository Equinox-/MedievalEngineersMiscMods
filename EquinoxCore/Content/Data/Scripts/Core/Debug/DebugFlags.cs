using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Logging;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.Debug
{
    public static class DebugFlags
    {
        private static bool _anythingEnabled;

        /// <summary>
        /// When encountering an exception immediately crash instead of just logging
        /// </summary>
        public static bool FailFast = false;

        public enum Level : byte
        {
            Trace = 1,
            Debug = 2
        }

        private static readonly ConcurrentDictionary<string, Level> State = new ConcurrentDictionary<string, Level>();
        private static DateTime? _errorDebounce;

        public static void SetLevel(string prefix, Level? flags)
        {
            MySession.Static.Log.Info($"EquinoxCore Logging level for \"{prefix}\" set to {flags?.ToString() ?? "nil"}");
            if (flags.HasValue)
                State[prefix] = flags.Value;
            else
                State.Remove(prefix);
            _anythingEnabled = State.Count > 0;
            DerivedState.Clear();
        }

        private static readonly ConcurrentDictionary<Type, Level> DerivedState = new ConcurrentDictionary<Type, Level>();

        private static Level GetLevel(Type t)
        {
            return DerivedState.GetOrAdd(t, (type) =>
            {
                var n = type.FullName;
                while (true)
                {
                    if (State.TryGetValue(n, out var k))
                        return k;
                    var idx = n.LastIndexOf('.');
                    if (idx < 0)
                        return 0;
                    n = n.Substring(0, idx);
                }
            });
        }

        public static bool Trace(Type t)
        {
            return _anythingEnabled && (GetLevel(t) <= Level.Trace);
        }

        public static bool Debug(Type t)
        {
            return _anythingEnabled && (GetLevel(t) <= Level.Debug);
        }

        public static void MaybeFailFast(string origin, FormattableString msg, Exception ex)
        {
            var logger = new NamedLogger(origin, MyLog.Default);
            logger.Critical(msg);
            logger.Critical(ex);
            // ReSharper disable once InvertIf
            if (FailFast)
            {
                MyLog.Default?.Flush();
                throw ex;
            }

            var utils = ((IMyUtilities) MyAPIUtilities.Static);
            if (!utils.IsDedicated && (!_errorDebounce.HasValue || DateTime.UtcNow > _errorDebounce.Value + TimeSpan.FromMinutes(5)))
            {
                utils.ShowNotification("An error occurred.  Please submit your logs to Equinox",
                    textColor: new Vector4(1, 0, 0, 1));
                _errorDebounce = DateTime.UtcNow;
            }
        }
    }
}