using System.Collections.Generic;
using System.Xml.Serialization;
using VRageMath;

namespace Equinox76561198048419394.Cartography.Data.Cartographic
{
    public class EquiCartographicRoute : EquiCartographicElement
    {
        private readonly List<RouteVertex> _vertices = new List<RouteVertex>();

        public EquiCartographicRoute(ulong id) : base(id)
        {
        }

        internal static EquiCartographicRoute Deserialize(MyObjectBuilder_CartographicRoute ob)
        {
            var dest = new EquiCartographicRoute(ob.Id);
            dest.DeserializeFrom(ob);
            dest._vertices.Clear();
            if (ob.Vertices != null)
            {
                dest._vertices.Capacity = ob.Vertices.Count;
                foreach (var vertex in ob.Vertices)
                    dest._vertices.Add(RouteVertex.Deserialize(vertex));
            }

            return dest;
        }

        internal MyObjectBuilder_CartographicRoute Serialize()
        {
            var ob = new MyObjectBuilder_CartographicRoute { Vertices = new List<MyObjectBuilder_CartographicRoute.RouteVertex>(_vertices.Count) };
            SerializeTo(ob);
            foreach (var vertex in _vertices)
                ob.Vertices.Add(vertex.Serialize());
            return ob;
        }

        public readonly struct RouteVertex
        {
            public readonly EquiCartographicLocation Location;

            public RouteVertex(EquiCartographicLocation location)
            {
                Location = location;
            }

            internal static RouteVertex Deserialize(MyObjectBuilder_CartographicRoute.RouteVertex ob) => new RouteVertex(new EquiCartographicLocation(
                ob.F, new Vector2D(ob.X, ob.Y), ob.E
            ));

            internal MyObjectBuilder_CartographicRoute.RouteVertex Serialize() => new MyObjectBuilder_CartographicRoute.RouteVertex
            {
                F = Location.Face,
                X = Location.TexCoords.X,
                Y = Location.TexCoords.Y,
                E = Location.ElevationFactor,
            };
        }
    }

    public class MyObjectBuilder_CartographicRoute : MyObjectBuilder_CartographicElement
    {
        [XmlElement("Vertex")]
        public List<RouteVertex> Vertices;

        public struct RouteVertex
        {
            [XmlAttribute]
            public byte F;

            [XmlAttribute]
            public double X;

            [XmlAttribute]
            public double Y;

            [XmlAttribute]
            public double E;
        }
    }
}