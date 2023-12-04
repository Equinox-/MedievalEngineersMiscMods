using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.SpeedTree
{
    public class SpeedTreeArray<T> : IXmlSerializable
    {
        public T[] Value;

        public XmlSchema GetSchema() => throw new NotImplementedException();

        public void ReadXml(XmlReader reader)
        {
            var temp = new List<T>();

            var span = reader.ReadElementString().AsSpan();
            var offset = 0;
            while (true)
            {
                var search = span.Slice(offset).IndexOfAny(' ', '\t', '\n');
                if (search == -1)
                    break;
                if (search == 0)
                {
                    offset++;
                    continue;
                }
                var next = search + offset;
                var component = span.Slice(offset, search);
                if (typeof(T) == typeof(float))
                    temp.Add((T)(object)float.Parse(component.ToString()));
                else if (typeof(T) == typeof(int))
                    temp.Add((T)(object)int.Parse(component.ToString()));
                else
                    throw new Exception($"Error reading {typeof(T)}");
                offset = next + 1;
            }

            Value = temp.ToArray();
        }

        public void WriteXml(XmlWriter writer) => writer.WriteString(string.Join(" ", Value));

        public T this[int i] => Value[i];
    }
}