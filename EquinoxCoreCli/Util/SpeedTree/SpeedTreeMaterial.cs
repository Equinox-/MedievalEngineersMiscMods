using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawMaterials
    {
        [XmlElement("Material", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawMaterialsMaterial[] Material { get; set; }


        [XmlAttribute]
        public int Count { get; set; }
    }

    public class SpeedTreeRawMaterialsMaterial
    {
        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public string SourceAssets { get; set; }


        [XmlElement("Map", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawMaterialsMaterialMap[] Map { get; set; }


        [XmlAttribute]
        public string ID { get; set; }


        [XmlAttribute]
        public string Name { get; set; }


        [XmlAttribute]
        public int TwoSided { get; set; }


        [XmlAttribute]
        public string UserData { get; set; }


        [XmlAttribute]
        public float VertexOpacity { get; set; }
    }


    public class SpeedTreeRawMaterialsMaterialMap
    {
        [XmlAttribute]
        public string Index { get; set; }


        [XmlAttribute]
        public string File { get; set; }
    }
}