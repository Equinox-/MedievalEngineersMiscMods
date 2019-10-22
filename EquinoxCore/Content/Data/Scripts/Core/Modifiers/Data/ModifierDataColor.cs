using System;
using System.Diagnostics;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Data
{
    public class ModifierDataColor : IModifierData
    {
        private static readonly byte[] _charToBitsLut;
        private static readonly char[] _bitsToCharLut;

        static ModifierDataColor()
        {
            _bitsToCharLut = "0123456789ABCDEF".ToCharArray();
            _charToBitsLut = new byte[256];
            for (var i = 0; i < _bitsToCharLut.Length; i++)
            {
                var c = _bitsToCharLut[i];
                _charToBitsLut[char.ToLowerInvariant(c)] = (byte) i;
                _charToBitsLut[char.ToUpperInvariant(c)] = (byte) i;
            }
        }
        
        public Color Raw;

        public ModifierDataColor(Color raw)
        {
            Raw = raw;
            Raw.A = 0xF;
        }

        public ModifierDataColor(string data)
        {
            if (data.Length != 6)
                return;
            Raw.R = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) data[0])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) data[1])]);
            Raw.G = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) data[2])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) data[3])]);
            Raw.B = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) data[4])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) data[5])]);
            Raw.A = 0xF;
        }

        public string Serialize()
        {
            var buffer = new char[6];
            buffer[0] = _bitsToCharLut[(Raw.R >> 4) & 0xF];
            buffer[1] = _bitsToCharLut[(Raw.R >> 0) & 0xF];
            buffer[2] = _bitsToCharLut[(Raw.G >> 4) & 0xF];
            buffer[3] = _bitsToCharLut[(Raw.G >> 0) & 0xF];
            buffer[4] = _bitsToCharLut[(Raw.B >> 4) & 0xF];
            buffer[5] = _bitsToCharLut[(Raw.B >> 0) & 0xF];            
            return new string(buffer);
        }
    }
}