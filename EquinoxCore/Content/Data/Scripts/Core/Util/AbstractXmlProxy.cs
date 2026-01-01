using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using VRage.Serialization;
using VRage.Serialization.Xml;

namespace Equinox76561198048419394.Core.Util
{
    public static class AbstractXmlProxy
    {
        public static AbstractXmlProxy<T> Wrap<T>(T src) => new AbstractXmlProxy<T>(src);

        public static AbstractXmlProxy<T>[] WrapList<T>(IReadOnlyList<T> src)
        {
            if (src == null) return null;
            var dest = new AbstractXmlProxy<T>[src.Count];
            for (var i = 0; i < src.Count; i++) dest[i] = new AbstractXmlProxy<T>(src[i]);
            return dest;
        }

        public static List<T> Unwrap<T>(IReadOnlyList<AbstractXmlProxy<T>> src) => src == null ? null : Unwrap(src, new List<T>(src.Count));

        public static TCollection Unwrap<T, TCollection>(IReadOnlyList<AbstractXmlProxy<T>> src, TCollection target, bool dropDefault = false)
            where TCollection : ICollection<T>
        {
            var equality = dropDefault ? EqualityComparer<T>.Default : null;
            if (target is List<T> list)
                list.EnsureSpace(src.Count);
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < src.Count; i++)
                if (equality == null || !equality.Equals(src[i], default))
                    target.Add(src[i]);
            return target;
        }
    }

    /// <summary>
    /// Wrapper type that allows serializing a value using <see cref="MyAbstractXmlSerializer{TAbstractBase}"/>.
    /// </summary>
    public class AbstractXmlProxy<T> : IXmlSerializable
    {
        [Serialize]
        private T _value;

        [XmlIgnore]
        [NoSerialize]
        public T Value => _value;

        // ReSharper disable once UnusedMember.Global
        public AbstractXmlProxy()
        {
        }

        public AbstractXmlProxy(T value) => _value = value;

        public static implicit operator T(AbstractXmlProxy<T> proxy) => proxy._value;

        public static implicit operator AbstractXmlProxy<T>(in T value) => new AbstractXmlProxy<T>(value);

        public XmlSchema GetSchema() => throw new NotImplementedException();

        public void ReadXml(XmlReader reader)
        {
            var ser = new MyAbstractXmlSerializer<T>();
            ser.ReadXml(reader);
            _value = (T)ser;
        }

        public void WriteXml(XmlWriter writer)
        {
            var ser = new MyAbstractXmlSerializer<T>(_value);
            ((IXmlSerializable)ser).WriteXml(writer);
        }
    }
}