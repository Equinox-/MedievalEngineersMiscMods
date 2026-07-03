using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Util;
using Medieval.Entities.Components;
using Medieval.GameSystems;
using Sandbox.Game.Entities;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRage.Scene;
using VRage.Serialization;
using VRage.Session;
using VRageMath;

namespace Equinox76561198048419394.Core.ChunkLoader
{
    [MyComponent(typeof(MyObjectBuilder_EquiChunkLoaderHost))]
    [MyDependency(typeof(MyPositionComponentBase))]
    [MyForwardDependency(typeof(MyInfinitePersistenceViewComponent))]
    public class EquiChunkLoaderHost : MyEntityComponent
    {
        private ChunkLoaderKey _key;
        public ref readonly ChunkLoaderKey Key => ref _key;
        public readonly HashSet<EntityId> UsedBy = new HashSet<EntityId>();
        internal TimeSpan LastLoadedFor { get; private set; }
        internal TimeSpan NextLoadTime;
        internal TimeSpan LoadedAt;
        internal TimeSpan KeepLoadedUntil;

        [Automatic]
        private readonly MyPositionComponentBase _position = null;

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            Entity.Flags &= ~(EntityFlags.Sync | EntityFlags.Persist);
            EnforceLocation();
        }

        private void EnforceLocation()
        {
            var box = _key.ToWorld();
            _position.WorldMatrix = MatrixD.CreateWorld(box.Center);
            _position.WorldVolume = new BoundingSphereD(box.Center, box.Extents.Length());
            _position.WorldAABB = box;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            LoadedAt = MySession.Static.ElapsedGameTime;
            this.GetLogger().Info($"{DescribeLocation()} loaded, currently {UsedBy.Count} users");
        }

        public override void OnRemovedFromScene()
        {
            var elapsed = MySession.Static.ElapsedGameTime - LoadedAt;
            LastLoadedFor = elapsed;
            this.GetLogger().Info($"{DescribeLocation()} unloaded after {elapsed}, currently {UsedBy.Count} users");
            base.OnRemovedFromScene();
        }

        private string DescribeLocation()
        {
            var pos = _position.WorldMatrixRef.Translation();
            var areas = MyGamePruningStructureSandbox.GetClosestPlanet(pos)?.Components.Get<MyPlanetAreasComponent>();
            if (areas == null) return $"lod={_key.Lod}, {pos}";
            var areaId = areas.GetArea(Vector3D.Transform(in pos, in areas.Entity.PositionComp.WorldMatrixInvScaledRef));
            areas.UnpackAreaIdToNames(areaId, out var kingdom, out var region, out var area);
            return $"lod={_key.Lod}, {kingdom}, {region}, {area}";
        }

        public override bool IsSerialized => true;

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiChunkLoaderHost)base.Serialize(copy);
            ob.Lod = _key.Lod;
            ob.Min = _key.Box.Min;
            ob.Max = _key.Box.Max;
            ob.NextLoadTimeSec = NextLoadTime.TotalSeconds;
            ob.LastLoadedForSec = LastLoadedFor.TotalSeconds;
            return ob;
        }

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiChunkLoaderHost)builder;
            _key = new ChunkLoaderKey(ob.Lod, new BoundingBoxI(ob.Min, ob.Max));
            NextLoadTime = TimeSpan.FromSeconds(ob.NextLoadTimeSec);
            LastLoadedFor = TimeSpan.FromSeconds(ob.LastLoadedForSec);
            if (_position != null) EnforceLocation();
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiChunkLoaderHost : MyObjectBuilder_EntityComponent
    {
        [XmlAttribute]
        public int Lod;

        [XmlElement]
        public SerializableVector3I Min;

        [XmlElement]
        public SerializableVector3I Max;

        [XmlElement]
        public double NextLoadTimeSec;

        [XmlElement]
        public double LastLoadedForSec;
    }
}