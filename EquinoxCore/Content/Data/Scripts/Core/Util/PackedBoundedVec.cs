using System;
using VRageMath;

namespace Equinox76561198048419394.Core.Util
{
    /// <summary>
    /// Utility for packing vectors in a fixed range to a fix bit value.
    /// </summary>
    public class PackedBoundedVec
    {
        private readonly float _packingOffset;
        private readonly float _packingMultiplier;
        private readonly float _unpackingMultiplier;
        private readonly uint _packingMask;
        private readonly float _packingMax;
        private readonly int _componentsBits;
        private readonly int _componentsBits2;

        public PackedBoundedVec(float minValue, float maxValue, int componentBits)
        {
            if (componentBits * 3 >= 32) throw new ArgumentException("Too many component bits for 32 bit values");
            _componentsBits = componentBits;
            _componentsBits2 = 2 * componentBits;
            _packingMask = (1u << componentBits) - 1;
            _packingMax = _packingMask;
            _packingOffset = minValue;
            var width = maxValue - minValue;
            _packingMultiplier = _packingMax / width;
            _unpackingMultiplier = width / _packingMax;
        }

        private uint PackFloat(float val) => (uint)MathHelper.Clamp((val - _packingOffset) * _packingMultiplier, 0, _packingMax);
        private float UnpackFloat(uint val) => (val & _packingMask) * _unpackingMultiplier + _packingOffset;

        public uint Pack(Vector3 val) =>
            (PackFloat(val.X) << _componentsBits2)
            | (PackFloat(val.Y) << _componentsBits)
            | PackFloat(val.Z);

        public Vector3 Unpack(uint packed) => new Vector3(
            UnpackFloat(packed >> _componentsBits2),
            UnpackFloat(packed >> _componentsBits),
            UnpackFloat(packed));
    }
}