using System.Xml.Serialization;
using VRage;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    public struct SerializableOrientedBoundingBoxD
    {
        [XmlElement("Orientation")]
        public SerializableQuaternion Orientation;
        [XmlElement("Center")]
        public SerializableVector3D Center;
        [XmlElement("HalfExtent")]
        public SerializableVector3D HalfExtent;

        public static implicit operator OrientedBoundingBoxD(SerializableOrientedBoundingBoxD box)
        {
            return new OrientedBoundingBoxD
            {
                Orientation = box.Orientation,
                Center = box.Center,
                HalfExtent = box.HalfExtent
            };
        }

        public static implicit operator SerializableOrientedBoundingBoxD(OrientedBoundingBoxD box)
        {
            return new SerializableOrientedBoundingBoxD
            {
                Orientation = box.Orientation,
                Center = box.Center,
                HalfExtent = box.HalfExtent
            };
        }
    }
}