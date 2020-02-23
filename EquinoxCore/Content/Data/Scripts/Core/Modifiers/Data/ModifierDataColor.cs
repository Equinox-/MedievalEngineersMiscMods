using System;
using System.Diagnostics;
using Equinox76561198048419394.Core.Util;
using ObjectBuilders.Definitions.GUI;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Data
{
    public class ModifierDataColor : IModifierData
    {
        private static readonly LruCache<string, ModifierDataColor> DataCache = new LruCache<string, ModifierDataColor>(16384);
        
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

        public readonly ColorDefinitionHSV Color;

        public ModifierDataColor(ColorDefinitionHSV hsv)
        {
            Color = hsv;
        }

        public static ModifierDataColor Deserialize(string data)
        {
            if (data == null || data.Length != 6)
                return null;
            return DataCache.GetOrCreate(data, dc =>
            {
                var h = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) dc[0])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) dc[1])]);
                var s = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) dc[2])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) dc[3])]);
                var v = (byte) ((_charToBitsLut[Math.Min(0xFF, (int) dc[4])] << 4) | _charToBitsLut[Math.Min(0xFF, (int) dc[5])]);
                var color = default(ColorDefinitionHSV);
                color.H = h * 360 / 0xFF;
                color.S = (s * 200 / 0xFF) - 100;
                color.V = (v * 200 / 0xFF) - 100;
                return new ModifierDataColor(color);
            });
        }

        public string Serialize()
        {
            var buffer = new char[6];
            var h = (byte) ((Color.H % 360) * 0xFF / 360);
            var s = (byte) ((Color.S + 100) * 0xFF / 200);
            var v = (byte) ((Color.V + 100) * 0xFF / 200);
            buffer[0] = _bitsToCharLut[(h >> 4) & 0xF];
            buffer[1] = _bitsToCharLut[(h >> 0) & 0xF];
            buffer[2] = _bitsToCharLut[(s >> 4) & 0xF];
            buffer[3] = _bitsToCharLut[(s >> 0) & 0xF];
            buffer[4] = _bitsToCharLut[(v >> 4) & 0xF];
            buffer[5] = _bitsToCharLut[(v >> 0) & 0xF];
            return new string(buffer);
        }
    }
}