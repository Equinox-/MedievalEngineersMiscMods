using System;
using System.Diagnostics;
using Equinox76561198048419394.Core.Util;
using ObjectBuilders.Definitions.GUI;
using VRageMath;

namespace Equinox76561198048419394.Core.Modifiers.Data
{
    public class ModifierDataColor : IModifierData
    {
        // Legacy format: hex encoded HHSSVV len=6
        // New format: "HHH+SSS+VVV" len=11
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

        private static int CharToDigit(char c) => _charToBitsLut[Math.Min(0xFF, (int) c)];

        private static int Char3ToInt(string s, int offset)
        {
            return CharToDigit(s[offset]) * 100 + CharToDigit(s[offset + 1]) * 10 + CharToDigit(s[offset + 2]);
        }

        private static void IntToChar3(int src, char[] dest, int offset)
        {
            dest[offset] = _bitsToCharLut[(src / 100) % 10];
            dest[offset + 1] = _bitsToCharLut[(src / 10) % 10];
            dest[offset + 2] = _bitsToCharLut[(src / 1) % 10];
        }

        public readonly ColorDefinitionHSV Color;

        public ModifierDataColor(ColorDefinitionHSV hsv)
        {
            Color = hsv;
        }

        public static ModifierDataColor Deserialize(string data)
        {
            if (data == null || (data.Length != 6 && data.Length != 11))
                return null;
            return DataCache.GetOrCreate(data, dc =>
            {
                if (data.Length == 6)
                {
                    var h = (byte) ((CharToDigit(dc[0]) << 4) | CharToDigit(dc[1]));
                    var s = (byte) ((CharToDigit(dc[2]) << 4) | CharToDigit(dc[3]));
                    var v = (byte) ((CharToDigit(dc[4]) << 4) | CharToDigit(dc[5]));
                    var color = default(ColorDefinitionHSV);
                    color.H = h * 360 / 0xFF;
                    color.S = (s * 200 / 0xFF) - 100;
                    color.V = (v * 200 / 0xFF) - 100;
                    return new ModifierDataColor(color);
                }
                else
                {
                    var h = Char3ToInt(dc, 0);
                    var s = (dc[3] == '-' ? -1 : 1) * Char3ToInt(dc, 4);
                    var v = (dc[7] == '-' ? -1 : 1) * Char3ToInt(dc, 8);
                    return new ModifierDataColor(new ColorDefinitionHSV {H = h, S = s, V = v});
                }
            });
        }

        public string Serialize()
        {
            var buffer = new char[11];
            IntToChar3(Color.H % 360, buffer, 0);
            buffer[3] = Color.S < 0 ? '-' : '+';
            IntToChar3(MathHelper.Clamp(Math.Abs(Color.S), 0, 100), buffer, 4);
            buffer[7] = Color.V < 0 ? '-' : '+';
            IntToChar3(MathHelper.Clamp(Math.Abs(Color.V), 0, 100), buffer, 8);
            return new string(buffer);
        }
    }
}