using System.Xml.Serialization;
using ProtoBuf;
using VRage.Serialization;
using VRageMath;

// ReSharper disable InconsistentNaming
// ReSharper disable ExplicitCallerInfoArgument
// ReSharper disable MemberCanBePrivate.Global

namespace Equinox76561198048419394.Core.Util
{
    [ProtoContract]
    public struct SerializableVector4
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public bool ShouldSerializeX() => false;

        public bool ShouldSerializeY() => false;

        public bool ShouldSerializeZ() => false;

        public bool ShouldSerializeW() => false;

        public SerializableVector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        [ProtoMember(1)]
        [XmlAttribute]
        [NoSerialize]
        public float x
        {
            get => X;
            set => X = value;
        }

        [ProtoMember(2)]
        [XmlAttribute]
        [NoSerialize]
        public float y
        {
            get => Y;
            set => Y = value;
        }

        [ProtoMember(3)]
        [NoSerialize]
        [XmlAttribute]
        public float z
        {
            get => Z;
            set => Z = value;
        }

        [ProtoMember(4)]
        [NoSerialize]
        [XmlAttribute]
        public float w
        {
            get => W;
            set => W = value;
        }

        public static implicit operator Vector4(SerializableVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

        public static implicit operator SerializableVector4(Vector4 v) => new SerializableVector4(v.X, v.Y, v.Z, v.W);
    }
}