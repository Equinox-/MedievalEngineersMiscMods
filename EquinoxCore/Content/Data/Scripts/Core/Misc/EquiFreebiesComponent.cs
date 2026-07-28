using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Definitions.GUI;
using Medieval.Entities.Components;
using Medieval.GameSystems;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.Game.WorldEnvironment;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Components.Entity.CubeGrid;
using VRage.Components.Session;
using VRage.Factory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Library.Collections;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components.Entity.Grid;
using VRage.ObjectBuilders.Inventory;
using VRage.ObjectBuilders.Scene;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace Equinox76561198048419394.Core.Misc
{
    [MySessionComponent(typeof(MyObjectBuilder_EquiFreebiesComponent), AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public class EquiFreebiesComponent : MySessionComponent
    {
        private const double ClaimDistance = 10;
        private static readonly TimeSpan DisplayTimeToLive = TimeSpan.FromMinutes(1);

        [Automatic]
        private readonly MyChatSystem _chat = null;

        protected override void OnLoad()
        {
            base.OnLoad();
            if (!MyMultiplayerModApi.Static.IsServer) return;

            _chat?.RegisterChatCommand(Cmd, HandleCommand, "Claim freebies", MyChatCommandType.Server);
            if (MyMultiplayer.Static != null) MyMultiplayer.Static.ClientJoined += PlayerJoined;
        }

        private const string Cmd = "/freebies";
        private const string CmdModeClaim = "claim";

        private bool HandleCommand(ulong sender, string message, MyChatCommandType _)
        {
            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerTarget = player?.ControlledEntity?.Get<MyTargetingComponentBase>();
            if (playerTarget == null)
                return Respond("You must have a character to use this command");
            var tokens = message.Split(' ');

            const string modeAdd = "add";
            const string modeInfo = "info";
            const string modeRemove = "remove";
            const string modeInterval = "interval";
            const string modeDescribe = "desc";
            var isAdmin = MyAPIGateway.Session.IsAdminModeEnabled(sender);

            if (tokens.Length < 2) return HelpListModes();
            switch (tokens[1])
            {
                case CmdModeClaim: return ModeClaim();
                case modeInfo: return ModeInfo();
                case modeAdd when isAdmin: return ModeAdd();
                case modeRemove when isAdmin: return ModeRemove();
                case modeInterval when isAdmin: return ModeInterval();
                case modeDescribe when isAdmin: return ModeDescribe();
                default: return HelpListModes();
            }

            bool ParseFreebie(out FreebieId id, bool existing = false)
            {
                if (tokens.Length < 3)
                {
                    id = default;
                    return Respond($"{tokens[0]} {tokens[1]} {(existing ? string.Join("|", _freebies.Keys) : "freebie")}");
                }

                id = new FreebieId(MyStringHash.GetOrCompute(tokens[2]));
                return false;
            }

            bool ParseExistingFreebie(out FreebieConfig config)
            {
                config = null;
                if (ParseFreebie(out var id, true)) return true;
                return !_freebies.TryGetValue(id, out config)
                       && Respond($"Unknown freebie {id}, known {string.Join(",", _freebies.Keys)}");
            }

            bool ParseFreebieChoice(out FreebieChoiceId id, FreebieConfig cfg = null)
            {
                if (tokens.Length < 4)
                {
                    id = default;
                    return Respond($"{tokens[0]} {tokens[1]} {tokens[2]} {(cfg == null ? "choice" : string.Join("|", cfg.Choices.Keys))}");
                }

                id = new FreebieChoiceId(MyStringHash.GetOrCompute(tokens[3]));
                return false;
            }

            bool ParseExistingFreebieChoice(FreebieConfig cfg, out FreebieChoice choice)
            {
                choice = null;
                if (ParseFreebieChoice(out var id, cfg)) return true;
                return !cfg.Choices.TryGetValue(id, out choice)
                       && Respond($"Unknown choice {id}, known {string.Join(",", cfg.Choices.Keys)}");
            }

            bool ModeClaim()
            {
                // Clear existing display.
                if (MyAPIGateway.Multiplayer == null || sender == MyAPIGateway.Multiplayer.MyId)
                    RemoveDisplay();
                else
                    MyAPIGateway.Multiplayer.RaiseEvent(this, x => x.RemoveDisplay, MyEventContext.Current.Sender);

                FreebieConfig freebie;
                var claimable = ClaimableFreebies(sender);
                if (tokens.Length < 3 && claimable.Count == 1)
                    freebie = claimable[0];
                else if (ParseFreebie(out var freebieId))
                    return true;
                else if (!_freebies.TryGetValue(freebieId, out freebie))
                    return Respond($"Unknown freebie {freebieId}, claimable {string.Join(",", claimable.Select(x => x.Id))}");
                else if (!claimable.Contains(freebie))
                    return Respond($"Not claimable freebie {freebieId}, claimable {string.Join(",", claimable.Select(x => x.Id))}");

                FreebieChoice choice;
                if (tokens.Length < 4 && freebie.Choices.Count == 1)
                    choice = freebie.Choices.Values.First();
                else if (ParseExistingFreebieChoice(freebie, out choice))
                    return true;

                if (choice.Location.HasValue)
                {
                    var playerPos = playerTarget.Entity.GetPosition();
                    var dist = Vector3D.Distance(playerPos, choice.Location.Value);
                    if (dist > ClaimDistance)
                        return OnIncorrectLocation(choice, choice.Location.Value);
                }

                if (choice.Scene?.Boxes?.Length > 0) 
                {
                    var conflicts = SceneConflicts(choice.Scene);
                    if (conflicts.Count > 0) 
                    {
                        if (MyAPIGateway.Multiplayer == null || sender == MyAPIGateway.Multiplayer.MyId)
                            ShowPlacementConflicts(choice.Scene.Boxes, conflicts.ToArray());
                        else
                            MyAPIGateway.Multiplayer.RaiseEvent(
                                this,
                                x => x.ShowPlacementConflicts,
                                choice.Scene.Boxes, conflicts.ToArray(),
                                MyEventContext.Current.Sender);
                        return Respond($"{conflicts.Count} things are in the way");
                    }
                }

                var playerInventory = playerTarget.Entity.GetInventory();
                if (playerInventory == null)
                    return Respond("Player does not have inventory");

                SpawnFreebie(sender, choice, playerInventory);
                return Respond("Freebie spawned");
            }

            bool ModeInfo()
            {
                var claimable = ClaimableFreebies(sender);
                if (tokens.Length < 3)
                {
                    foreach (var free in _freebies.Values)
                        Respond($"- \"{free.Id}\" {(claimable.Contains(free) ? "(claimable)" : "(claimed)")}");
                    return true;
                }

                if (ParseExistingFreebie(out var freebie)) return true;
                Respond($"Freebie {freebie.Id}, {FormatInterval(freebie.Interval)} {(claimable.Contains(freebie) ? "(claimable)" : "(claimed)")}");
                foreach (var choice in freebie.Choices.Values)
                    Respond($"- Choice {choice.Id}, {choice.Description}");
                return true;
            }

            bool OnIncorrectLocation(FreebieChoice choice, Vector3D location)
            {
                var areas = MyGamePruningStructureSandbox.GetClosestPlanet(location)?.Get<MyPlanetAreasComponent>();
                string place;
                if (areas != null)
                {
                    var areaId = areas.GetArea(Vector3D.Transform(location, in areas.Entity.PositionComp.WorldMatrixInvScaledRef));
                    areas.UnpackAreaIdToNames(areaId, out var kingdom, out var region, out var area);
                    place = $"{kingdom}, {region}, {area}";
                }
                else
                    place = location.ToString();

                if (MyAPIGateway.Multiplayer == null || sender == MyAPIGateway.Multiplayer.MyId)
                    ShowChoiceLocationGuide(choice.WaypointName, location);
                else
                    MyAPIGateway.Multiplayer.RaiseEvent(
                        this,
                        x => x.ShowChoiceLocationGuide,
                        choice.WaypointName,
                        (SerializableVector3D)location,
                        MyEventContext.Current.Sender);
                return Respond($"Not in the right place ({place}) to claim this freebie");
            }

            bool CollectGrid(out FreebieScene scene)
            {
                scene = null;
                var target = playerTarget.Detected.Entity?.GetTopMostParent();
                if (target?.Components.Get<MyGridDataComponent>() == null) return Respond("No grid targeted");

                var sceneCollector = Scene.GetCollector();
                sceneCollector.CollectAllConnectedEntities(target);

                var boxes = new List<OrientedBoundingBoxD>();
                foreach (var entity in sceneCollector.Entities)
                    CollectBox(boxes, new OrientedBoundingBoxD(entity.PositionComp.LocalAABB, entity.PositionComp.WorldMatrix));

                scene = new FreebieScene( sceneCollector.SerializeContents(), boxes.ToArray());
                foreach (var entOb in scene.Scene.Entities)
                {
                    var gridDataOb = entOb.ComponentContainer.GetComponent<MyObjectBuilder_GridDataComponent>();
                    gridDataOb.CoordinateSystem = 0;
                }

                return false;
            }

            bool ModeAdd()
            {
                const string typeGrids = "grids";
                const string typeItems = "items";
                const string typeLocation = "location";
                const string typeCommit = "commit";
                var types = new HashSet<string>(tokens.Skip(4));
                if (!types.Contains(typeGrids) && !types.Contains(typeItems))
                    return Respond($"{tokens[0]} {tokens[1]} freebieId choiceId {typeGrids}|{typeItems}|{typeLocation}|{typeCommit}...");
                if (ParseFreebie(out var freebieId) || ParseFreebieChoice(out var choiceId)) return true;

                FreebieScene scene = null;
                var items = new List<MyObjectBuilder_InventoryItem>();
                if (types.Contains(typeGrids) && CollectGrid(out scene)) return true;
                if (types.Contains(typeItems))
                {
                    if (playerTarget.Container != null)
                        foreach (var inv in playerTarget.Container.GetComponents<MyInventory>())
                            if (inv.ShownInGUI)
                                foreach (var item in inv.Items)
                                    items.Add(item.Serialize());
                    if (items.Count == 0)
                        return Respond("Adding items to the freebie, but no items in player inventory");
                }

                var commit = types.Contains(typeCommit);
                var location = types.Contains(typeLocation) || scene != null ? (Vector3D?)playerTarget.Entity.GetPosition() : null;
                var freebie = _freebies.GetValueOrDefault(freebieId);
                var choice = freebie?.Choices.GetValueOrDefault(choiceId);
                Respond($"{(commit ? "Creating" : "Would create")} {(choice == null ? "new " : "")}choice {choiceId}"
                        + $" for {(freebie == null ? "new " : "")}freebie {freebieId}");
                if (location.HasValue)
                    Respond("Only redeemable here");
                if (scene != null)
                    foreach (var entity in scene.Scene.Entities)
                    {
                        var grid = entity.ComponentContainer.GetComponent<MyObjectBuilder_GridDataComponent>();
                        Respond(grid != null ? $"- Grid with {grid.Blocks?.Count ?? 0} blocks" : $"- Entity {entity.EntityDefinitionId?.SubtypeName}");
                    }

                foreach (var item in items)
                    Respond($"- {item.Amount}x {item.SubtypeName}");

                if (!commit) return true;

                if (freebie == null) _freebies.Add(freebieId, freebie = new FreebieConfig(freebieId));
                if (choice == null) freebie.Choices.Add(choiceId, choice = new FreebieChoice(freebie, choiceId));
                choice.Scene = scene;
                choice.Items = items;
                choice.Location = location;
                return true;
            }

            bool ModeRemove()
            {
                if (ParseExistingFreebie(out var freebie)) return true;
                if (tokens.Length >= 4)
                {
                    if (ParseExistingFreebieChoice(freebie, out var choice)) return true;
                    freebie.Choices.Remove(choice.Id);
                    if (freebie.Choices.Count > 0)
                        return Respond($"Remove choice {choice.Id} from freebie {freebie.Id}");
                }

                RemoveFreebie(freebie.Id);
                return Respond($"Removed freebie {freebie.Id}");
            }

            bool TryParseInterval(string val, out TimeSpan interval)
            {
                if (val == "once")
                {
                    interval = TimeSpan.MaxValue;
                    return true;
                }

                if (double.TryParse(val, out var hrs))
                {
                    interval = TimeSpan.FromHours(hrs);
                    return true;
                }

                interval = TimeSpan.Zero;
                return false;
            }

            bool ModeInterval()
            {
                if (ParseExistingFreebie(out var freebie)) return true;
                if (tokens.Length < 4 || !TryParseInterval(tokens[3], out var interval))
                    return Respond($"{tokens[0]} {tokens[1]} {tokens[2]} once|hours");
                var prev = freebie.Interval;
                freebie.Interval = interval;
                return Respond($"Change interval for {freebie.Id} from {FormatInterval(prev)} to {FormatInterval(interval)}");
            }

            bool ModeDescribe()
            {
                if (ParseExistingFreebie(out var freebie) || ParseExistingFreebieChoice(freebie, out var choice)) return true;
                if (tokens.Length < 5) return Respond($"{tokens[0]} {tokens[1]} {tokens[2]} {tokens[3]} description...");
                var desc = string.Join(" ", tokens.Skip(4));
                choice.Description = desc;
                return Respond($"Change freebie {freebie.Id} choice {choice} description to \"{desc}\"");
            }

            bool HelpListModes() =>
                Respond($"{tokens[0]} {CmdModeClaim}|{modeInfo}" + (isAdmin ? $"|{modeAdd}|{modeRemove}|{modeInterval}|{modeDescribe}" : ""));

            bool Respond(string response)
            {
                MyChatSystem.Static.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"),
                    0, response);
                return true;
            }
        }

        private static string FormatInterval(TimeSpan interval) => interval == TimeSpan.MaxValue ? "once" : interval.ToString();

        private static List<OrientedBoundingBoxD> SceneConflicts(FreebieScene scene)
        {
            var results = new List<OrientedBoundingBoxD>();
            if (scene.Boxes.Length == 0) return results;
            var aabb = BoundingBoxD.CreateInvalid();
            foreach (var box in scene.Boxes)
                aabb.Include(box.GetAABB());
            using (PoolManager.Get(out List<MyEntity> entities))
            using (PoolManager.GetArray(8, out Vector3D[] corners))
            {
                MyGamePruningStructure.GetAllEntitiesInBox(aabb, entities);
                foreach (var entity in entities)
                {
                    if (entity is MyVoxelBase || entity is MyEnvironmentSector || entity.Physics == null) continue;
                    foreach (var box in scene.Boxes)
                    {
                        var entityObb = new OrientedBoundingBoxD(entity.PositionComp.LocalAABB, entity.PositionComp.WorldMatrix);
                        if (!box.Intersects(ref entityObb)) continue;
                        if (entity.PositionComp.LocalAABB.Volume() <= 8)
                        {
                            // Use small boxes directly.
                            CollectBox(results, entityObb);
                            continue;
                        }

                        var localAabb = BoundingBoxD.CreateInvalid();
                        box.GetCorners(corners, 0);
                        for (var i = 0; i < 8; i++)
                            localAabb.Include(Vector3D.Transform(in corners[i], in entity.PositionComp.WorldMatrixInvScaledRef));
                        var intersection = localAabb.Intersect(entity.PositionComp.LocalAABB);
                        CollectBox(results, new OrientedBoundingBoxD(intersection, entity.PositionComp.WorldMatrix));
                    }
                }
            }

            return results;
        }

        private static void CollectBox(List<OrientedBoundingBoxD> results, OrientedBoundingBoxD box)
        {
            foreach (var existing in results)
                if (existing.Contains(ref box) == ContainmentType.Contains)
                    return;
            results.RemoveAll(x => box.Contains(ref x) == ContainmentType.Contains);
            results.Add(box);
        }

        private void PlayerJoined(ulong id)
        {
            var now = Session.ElapsedGameTime;
            var available = ClaimableFreebies(id);
            if (available.Count == 0) return;
            _chat?.SendMessageToClient(
                id,
                MyChatSystem.SystemChannel,
                0,
                $"You have {string.Join(", ", available.Select(x => x.Id))} freebies available, \"{Cmd} {CmdModeClaim}\" to claim");
        }

        private void SpawnFreebie(ulong player, FreebieChoice choice, MyInventory inventory) 
        {
            if (choice.Items != null)
                foreach (var itemOb in choice.Items)
                {
                    var item = MyInventoryItem.Factory.CreateAndDeserialize(itemOb);
                    if (item != null)
                        inventory.Add(item, MyInventoryBase.NewItemParams.ForcedInsertion);
                }

            if (choice.Scene != null)
            {
                IMyUtilities utils = MyAPIUtilities.Static;
                var ob = utils.SerializeFromXML<MyObjectBuilder_Scene>(utils.SerializeToXML(choice.Scene.Scene));
                Scene.RemapObject(ob);
                Scene.LoadAsync(ob);
            }

            GetPlayerState(player).Claims[choice.Freebie.Id] = Session.ElapsedGameTime;
        }

        private List<FreebieConfig> ClaimableFreebies(ulong id)
        {
            var now = Session.ElapsedGameTime;
            var state = GetPlayerState(id);
            var available = new List<FreebieConfig>();
            foreach (var freebie in _freebies.Values)
                if (!state.Claims.TryGetValue(freebie.Id, out var lastClaimed)
                    || (freebie.Interval < TimeSpan.MaxValue && lastClaimed + freebie.Interval < now))
                    available.Add(freebie);
            return available;
        }

        #region Client Display

        [Event, Reliable, Client]
        private void RemoveDisplay()
        {
            RemoveScheduledUpdate(RemoveDisplayDelayed);
            RemoveDisplayDelayed(0);
        }

        [Event, Reliable, Client]
        private void ShowChoiceLocationGuide(string name, SerializableVector3D target)
        {
            RemoveDisplay();
            if (!MyDefinitionManager.TryGet(MyStringHash.GetOrCompute("Freebie"), out MyWaypointDefinition waypointDef)) return;
            Vector3D pos = target;
            var planet = MyGamePruningStructureSandbox.GetClosestPlanet(pos);
            _waypoints = planet?.Get<MyPlanetaryWaypointComponent>();
            if (_waypoints == null) return;
            _waypointProvider = new WaypointProvider(new MyWaypoint(name, waypointDef)
            {
                Position = pos,
            });
            _waypoints.AddWaypointProvider(_waypointProvider);
            AddScheduledCallback(RemoveDisplayDelayed, DisplayTimeToLive);
        }

        [Event, Reliable, Client]
        private void ShowPlacementConflicts(OrientedBoundingBoxD[] scene, OrientedBoundingBoxD[] conflicts)
        {
            RemoveDisplay();
            _sceneBoxes = scene;
            _conflictBoxes = conflicts;
            AddFixedUpdate(RenderBoxes);
            AddScheduledCallback(RemoveDisplayDelayed, DisplayTimeToLive);
        }

        [Update(false)]
        private void RemoveDisplayDelayed(long _)
        {
            if (_waypointProvider != null)
            {
                _waypoints.RemoveWaypointProvider(_waypointProvider);
                _waypoints = null;
                _waypointProvider = null;
            }

            _sceneBoxes = null;
            _conflictBoxes = null;
            RemoveFixedUpdate(RenderBoxes);
        }

        private OrientedBoundingBoxD[] _sceneBoxes;
        private OrientedBoundingBoxD[] _conflictBoxes;

        [FixedUpdate(false)]
        private void RenderBoxes()
        {
            if (_sceneBoxes != null)
                foreach (var box in _sceneBoxes)
                    MyRenderProxy.DebugDrawOBB(box, Color.Blue, depthRead: false, shaded: false);
            if (_conflictBoxes != null)
                foreach (var box in _conflictBoxes)
                    MyRenderProxy.DebugDrawOBB(box, Color.Red, depthRead: false, shaded: false);
        }

        private MyPlanetaryWaypointComponent _waypoints;
        private IMyWaypointProvider _waypointProvider;

        private sealed class WaypointProvider : IMyWaypointProvider
        {
            private readonly MyWaypoint _waypoint;

            public WaypointProvider(MyWaypoint waypoint) => _waypoint = waypoint;

            public IEnumerable<MyWaypoint> GetWaypoints(long _, Vector3D playerPos, double range)
            {
                if (Vector3D.Distance(playerPos, _waypoint.Position) <= range)
                    yield return _waypoint;
            }
        }

        #endregion

        #region Operations

        private void RemoveFreebie(FreebieId id)
        {
            _freebies.Remove(id);
            foreach (var player in _players.Values)
                player.Claims.Remove(id);
        }

        private PlayerState GetPlayerState(ulong id)
        {
            if (!_players.TryGetValue(id, out var state))
                _players.Add(id, state = new PlayerState());
            return state;
        }

        #endregion

        #region Freebie Config

        private readonly Dictionary<FreebieId, FreebieConfig> _freebies = new Dictionary<FreebieId, FreebieConfig>();

        private readonly struct FreebieId : IEquatable<FreebieId>
        {
            public readonly MyStringHash Value;
            public FreebieId(MyStringHash value) => Value = value;
            public bool Equals(FreebieId other) => Value.Equals(other.Value);
            public override bool Equals(object obj) => obj is FreebieId other && Equals(other);
            public override int GetHashCode() => Value.GetHashCode();
            public override string ToString() => Value.String;
            public static implicit operator string(FreebieId id) => id.Value.String;
        }

        private readonly struct FreebieChoiceId : IEquatable<FreebieChoiceId>
        {
            public readonly MyStringHash Value;
            public FreebieChoiceId(MyStringHash value) => Value = value;
            public bool Equals(FreebieChoiceId other) => Value.Equals(other.Value);
            public override bool Equals(object obj) => obj is FreebieChoiceId other && Equals(other);
            public override int GetHashCode() => Value.GetHashCode();
            public override string ToString() => Value.String;
            public static implicit operator string(FreebieChoiceId id) => id.Value.String;
        }

        private class FreebieConfig
        {
            public readonly FreebieId Id;

            public TimeSpan Interval = TimeSpan.MaxValue;
            public readonly Dictionary<FreebieChoiceId, FreebieChoice> Choices = new Dictionary<FreebieChoiceId, FreebieChoice>();

            public FreebieConfig(FreebieId id) => Id = id;

            public MyObjectBuilder_EquiFreebiesComponent.FreebieConfig Serialize() => new MyObjectBuilder_EquiFreebiesComponent.FreebieConfig
            {
                Id = Id,
                IntervalSec = Interval == TimeSpan.MaxValue ? null : (double?)Interval.TotalSeconds,
                Choices = Choices.Values.Select(x => x.Serialize()).ToArray(),
            };

            public void Deserialize(MyObjectBuilder_EquiFreebiesComponent.FreebieConfig ob)
            {
                Interval = ob.IntervalSec != null ? TimeSpan.FromSeconds(ob.IntervalSec.Value) : TimeSpan.MaxValue;
                Choices.Clear();
                if (ob.Choices != null)
                    foreach (var choice in ob.Choices)
                    {
                        var cfg = new FreebieChoice(this, new FreebieChoiceId(MyStringHash.GetOrCompute(choice.Id)));
                        cfg.Deserialize(choice);
                        Choices[cfg.Id] = cfg;
                    }
            }
        }

        private class FreebieChoice
        {
            public readonly FreebieConfig Freebie;
            public readonly FreebieChoiceId Id;
            public Vector3D? Location;
            public FreebieScene Scene;
            public List<MyObjectBuilder_InventoryItem> Items;
            public string Description;

            public FreebieChoice(FreebieConfig freebie, FreebieChoiceId id)
            {
                Freebie = freebie;
                Id = id;
            }

            public MyObjectBuilder_EquiFreebiesComponent.FreebieChoice Serialize() => new MyObjectBuilder_EquiFreebiesComponent.FreebieChoice
            {
                Id = Id,
                Location = Location,
                Scene = Scene != null
                    ? new MyObjectBuilder_EquiFreebiesComponent.FreebieScene
                    {
                        Scene = Scene.Scene,
                        Bounds = Scene.Boxes.Select(x => (SerializableOrientedBoundingBoxD)x).ToArray(),
                    }
                    : null,
                Items = AbstractXmlProxy.WrapList(Items),
                Description = Description,
            };

            public void Deserialize(MyObjectBuilder_EquiFreebiesComponent.FreebieChoice ob)
            {
                if (ob.Scene?.Scene != null && ob.Scene?.Bounds != null)
                    Scene = new FreebieScene(ob.Scene.Scene, ob.Scene.Bounds.Select(x => (OrientedBoundingBoxD)x).ToArray());
                Items = AbstractXmlProxy.Unwrap(ob.Items);
                Location = ob.Location;
                Description = ob.Description;
            }

            public string WaypointName => Freebie.Id.Value.String + (Id.Value == MyStringHash.NullOrEmpty ? "" : ", " + Id.Value.String);
        }

        public class FreebieScene
        {
            public readonly MyObjectBuilder_Scene Scene;
            public readonly OrientedBoundingBoxD[] Boxes;

            public FreebieScene(MyObjectBuilder_Scene scene, OrientedBoundingBoxD[] boxes)
            {
                Scene = scene;
                Boxes = boxes;
            }
        }

        #endregion

        #region Player State

        private readonly Dictionary<ulong, PlayerState> _players = new Dictionary<ulong, PlayerState>();

        private class PlayerState
        {
            public readonly Dictionary<FreebieId, TimeSpan> Claims = new Dictionary<FreebieId, TimeSpan>();

            public MyObjectBuilder_EquiFreebiesComponent.PlayerState Serialize(ulong steamId) => new MyObjectBuilder_EquiFreebiesComponent.PlayerState
            {
                SteamId = steamId,
                Claims = Claims.Select(x => new MyObjectBuilder_EquiFreebiesComponent.PlayerState.ClaimRecord
                {
                    Freebie = x.Key.Value.String,
                    At = x.Value.Ticks,
                }).ToArray()
            };

            public void Deserialize(MyObjectBuilder_EquiFreebiesComponent.PlayerState ob)
            {
                Claims.Clear();
                if (ob.Claims != null)
                    foreach (var claim in ob.Claims)
                        Claims[new FreebieId(MyStringHash.GetOrCompute(claim.Freebie))] = TimeSpan.FromTicks(claim.At);
            }
        }

        #endregion

        #region Serialization

        protected override bool IsSerialized => _freebies.Count > 0;

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_EquiFreebiesComponent)base.Serialize();
            ob.Freebies = _freebies.Select(x => x.Value.Serialize()).ToArray();
            ob.Players = _players.Select(x => x.Value.Serialize(x.Key)).ToArray();
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_EquiFreebiesComponent)objectBuilder;
            _freebies.Clear();
            if (ob.Freebies != null)
                foreach (var freebie in ob.Freebies)
                {
                    var cfg = new FreebieConfig(new FreebieId(MyStringHash.GetOrCompute(freebie.Id)));
                    cfg.Deserialize(freebie);
                    _freebies[cfg.Id] = cfg;
                }

            _players.Clear();
            if (ob.Players != null)
                foreach (var player in ob.Players)
                {
                    var state = new PlayerState();
                    state.Deserialize(player);
                    _players[player.SteamId] = state;
                }
        }

        #endregion
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiFreebiesComponent : MyObjectBuilder_SessionComponent
    {
        [XmlElement("Freebie")]
        [NoSerialize]
        public FreebieConfig[] Freebies;

        [XmlElement("Player")]
        [NoSerialize]
        public PlayerState[] Players;

        public class FreebieConfig
        {
            [XmlAttribute]
            public string Id;

            [XmlElement]
            public double? IntervalSec;

            [XmlElement("Choice")]
            public FreebieChoice[] Choices;
        }

        public class FreebieChoice
        {
            [XmlAttribute]
            public string Id;

            [XmlElement]
            public SerializableVector3D? Location;

            [XmlElement]
            public string Description;

            [XmlElement]
            public FreebieScene Scene;

            [XmlElement]
            public AbstractXmlProxy<MyObjectBuilder_InventoryItem>[] Items;
        }

        public class FreebieScene
        {
            [XmlElement]
            public AbstractXmlProxy<MyObjectBuilder_Scene> Scene;

            [XmlElement("Bounds")]
            public SerializableOrientedBoundingBoxD[] Bounds;
        }

        public class PlayerState
        {
            [XmlAttribute]
            public ulong SteamId;

            [XmlElement("Claim")]
            public ClaimRecord[] Claims;

            public struct ClaimRecord
            {
                [XmlAttribute]
                public string Freebie;

                [XmlAttribute]
                public long At;
            }
        }
    }
}