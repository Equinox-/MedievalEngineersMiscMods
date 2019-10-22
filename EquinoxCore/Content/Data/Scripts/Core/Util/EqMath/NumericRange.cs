using System.Xml.Serialization;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    public struct NumericRange
    {
        [XmlAttribute]
        public float Min;
        
        [XmlAttribute]
        public float Max;
        
        public float Clamp(float val)
        {
            return MathHelper.Clamp(val, Min, Max);
        }
        
        public static NumericRange operator *(NumericRange a, float b)
        {
            return new NumericRange() {Min = a.Min * b, Max = a.Max * b};
        }
    }
}