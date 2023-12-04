using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawCollisionObjects
    {
        [XmlElement("CollisionObject", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawCollisionObjectsCollisionObject[] CollisionObject { get; set; }


        [XmlAttribute]
        public string Count { get; set; }
    }


    public class SpeedTreeRawCollisionObjectsCollisionObject
    {
        [XmlAttribute]
        public string Type { get; set; }


        [XmlAttribute]
        public string Pos1X { get; set; }


        [XmlAttribute]
        public string Pos1Y { get; set; }


        [XmlAttribute]
        public string Pos1Z { get; set; }


        [XmlAttribute]
        public string Pos2X { get; set; }


        [XmlAttribute]
        public string Pos2Y { get; set; }


        [XmlAttribute]
        public string Pos2Z { get; set; }


        [XmlAttribute]
        public string Radius { get; set; }


        [XmlAttribute]
        public string UserData { get; set; }
    }
}