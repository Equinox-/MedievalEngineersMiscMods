using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game.Components;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Logging;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Scene;

namespace Equinox76561198048419394.Cartography.Data.Cartographic
{
    public readonly struct EquiCartographicDataId : IEquatable<EquiCartographicDataId>
    {
        public static readonly EquiCartographicDataId Null = new EquiCartographicDataId(0);

        public readonly ulong HostEntity;

        public EquiCartographicDataId(ulong hostEntity) => HostEntity = hostEntity;

        public bool Equals(EquiCartographicDataId other) => HostEntity == other.HostEntity;

        public override bool Equals(object obj) => obj is EquiCartographicDataId other && Equals(other);

        public override int GetHashCode() => HostEntity.GetHashCode();

        public override string ToString() => $"Carto[{HostEntity:X}]";
    }

    [MyComponent(typeof(MyObjectBuilder_EquiCartographicData))]
    [ReplicatedComponent(typeof(EquiCartographicDataReplicable))]
    public class EquiCartographicData : MyEntityComponent, IMyEventProxy
    {
        private readonly Dictionary<ulong, EquiCartographicElement> _elements = new Dictionary<ulong, EquiCartographicElement>();
        public EquiCartographicDataId DataId => new EquiCartographicDataId(Entity.Id.Value);
        public EntityId Planet { get; private set; }
        public bool Immutable;

        public void AddElement(EquiCartographicElement element)
        {
            _elements.Add(element.Id, element);
        }

        public override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
        }

        public override bool IsSerialized => true;

        public override void Deserialize(MyObjectBuilder_EntityComponent builder)
        {
            base.Deserialize(builder);
            var ob = (MyObjectBuilder_EquiCartographicData)builder;
            Planet = ob.Planet;
            Immutable = ob.Immutable;

            _elements.Clear();
            if (ob.Routes != null)
                foreach (var route in ob.Routes)
                    _elements[route.Id] = EquiCartographicRoute.Deserialize(route);
        }

        public override MyObjectBuilder_EntityComponent Serialize(bool copy = false)
        {
            var ob = (MyObjectBuilder_EquiCartographicData)base.Serialize(copy);
            ob.Planet = Planet.Value;
            ob.Immutable = Immutable;
            ob.Routes = new List<MyObjectBuilder_CartographicRoute>();
            foreach (var entry in _elements.Values)
                switch (entry)
                {
                    case EquiCartographicRoute route:
                        ob.Routes.Add(route.Serialize());
                        break;
                }

            return ob;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_EquiCartographicData : MyObjectBuilder_EntityComponent
    {
        [XmlAttribute]
        public ulong Planet;

        public bool Immutable;

        [XmlElement("Route")]
        public List<MyObjectBuilder_CartographicRoute> Routes;
    }
}