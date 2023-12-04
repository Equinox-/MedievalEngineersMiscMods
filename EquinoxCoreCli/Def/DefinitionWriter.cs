using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Cli.Util;
using VRage.ObjectBuilders.Definitions;

namespace Equinox76561198048419394.Core.Cli.Def
{
    public static class DefinitionWriter
    {
        public static void Write(MyObjectBuilder_Definitions definitionSet, string path)
        {
            XDocument doc;
            using (var baseWriter = new StringWriter())
            {
                var serializer = new XmlSerializer(typeof(MyObjectBuilder_Definitions));
                using (var writer = new XmlTextWriter(baseWriter))
                    serializer.Serialize(writer, definitionSet);
                doc = XDocument.Load(new StringReader(baseWriter.ToString()));
            }

            NullCleaner.Clean(doc);
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var writer = new XmlTextWriter(path, Encoding.UTF8))
            {
                writer.Formatting = Formatting.Indented;
                writer.Indentation = 2;
                doc.WriteTo(writer);
            }
        }
    }
}