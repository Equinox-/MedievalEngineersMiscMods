using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeRawWind
    {
        [XmlElement("Common", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWindCommon[] Common { get; set; }


        [XmlElement("Shared", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWindShared[] Shared { get; set; }


        [XmlElement("Branch1", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWindBranch1[] Branch1 { get; set; }


        [XmlElement("Branch2", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWindBranch2[] Branch2 { get; set; }


        [XmlElement("Ripple", Form = XmlSchemaForm.Unqualified)]
        public SpeedTreeRawWindRipple[] Ripple { get; set; }


        [XmlAttribute]
        public string Version { get; set; }
    }

    public class SpeedTreeRawWindCommon
    {
        [XmlAttribute]
        public string StrengthResponse { get; set; }


        [XmlAttribute]
        public string DirectionResponse { get; set; }


        [XmlAttribute]
        public string GustFrequency { get; set; }


        [XmlAttribute]
        public string GustStrengthMin { get; set; }


        [XmlAttribute]
        public string GustStrengthMax { get; set; }


        [XmlAttribute]
        public string GustDurationMin { get; set; }


        [XmlAttribute]
        public string GustDurationMax { get; set; }


        [XmlAttribute]
        public string GustRiseScalar { get; set; }


        [XmlAttribute]
        public string GustFallScalar { get; set; }
    }

    public class SpeedTreeRawWindShared
    {
        [XmlElement("Curve", IsNullable = true)]
        public Curve[] Curve { get; set; }


        [XmlAttribute]
        public string Enabled { get; set; }


        [XmlAttribute]
        public string Independence { get; set; }


        [XmlAttribute]
        public string HeightStart { get; set; }
    }

    public class SpeedTreeRawWindBranch1
    {
        [XmlElement("Curve", IsNullable = true)]
        public Curve[] Curve { get; set; }


        [XmlAttribute]
        public string Enabled { get; set; }


        [XmlAttribute]
        public string Independence { get; set; }


        [XmlAttribute]
        public string StretchLimit { get; set; }
    }

    public class SpeedTreeRawWindBranch2
    {
        [XmlElement("Curve", IsNullable = true)]
        public Curve[] Curve { get; set; }


        [XmlAttribute]
        public string Enabled { get; set; }


        [XmlAttribute]
        public string Independence { get; set; }


        [XmlAttribute]
        public string StretchLimit { get; set; }
    }


    public class SpeedTreeRawWindRipple
    {
        [XmlElement("Curve", IsNullable = true)]
        public Curve[] Curve { get; set; }


        [XmlAttribute]
        public string Enabled { get; set; }


        [XmlAttribute]
        public string ShimmerEnabled { get; set; }


        [XmlAttribute]
        public string Shimmer { get; set; }


        [XmlAttribute]
        public string Independence { get; set; }
    }

    public class Curve
    {
        [XmlAttributeAttribute]
        public string Name { get; set; }


        [XmlTextAttribute]
        public string Value { get; set; }
    }
}