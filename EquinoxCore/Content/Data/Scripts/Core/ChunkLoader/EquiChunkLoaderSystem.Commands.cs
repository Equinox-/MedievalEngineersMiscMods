using System;
using System.Collections.Generic;
using System.Linq;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.ChunkLoader
{
    [StaticEventOwner]
    public partial class EquiChunkLoaderSystem
    {
        internal static bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerTarget = player?.ControlledEntity?.Get<MyTargetingComponentBase>();
            if (playerTarget == null)
                return Respond("You must have a character to use this command");
            var system = MySession.Static.Components.Get<EquiChunkLoaderSystem>();
            if (system == null)
                return Respond("Chunk loader system is missing");
            var tokens = message.Split(' ');
            const string modeInventory = "inventory";
            const string modeList = "list";
            const string modeShow = "show";
            const string modeHide = "hide";
            const string modeAdmin = "admin";

            if (tokens.Length < 2) return HelpListModes();
            switch (tokens[1])
            {
                case modeInventory: return ModeInventory();
                case modeList: return ModeList();
                case modeShow: return ModeShowHide(true);
                case modeHide: return ModeShowHide(false);
                case modeAdmin: return ModeAdmin();
                default: return HelpListModes();
            }

            bool HelpListModes() => Respond($"{tokens[0]} {modeInventory}|{modeList}|{modeShow}|{modeHide}|{modeAdmin}");

            bool ModeInventory()
            {
                if (!system._enabled) return Respond("Not enabled");
                var inventories = playerTarget.Detected.Entity?.Components.GetComponents<MyInventoryBase>().ToList();
                if (inventories == null || inventories.Count == 0)
                    return Respond("Must be targeting a block with an inventory");
                if (!NetworkTrust.IsTrustedBox(inventories[0]))
                    return Respond("No access at this location");
                var container = inventories[0].Container;
                var prefix = $"{container.Entity.DefinitionId?.SubtypeName}, {inventories.Count} inventories";
                var has = container.Get<EquiChunkLoaderInventoryTrigger>();
                var subcommand = tokens.Length >= 3 ? tokens[2] : "";
                switch (subcommand)
                {
                    case "add":
                        container.GetOrAdd<EquiChunkLoaderInventoryTrigger>();
                        return Respond($"{prefix} {(has != null ? "already had" : "now has")} inventory chunk loading");
                    case "remove":
                        if (has?.Key != null)
                            system.TriggerDestroyed(has.Key.Value, has.Entity);
                        container.RemoveAll<EquiChunkLoaderInventoryTrigger>();
                        return Respond($"{prefix} {(has != null ? "no longer has" : "didn't have")} inventory chunk loading");
                    case "info":
                        Respond($"{prefix}, {(has != null ? "has" : "does not have")} inventory chunk loading");
                        break;
                }

                return subcommand == "info" || Respond($"{tokens[0]} {modeInventory} add|remove|info");
            }

            bool ModeList()
            {
                if (!system._enabled) return Respond("Not enabled");
                var playerPos = playerTarget.Entity.GetPosition();
                if (!NetworkTrust.IsTrustedPoint(null, playerPos))
                    return Respond("No access at this location");
                var loaders = 0;
                var triggers = 0;
                foreach (var loader in system._chunkLoaders.Values)
                    if (loader.Entity.PositionComp.WorldAABB.Contains(playerPos) != ContainmentType.Disjoint)
                    {
                        loaders++;
                        triggers += loader.UsedBy.Count;
                    }

                return Respond($"{loaders} chunk loaders with {triggers} triggers cover this location");
            }

            bool ModeShowHide(bool show)
            {
                if (!system._enabled && !MyAPIGateway.Session.IsAdminModeEnabled(sender)) return Respond("Not enabled");
                var playerPos = playerTarget.Entity.GetPosition();
                if (!NetworkTrust.IsTrustedPoint(null, playerPos))
                    return Respond("No access at this location");
                var local = MyEventContext.Current.IsLocallyInvoked;
                if (!float.TryParse(tokens.Length >= 3 ? tokens[2] : "", out var radius))
                    radius = 1;
                else if (CheckAdmin())
                    return true;
                var query = new BoundingSphereD(playerPos, radius);
                if (local)
                {
                    system._debug.Clear();
                    if (show)
                        system._debug.AddRange(system.ChunkLoadingDebugShapes(query));
                    system.ShowHideDebug(show);
                }
                else
                {
                    if (show)
                        system.SendDebugChunks(query, MyEventContext.Current.Sender);
                    MyMultiplayerModApi.Static.RaiseEvent(system, x => x.ShowHideDebug, show, MyEventContext.Current.Sender);
                }

                return Respond($"{(show ? "Showing" : "Hiding")} chunk loader info");
            }

            bool ModeAdmin()
            {
                if (CheckAdmin()) return true;
                var subcommand = tokens.Length >= 3 ? tokens[2] : "";
                const string subEnable = "enable";
                const string subDisable = "disable";
                const string subReloadInterval = "reloadInterval";
                const string subMinTime = "minTime";
                switch (subcommand)
                {
                    case subEnable:
                        if (system._enabled) return Respond("Already enabled");
                        system._enabled = true;
                        return Respond("Now enabled");
                    case subDisable:
                        if (!system._enabled) return Respond("Already disabled");
                        system._enabled = false;
                        return Respond("Now disabled");
                    case subReloadInterval:
                        if (!TryParseTimeSpan(3, out var reloadTime))
                            return Respond($"{tokens[0]} {tokens[1]} {tokens[2]} reloadIntervalInMinutes");
                        system._reloadInterval = reloadTime;
                        return Respond($"Changed reload interval to {system.ReloadInterval}");
                    case subMinTime:
                        if (!TryParseTimeSpan(3, out var minLoadTime))
                            return Respond($"{tokens[0]} {tokens[1]} {tokens[2]} minimumLoadTimeInMinutes");
                        system._minLoadTime = minLoadTime;
                        return Respond($"Changed minimum load time to {system.MinLoadTime}");
                    default:
                        return Respond($"{tokens[0]} {tokens[1]} {subEnable}|{subDisable}|{subReloadInterval}|{subMinTime}");
                }
            }

            bool TryParseTimeSpan(int ix, out TimeSpan? value)
            {
                value = null;
                if (ix >= tokens.Length)
                    return false;
                if (tokens[ix] == "reset")
                    return true;
                if (!float.TryParse(tokens[ix], out var seconds) || seconds < 1)
                    return false;
                value = TimeSpan.FromSeconds(seconds);
                return true;
            }

            bool Respond(string response)
            {
                MyChatSystem.Static.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"),
                    0, response);
                return true;
            }

            bool CheckAdmin()
            {
                return !MyAPIGateway.Session.IsAdminModeEnabled(sender) && Respond("You need to enable Medieval Master to use this command.");
            }
        }

        internal void SendDebugChunks(BoundingSphereD query, EndpointId to)
        {
            MyMultiplayerModApi.Static.RaiseEvent(this, x => x.ResetDebugList, to);
            foreach (var debug in ChunkLoadingDebugShapes(query))
                MyMultiplayerModApi.Static.RaiseEvent(this, x => x.AddDebugList, debug, to);
        }

        private IEnumerable<ChunkLoadingDebug> ChunkLoadingDebugShapes(BoundingSphereD filter)
        {
            foreach (var host in _chunkLoaders.Values)
                if (host.Entity.PositionComp.WorldAABB.Intersects(filter))
                {
                    var users = host.UsedBy;
                    var userIds = users.Count > 0 ? new EntityId[users.Count] : null;
                    if (userIds != null) users.CopyTo(userIds);
                    yield return new ChunkLoadingDebug
                    {
                        Lod = host.Key.Lod,
                        Box = host.Key.Box,
                        UserIds = userIds,
                        LastLoadedSec = (int)host.LastLoadedFor.TotalSeconds,
                        LoadedAtSec = host.Entity.InScene ? (int)host.LoadedAt.TotalSeconds : 0,
                        DemandedSec = host.Entity.InScene ? (int)(host.KeepLoadedUntil - host.LoadedAt).TotalSeconds : 0,
                    };
                }
        }

        private readonly List<ChunkLoadingDebug> _debug = new List<ChunkLoadingDebug>();

        [Event, Client, Reliable]
        internal void ResetDebugList() => _debug.Clear();

        [Event, Client, Reliable]
        internal void AddDebugList(ChunkLoadingDebug item) => _debug.Add(item);

        [Event, Client, Reliable]
        internal void ShowHideDebug(bool show)
        {
            if (show)
                Scheduler.AddFixedUpdate(DebugDraw);
            else
                Scheduler.RemoveFixedUpdate(DebugDraw);
        }

        [FixedUpdate(false)]
        void DebugDraw()
        {
            using (var batch = MyRenderProxy.DebugDrawBatchAABB(MatrixD.Identity, Color.Red, shaded: false))
            {
                foreach (var shape in _debug)
                {
                    var key = new ChunkLoaderKey(shape.Lod, shape.Box);
                    var aabb = key.ToWorld();
                    batch.Add(ref aabb);

                    string text;
                    if (shape.LoadedAtSec > 0)
                    {
                        var now = (int)Session.ElapsedGameTime.TotalSeconds;
                        text = $"{TimeSpan.FromSeconds(now - shape.LoadedAtSec)} / {TimeSpan.FromSeconds(shape.DemandedSec)}";
                    }
                    else
                        text = $"{TimeSpan.FromSeconds(shape.LastLoadedSec)}";

                    MyRenderProxy.DebugDrawText3D(aabb.Center,
                        text,
                        Color.Lime, 0.5f);

                    if (shape.UserIds == null) continue;
                    foreach (var entityId in shape.UserIds)
                        if (Scene.TryGetEntity(entityId, out var entity))
                        {
                            var entityAabb = entity.PositionComp.WorldAABB;
                            batch.Add(ref entityAabb, Color.Blue);
                        }
                }
            }
        }

        [RpcSerializable]
        internal struct ChunkLoadingDebug
        {
            public int Lod;
            public BoundingBoxI Box;

            [Nullable]
            public EntityId[] UserIds;

            public int LastLoadedSec;
            public int LoadedAtSec;
            public int DemandedSec;
        }
    }
}