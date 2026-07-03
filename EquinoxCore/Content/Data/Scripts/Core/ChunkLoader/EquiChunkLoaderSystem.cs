using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Library.Collections;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Equinox76561198048419394.Core.ChunkLoader
{
    [MySessionComponent(typeof(MyObjectBuilder_EquiChunkLoaderSystem), AlwaysOn = true, AllowAutomaticCreation = true)]
    public partial class EquiChunkLoaderSystem : MySessionComponent, IMyPersistenceComponent
    {
        private readonly Dictionary<ChunkLoaderKey, EquiChunkLoaderHost> _chunkLoaders = new Dictionary<ChunkLoaderKey, EquiChunkLoaderHost>();
        private TimeSpan? _reloadInterval;
        private TimeSpan? _minLoadTime;
        private TimeSpan ReloadInterval => _reloadInterval ?? TimeSpan.FromHours(6);
        private TimeSpan MinLoadTime => _minLoadTime ?? TimeSpan.FromMinutes(15); 
        private bool _enabled;

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (MyMultiplayerModApi.Static.IsServer)
                AddScheduledUpdate(Tick, 60_000);
        }

        internal ChunkLoaderKey? TriggerPresent(MyEntity entity)
        {
            var topEntity = entity;
            while (topEntity.Parent != null)
                topEntity = topEntity.Parent;
            if (topEntity.Physics != null && !topEntity.Physics.IsStatic)
                return null;
            var box = topEntity.PositionComp.WorldAABB;
            foreach (var group in entity.Scene.GetEntityGroups(topEntity.Id))
                box.Include(group.GetWorldBounds());
            var key = ChunkLoaderKey.FromWorld(in box);
            HostFor(in key).UsedBy.Add(entity.Id);
            return key;
        }

        private void TriggerDestroyed(ChunkLoaderKey key, MyEntity entity)
        {
            if (_chunkLoaders.TryGetValue(key, out var host)
                && host.UsedBy.Remove(entity.Id)
                && host.UsedBy.Count == 0)
                RemoveChunkLoader(host);
        }

        internal void KeepLoaded(ChunkLoaderKey key, TimeSpan duration)
        {
            if (!_chunkLoaders.TryGetValue(key, out var tracker)) return;
            tracker.KeepLoadedUntil = Session.ElapsedGameTime + duration;

            // If a player is nearby immediately schedule loading this entity, which will keep it processing when the user leaves if necessary.
            var bounds = tracker.Entity.PositionComp.WorldAABB;
            foreach (var player in MyPlayers.Static.GetAllPlayers().Values)
            {
                var controlled = player.ControlledEntity;
                if (controlled == null || bounds.Contains(controlled.GetPosition()) == ContainmentType.Disjoint) continue;
                tracker.NextLoadTime = Session.ElapsedGameTime;
                return;
            }
        }

        private readonly MyDefinitionId _hostDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "EquiChunkLoaderHost");

        private EquiChunkLoaderHost HostFor(in ChunkLoaderKey key)
        {
            if (_chunkLoaders.TryGetValue(key, out var data))
                return data;
            return CreateHost(new MyObjectBuilder_EntityBase
            {
                ComponentContainer = new MyObjectBuilder_ComponentContainer
                {
                    Components =
                    {
                        new MyObjectBuilder_EquiChunkLoaderHost
                        {
                            Lod = key.Lod,
                            Min = key.Box.Min,
                            Max = key.Box.Max,
                            NextLoadTimeSec = Session.ElapsedGameTime.TotalSeconds + ReloadInterval.TotalSeconds * MyRandom.Instance.NextDouble(),
                        }
                    }
                },
            });
        }

        private EquiChunkLoaderHost CreateHost(MyObjectBuilder_EntityBase ob)
        {
            ob.EntityDefinitionId = _hostDefinitionId;
            var entity = Scene.LoadEntity(ob, activate: false);
            var host = entity.Get<EquiChunkLoaderHost>() ?? throw new ArgumentException("Created chunk loader host entity does not have host component");
            _chunkLoaders.Add(host.Key, host);
            return host;
        }

        [Update(false)]
        private void Tick(long dt)
        {
            if (!_enabled) return;
            using (PoolManager.Get(out List<EquiChunkLoaderHost> toRemove))
            {
                var now = Session.ElapsedGameTime;
                foreach (var tracker in _chunkLoaders.Values)
                {
                    // Not ready to load yet.
                    if (tracker.NextLoadTime > now) continue;
                    // Ready to load and hasn't been loaded.
                    if (!tracker.Entity.InScene)
                        EnsureLoaded(tracker);
                    // Currently loaded and needs to unload.
                    else if (tracker.KeepLoadedUntil < now)
                    {
                        EnsureUnloaded(tracker);
                        // If nothing remains in the user set, remove the tracker.
                        if (tracker.UsedBy.Count == 0) toRemove.Add(tracker);
                    }
                }

                foreach (var remove in toRemove)
                    RemoveChunkLoader(remove);
            }
        }

        private void EnsureLoaded(EquiChunkLoaderHost tracker)
        {
            if (tracker.Entity.InScene) return;
            // Adding the tracker entity to the scene will load the area.
            Scene.ActivateEntity(tracker.Entity);
            // Keep the tracker loaded for a minimum amount of time.
            tracker.KeepLoadedUntil = Session.ElapsedGameTime + MinLoadTime;
        }

        private void EnsureUnloaded(EquiChunkLoaderHost tracker)
        {
            if (!tracker.Entity.InScene) return;
            // Removing the tracker entity from the scene will unload the area (eventually)
            Scene.DeactivateEntity(tracker.Entity);
            // Schedule for another load in the future.
            tracker.NextLoadTime = Session.ElapsedGameTime + ReloadInterval;
            // Remove all unloaded/irrelevant entities from the user set.
            // They either no longer exist, no longer chunk load, or no longer are in this area.
            tracker.UsedBy.RemoveWhere(id =>
                !Scene.TryGetEntity(id, out var tracked)
                || !tracked.Components.TryGet(out EquiChunkLoaderTrigger trigger)
                || !trigger.Key.HasValue
                || !tracker.Key.Equals(trigger.Key));
        }

        private void RemoveChunkLoader(EquiChunkLoaderHost host)
        {
            host.Entity.Close();
            _chunkLoaders.Remove(host.Key);
        }

        protected override bool IsSerialized => _chunkLoaders.Count > 0;

        protected override MyObjectBuilder_SessionComponent Serialize()
        {
            var ob = (MyObjectBuilder_EquiChunkLoaderSystem)base.Serialize();
            ob.Enabled = _enabled;
            ob.ChunkLoaders = _chunkLoaders.Values.Select(x => new MyObjectBuilder_EquiChunkLoaderSystem.ChunkLoaderHostStorage
            {
                Id = x.Entity.EntityId,
                Components = x.Entity.Components.Serialize(),
            }).ToList();
            ob.MinLoadTimeSeconds = _minLoadTime?.TotalSeconds;
            ob.ReloadIntervalSeconds = _reloadInterval?.TotalSeconds;
            return ob;
        }

        protected override void Deserialize(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Deserialize(objectBuilder);
            var ob = (MyObjectBuilder_EquiChunkLoaderSystem)objectBuilder;
            _enabled = ob.Enabled;
            _chunkLoaders.Clear();
            if (ob.MinLoadTimeSeconds != null)
                _minLoadTime = TimeSpan.FromSeconds(ob.MinLoadTimeSeconds.Value);
            if (ob.ReloadIntervalSeconds != null)
                _reloadInterval = TimeSpan.FromSeconds(ob.ReloadIntervalSeconds.Value);
            if (ob.ChunkLoaders == null) return;

            // MyEntity.Init uses MyEntity.CreateStandardRenderComponentsExtCallback, which is not initialized until the static constructor
            // of MyEntities is invoked.
            // ReSharper disable once UnusedVariable
            var initializeEntitiesDueToWeirdRaceCondition = MyEntities.Count;
            foreach (var chunkLoader in ob.ChunkLoaders)
                CreateHost(new MyObjectBuilder_EntityBase
                {
                    EntityId = chunkLoader.Id,
                    ComponentContainer = chunkLoader.Components,
                });
        }

        public void Save()
        {
        }

        private void ValidateSceneEntity(MyEntity entity)
        {
            if (!entity.Components.TryGet(out EquiChunkLoaderHost host))
                throw new ArgumentException($"Chunk loader host entity {entity} does not have chunk loader host component");
            var existing = _chunkLoaders.GetValueOrDefault(host.Key);
            if (host != existing)
                throw new ArgumentException($"Chunk loader host entity {entity} is not already stored ({existing})");
        }

        public void AddEntity(MyEntity entity) => ValidateSceneEntity(entity);
        public void RemoveEntity(MyEntity entity) => ValidateSceneEntity(entity);
        public void AddGroup(MyGroup group) => throw new NotImplementedException();
        public void RemoveGroup(MyGroup group) => throw new NotImplementedException();
        private readonly HashSet<MyStringHash> _persistedTags = new HashSet<MyStringHash> { MyStringHash.GetOrCompute("EquiChunkLoaderHost") };
        public IEnumerable<MyStringHash> PersistedTags => _persistedTags;
        public IEnumerable<string> DataFolders { get; } = new HashSet<string>();
        public bool Default => false;
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiChunkLoaderSystem : MyObjectBuilder_SessionComponent
    {
        [XmlElement]
        [NoSerialize]
        public bool Enabled;

        [XmlElement]
        [NoSerialize]
        public double? ReloadIntervalSeconds;

        [XmlElement]
        [NoSerialize]
        public double? MinLoadTimeSeconds;

        [XmlElement("ChunkLoader")]
        [NoSerialize]
        public List<ChunkLoaderHostStorage> ChunkLoaders;

        public class ChunkLoaderHostStorage
        {
            [XmlAttribute]
            public long Id;

            [XmlIgnore]
            [Serialize]
            public MyObjectBuilder_ComponentContainer Components;

            [NoSerialize]
            [XmlElement("Component")]
            public AbstractXmlProxy<MyObjectBuilder_EntityComponent>[] ComponentsForXml
            {
                get => AbstractXmlProxy.WrapList(Components?.Components);
                set
                {
                    if (value == null)
                    {
                        Components = null;
                        return;
                    }

                    Components = new MyObjectBuilder_ComponentContainer();
                    // drop null values that came from removed mods
                    AbstractXmlProxy.Unwrap(value, Components.Components, dropDefault: true);
                }
            }
        }
    }
}