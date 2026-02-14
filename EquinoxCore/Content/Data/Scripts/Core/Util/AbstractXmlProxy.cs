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
        public static AbstractXmlProxy<T> Wrap<T>(T src) where T : class => new AbstractXmlProxy<T>(src);

        public static AbstractXmlProxy<T>[] WrapList<T>(IReadOnlyList<T> src) where T : class
        {
            if (src == null) return null;
            var dest = new AbstractXmlProxy<T>[src.Count];
            for (var i = 0; i < src.Count; i++) dest[i] = new AbstractXmlProxy<T>(src[i]);
            return dest;
        }

        public static List<T> Unwrap<T>(IReadOnlyList<AbstractXmlProxy<T>> src) where T : class => src == null ? null : Unwrap(src, new List<T>(src.Count));

        public static TCollection Unwrap<T, TCollection>(IReadOnlyList<AbstractXmlProxy<T>> src, TCollection target, bool dropDefault = false) where T : class
            where TCollection : ICollection<T>
        {
            if (src == null) return target;
            if (target is List<T> list)
                list.EnsureSpace(src.Count);
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < src.Count; i++)
                if (dropDefault || src[i] != null)
                    target.Add(src[i]);
            return target;
        }

        public static T[] UnwrapArray<T>(IReadOnlyList<AbstractXmlProxy<T>> src, bool dropDefault = false) where T : class
        {
            if (src == null) return null;
            var outputCount = 0;
            var output = new T[src.Count];
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < src.Count; i++)
                if (dropDefault || src[i] != null)
                    output[outputCount++] = src[i];
            Array.Resize(ref output, outputCount);
            return output;
        }
    }

    /// <summary>
    /// Wrapper type that allows serializing a value using <see cref="MyAbstractXmlSerializer{TAbstractBase}"/>.
    /// Replace usages of <see cref="XmlElementAttribute"/> and <see cref="XmlArrayItemAttribute"/> with
    /// <code>Type = typeof(MyAbstractXmlSerializer&lt;T&gt;)</code> by removing the Type attribute and changed the object builder item
    /// to use <code>AbstractXmlProxy&lt;T&gt;</code> 
    /// </summary>
    public class AbstractXmlProxy<T> : IXmlSerializable where T : class
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

        public static implicit operator T(AbstractXmlProxy<T> proxy) => proxy?._value;

        public static implicit operator AbstractXmlProxy<T>(T value) => value != null ? new AbstractXmlProxy<T>(value) : null;

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