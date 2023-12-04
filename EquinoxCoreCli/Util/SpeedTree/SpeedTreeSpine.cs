using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawSpinesSpine
    {
        [XmlAttribute]
        public int Count { get; set; }

        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> X { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Y { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Z { get; set; }


        [XmlElement(Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeArray<float> Radius { get; set; }
    }
}