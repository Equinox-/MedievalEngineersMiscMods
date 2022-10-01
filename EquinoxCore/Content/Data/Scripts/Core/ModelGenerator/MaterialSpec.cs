using System.Collections.Generic;
using System.Xml.Serialization;
using VRageRender.Import;

namespace Equinox76561198048419394.Core.ModelGenerator
{
    public sealed class MaterialSpec
    {
        [XmlElement("Parameter")]
        public List<Parameter> Parameters;

        [XmlElement("IconResolution")]
        public int? IconResolution;
        
        [XmlElement("Icon")]
        public List<string> Icons;

        public struct Parameter
        {
            [XmlAttribute("Name")]
            public string Name;

            [XmlText]
            public string Value;
        }

        public MaterialDescriptor Build()
        {
            return MaterialTable.From(this);
        }
    }
}