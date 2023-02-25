using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.Game;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Logging;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Scene;
using VRage.ParallelWorkers;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Data.Framework
{
    [MySessionComponent(typeof(MyObjectBuilder_EquiCartographicDataManager), AlwaysOn = true)]
    public class EquiExternalItemDataManager : MySessionComponent, IMyPersistenceComponent
    {
        private readonly MyParallelTask _parallel = new MyParallelTask();

        protected override void OnAddedToSession()
        {
            base.OnAddedToSession();
            if (MyMultiplayerModApi.Static.IsServer)
            {
                AddFixedUpdate(RefreshRequired);
                AddScheduledUpdate(BackgroundUnload, 5_000);
            }
        }

        #region Access Management

        private readonly MyHashSetDictionary<ulong, ulong> _playerToData = new MyHashSetDictionary<ulong, ulong>();
        private readonly MyHashSetDictionary<ulong, ulong> _dataToPlayers = new MyHashSetDictionary<ulong, ulong>();
        public bool PlayerHasAccess(MyPlayer.PlayerId player, EntityId data) => _playerToData.GetOrDefault(player.SteamId).Contains(data.Value);

        [FixedUpdate(false)]
        private void RefreshRequired()
        {
            _playerToData.Clear();
            _dataToPlayers.Clear();
            foreach (var player in MyPlayers.Static.GetAllPlayers().Values)
            {
                var steamId = player.Id.SteamId;
                var controlled = player.ControlledEntity;
                if (controlled == null)
                    continue;
                foreach (var component in controlled.Components)
                {
                    if (!(component is MyInventory inv) || !inv.ShownInGUI)
                        continue;
                    foreach (var item in inv.Items)
                        if (item is IEquiExternalDataItem externalDataItem)
                        {
                            var entity = externalDataItem.DataHost;
                            if (entity.HasValue)
                            {
                                _playerToData.Add(steamId, entity.Value.Value);
                                _dataToPlayers.Add(entity.Value.Value, steamId);
                            }
                        }
                }
            }

            foreach (var required in _dataToPlayers.Keys)
            {
                EntityId id = required;
                DataHolder state;
                lock (_holders)
                {
                    if (!_holders.TryGetValue(id, out state))
                        _holders.Add(id, state = new DataHolder(id, this));
                }

                state.RequestLoad();
            }
        }

        [Update(false)]
        private void BackgroundUnload(long dt)
        {
            using (PoolManager.Get(out List<DataHolder> unloading))
            {
                lock (_holders)
                {
                    foreach (var holder in _holders)
                        if (!_dataToPlayers.ContainsKey(holder.Key.Value))
                            unloading.Add(holder.Value);
                }

                foreach (var holder in unloading)
                    holder.RequestUnload();
            }
        }

        #endregion

        #region Loading

        private readonly Dictionary<EntityId, DataHolder> _holders = new Dictionary<EntityId, DataHolder>(EntityId.Comparer);

        private enum DataState
        {
            Persisted,
            Serialized,
            Loaded,
            Failed,
        }

        private static string PersistencePath(EntityId id) => $"external_item_data_{id.Value:X}.xml";

        private sealed class DataHolder : IWork, ISceneLoadListener
        {
            private readonly EquiExternalItemDataManager _owner;
            public readonly EntityId Id;

            private string Path => PersistencePath(Id);

            public DataHolder(EntityId id, EquiExternalItemDataManager owner)
            {
                Id = id;
                _owner = owner;
                _state = DataState.Persisted;
            }

            public DataHolder(EntityId id, EquiExternalItemDataManager owner, MyEntity initialState)
            {
                Id = id;
                _owner = owner;
                _entity = initialState;
                _state = DataState.Loaded;
            }

            private volatile MyEntity _entity;
            private volatile MyObjectBuilder_Scene _serialized;
            private volatile DataState _state;
            private volatile bool _activeWork;

            public void RequestLoad()
            {
                lock (this)
                {
                    if (_activeWork)
                        return;
                    switch (_state)
                    {
                        case DataState.Persisted:
                            _owner.GetLogger().Info($"Loading {Id} from disk");
                            _activeWork = true;
                            _owner._parallel.Start(this);
                            break;
                        case DataState.Serialized:
                            _owner.GetLogger().Info($"Adding {Id} to scene");
                            _activeWork = true;
                            _owner.Scene.LoadAsync(_serialized, this);
                            break;
                        case DataState.Loaded:
                        case DataState.Failed:
                            return;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            public void DoWork()
            {
                try
                {
                    lock (this)
                    {
                        if (_state != DataState.Persisted)
                            return;
                    }

                    using (var stream = MyAPIGateway.Utilities.ReadFileInWorldStorage(Path, typeof(MyAPIGateway)))
                    {
                        var content = stream.ReadToEnd();
                        lock (this)
                        {
                            _serialized = MyAPIGateway.Utilities.SerializeFromXML<MyObjectBuilder_Scene>(content);
                            _state = DataState.Serialized;
                        }
                    }

                    _owner.GetLogger().Info($"Loaded {Id} from disk");
                }
                catch (Exception err)
                {
                    _owner.GetLogger().Warning($"Failed to load external item data {Id}.  Scene failed to deserialize: {err}");
                    _state = DataState.Failed;
                }
                finally
                {
                    _activeWork = false;
                    MyLog.Default.Flush();
                }
            }

            public void OnLoaded(MyScene loadingScene)
            {
                lock (this)
                {
                    if (loadingScene.TryGetEntity(Id, out var entity))
                    {
                        _entity = entity;
                        _state = DataState.Loaded;
                        _entity.Replicate = true;
                        _owner.GetLogger().Info($"Added {Id} to scene");
                    }
                    else
                    {
                        _owner.GetLogger().Warning($"Failed to load external item data {Id}.  Scene was missing entity.");
                        _state = DataState.Failed;
                    }
MyLog.Default.Flush();
                    _activeWork = false;
                }
            }

            public void OnAddedToScene()
            {
            }

            public void RequestUnload()
            {
                lock (this)
                {
                    if (_activeWork)
                        return;
                    if (_state == DataState.Loaded)
                    {
                        _serialized = SerializeToScene();
                        _owner.Scene.Destroy(_entity);
                        _entity = null;
                        _state = DataState.Serialized;
                        _owner.GetLogger().Info($"Unloading {Id}");
                        MyLog.Default.Flush();
                    }
                }
            }

            private MyObjectBuilder_Scene SerializeToScene()
            {
                var ob = _entity.GetObjectBuilder();
                var scene = new MyObjectBuilder_Scene();
                scene.Entities.Add(ob);
                return scene;
            }

            public void Save(out bool stopTracking)
            {
                MyObjectBuilder_Scene scene = null;
                lock (this)
                {
                    switch (_state)
                    {
                        case DataState.Persisted:
                            stopTracking = !_activeWork;
                            return;
                        case DataState.Serialized:
                            stopTracking = !_activeWork;
                            scene = _serialized;
                            break;
                        case DataState.Loaded:
                            stopTracking = false;
                            scene = SerializeToScene();
                            break;
                        case DataState.Failed:
                            stopTracking = false;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (scene != null)
                {
                    _owner.GetLogger().Info($"Writing {Id} to disk");
                    using (var stream = MyAPIGateway.Utilities.WriteFileInWorldStorage(Path, typeof(MyAPIGateway)))
                    {
                        stream.Write(MyAPIGateway.Utilities.SerializeToXML(scene));
                    }
                    MyLog.Default.Flush();
                }
            }
        }

        #endregion

        #region IMyPersistenceComponent

        public void Save()
        {
            lock (_holders)
                using (PoolManager.Get(out List<EntityId> trackingNotNeeded))
                {
                    foreach (var holder in _holders)
                    {
                        holder.Value.Save(out var stopTracking);
                        if (stopTracking)
                            trackingNotNeeded.Add(holder.Key);
                    }

                    foreach (var id in trackingNotNeeded)
                    {
                        this.GetLogger().Info($"Done tracking {id}");
                        _holders.Remove(id);
                        MyLog.Default.Flush();
                    }
                }
        }

        public void AddEntity(MyEntity entity)
        {
            lock (_holders)
                _holders[entity.Id] = new DataHolder(entity.Id, this, entity);
        }

        public void RemoveEntity(MyEntity entity)
        {
            // Never destroy the holders since it's not possible to safely do so without loading every single item.
        }

        void IMyPersistenceComponent.AddGroup(MyGroup group)
        {
        }

        void IMyPersistenceComponent.RemoveGroup(MyGroup group)
        {
        }

        IEnumerable<MyStringHash> IMyPersistenceComponent.PersistedTags { get; } = new List<MyStringHash> { MyStringHash.GetOrCompute("ExternalItemData") };
        IEnumerable<string> IMyPersistenceComponent.DataFolders { get; } = new List<string>();
        bool IMyPersistenceComponent.Default => false;

        #endregion

        public MyEntity Create(string subtype)
        {
            return Create(new MyObjectBuilder_EntityBase
            {
                EntityDefinitionId = new SerializableDefinitionId(typeof(MyObjectBuilder_EntityBase), subtype)
            });
        }

        public MyEntity Create(MyObjectBuilder_EntityBase entity)
        {
            if (!MyMultiplayerModApi.Static.IsServer)
                throw new Exception("Can only create on server");
            entity.PersistentFlags = MyPersistentEntityFlags2.None;
            entity.PositionAndOrientation = new MyPositionAndOrientation(MatrixD.CreateTranslation(new Vector3D(1e9)));
            if (entity.EntityId == 0)
                entity.EntityId = MyEntityIdentifier.AllocateId();
            var result = Scene.LoadEntity(entity);
            result.Replicate = true;
            return result;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCartographicDataManager : MyObjectBuilder_SessionComponent
    {
    }
}