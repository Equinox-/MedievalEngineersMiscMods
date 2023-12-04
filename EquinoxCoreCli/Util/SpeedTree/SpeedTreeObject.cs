using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawObjects
    {
        [XmlElement("Object", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawObjectsObject[] Object { get; set; }


        [XmlAttribute]
        public int Count { get; set; }


        [XmlAttribute]
        public float LodNear { get; set; }


        [XmlAttribute]
        public float LodFar { get; set; }


        [XmlAttribute]
        public float BoundsMinX { get; set; }


        [XmlAttribute]
        public float BoundsMinY { get; set; }


        [XmlAttribute]
        public float BoundsMinZ { get; set; }


        [XmlAttribute]
        public float BoundsMaxX { get; set; }


        [XmlAttribute]
        public float BoundsMaxY { get; set; }


        [XmlAttribute]
        public float BoundsMaxZ { get; set; }
    }

    public class SpeedTreeRawObjectsObject
    {
        [XmlElement("Points", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawObjectsObjectPoints Points { get; set; }


        [XmlElement("Vertices", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawObjectsObjectVertices Vertices { get; set; }


        [XmlElement("Triangles", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawObjectsObjectTriangles Triangles { get; set; }


        [XmlAttribute]
        public string ID { get; set; }


        [XmlAttribute]
        public string Name { get; set; }


        [XmlAttribute]
        public float AbsX { get; set; }


        [XmlAttribute]
        public float AbsY { get; set; }


        [XmlAttribute]
        public float AbsZ { get; set; }


        [XmlAttribute]
        public float RelX { get; set; }


        [XmlAttribute]
        public float RelY { get; set; }


        [XmlAttribute]
        public float RelZ { get; set; }


        [XmlAttribute]
        public string ParentID { get; set; }


        [XmlAttribute]
        public float BoundsMinX { get; set; }


        [XmlAttribute]
        public float BoundsMinY { get; set; }


        [XmlAttribute]
        public float BoundsMinZ { get; set; }


        [XmlAttribute]
        public float BoundsMaxX { get; set; }


        [XmlAttribute]
        public float BoundsMaxY { get; set; }


        [XmlAttribute]
        public float BoundsMaxZ { get; set; }
    }
}