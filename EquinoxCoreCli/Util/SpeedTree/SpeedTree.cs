using System.IO;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    [XmlRoot("SpeedTreeRaw")]
    public class SpeedTreeRaw
    {
        public static readonly XmlSerializer Serializer = new XmlSerializer(typeof(SpeedTreeRaw));

        public static SpeedTreeRaw Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open);
            return (SpeedTreeRaw)Serializer.Deserialize(stream);
        }

        [XmlElement("Materials", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawMaterials Materials { get; set; }


        [XmlElement("Wind", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWind Wind { get; set; }


        [XmlElement("Objects", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawObjects Objects { get; set; }


        [XmlElement("CollisionObjects", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawCollisionObjects CollisionObjects { get; set; }


        [XmlElement("Bones", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawBones Bones { get; set; }


        [XmlArray(Form = XmlSchemaForm.Unqualified)]
        [XmlArrayItem("Spine", typeof(SpeedTreeRawSpinesSpine), Form = XmlSchemaForm.Unqualified, IsNullable = false)]
        public SpeedTreeRawSpinesSpine[] Spines { get; set; }


        [XmlAttribute]
        public string VersionMajor { get; set; }


        [XmlAttribute]
        public string VersionMinor { get; set; }


        [XmlAttribute]
        public string UserData { get; set; }


        [XmlAttribute]
        public string Source { get; set; }
    }
}