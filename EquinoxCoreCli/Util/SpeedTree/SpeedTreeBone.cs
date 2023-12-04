using System.Xml.Schema;
using System.Xml.Serialization;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawBones
    {
        [XmlElement("Bone", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawBonesBone[] Bone { get; set; }


        [XmlAttribute]
        public int Count { get; set; }
    }


    public class SpeedTreeRawBonesBone
    {
        [XmlAttribute]
        public int ID { get; set; }


        [XmlAttribute]
        public int ParentID { get; set; }

        public Vector3 Start => new Vector3(StartX, StartY, StartZ);
        public Vector3 End => new Vector3(EndX, EndY, EndZ);


        [XmlAttribute]
        public float Radius { get; set; }


        [XmlAttribute]
        public float StartX { get; set; }


        [XmlAttribute]
        public float StartY { get; set; }


        [XmlAttribute]
        public float StartZ { get; set; }


        [XmlAttribute]
        public float EndX { get; set; }


        [XmlAttribute]
        public float EndY { get; set; }


        [XmlAttribute]
        public float EndZ { get; set; }


        [XmlAttribute]
        public float Mass { get; set; }


        [XmlAttribute]
        public string Generator { get; set; }
    }
}