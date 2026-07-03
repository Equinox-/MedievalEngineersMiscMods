using System;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.Session;

namespace Equinox76561198048419394.Core.ChunkLoader
{
    public abstract class EquiChunkLoaderTrigger : MyEntityComponent
    {
        internal ChunkLoaderKey? Key;
        private EquiChunkLoaderSystem _chunkLoaderSystem;

        private EquiChunkLoaderSystem ChunkLoaderSystem =>
            _chunkLoaderSystem ?? (_chunkLoaderSystem = MySession.Static?.Components.Get<EquiChunkLoaderSystem>());

        protected static bool IsServer => MyMultiplayerModApi.Static.IsServer;
        protected virtual bool IsValid => true;

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            if (IsServer && IsValid) Key = ChunkLoaderSystem?.TriggerPresent(Entity);
        }

        protected bool KeepLoaded(TimeSpan forTime)
        {
            if (Entity == null || !Entity.InScene) return false;
            if (Key == null || ChunkLoaderSystem == null) return false;
            ChunkLoaderSystem.KeepLoaded(Key.Value, forTime);
            return true;
        }

        public override bool IsSerialized => true;
    }

    [MyComponent(typeof(MyObjectBuilder_EquiChunkLoaderInventoryTrigger))]
    [MyDependency(typeof(MyInventoryBase), Critical = false)]
    public class EquiChunkLoaderInventoryTrigger : EquiChunkLoaderTrigger
    {
        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            if (!IsServer) return;
            foreach (var comp in Container) OnComponentAdded(comp);
            Container.ComponentAdded += OnComponentAdded;
            Container.ComponentRemoved += OnComponentRemoved;
        }

        public override void OnBeforeRemovedFromContainer()
        {
            Container.ComponentAdded -= OnComponentAdded;
            Container.ComponentRemoved -= OnComponentRemoved;
            foreach (var comp in Container) OnComponentRemoved(comp);
            base.OnBeforeRemovedFromContainer();
        }

        private void OnComponentAdded(MyEntityComponent obj)
        {
            if (obj is MyInventoryBase inv)
                inv.ContentsChanged += OnInventoryChanged;
        }

        private void OnComponentRemoved(MyEntityComponent obj)
        {
            if (obj is MyInventoryBase inv)
                inv.ContentsChanged -= OnInventoryChanged;
        }

        private void OnInventoryChanged(MyInventoryBase _) => KeepLoaded(TimeSpan.FromMinutes(10));
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiChunkLoaderInventoryTrigger : MyObjectBuilder_EntityComponent
    {
    }
}