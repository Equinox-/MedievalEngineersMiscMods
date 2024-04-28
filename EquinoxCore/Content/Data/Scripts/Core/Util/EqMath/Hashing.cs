using System;

namespace Equinox76561198048419394.Core.Util.EqMath
{
    // MurmurHash based on https://github.com/aappleby/smhasher/blob/master/src/MurmurHash3.cpp
    public static class Hashing
    {
        // ReSharper disable StringLiteralTypo
        private static readonly char[] ToStringBuffer = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray();
        // ReSharper restore StringLiteralTypo

        public struct Hash128 : IEquatable<Hash128>
        {
            public readonly ulong V0, V1;

            public Hash128(ulong v0, ulong v1)
            {
                V0 = v0;
                V1 = v1;
            }

            public bool Equals(Hash128 other)
            {
                return V0 == other.V0 && V1 == other.V1;
            }

            public override bool Equals(object obj)
            {
                return obj is Hash128 other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int) V0;
                    hashCode = (hashCode * 397) ^ (int) V1;
                    return hashCode;
                }
            }

            public Hash128 Combine(in Hash128 other)
            {
                var builder = new HashBuilder(Seed);
                builder.Add(V0, V1);
                builder.Add(other.V0, other.V1);
                return builder.Build();
            }

            private ulong GetShifted(int offset)
            {
                offset %= sizeof(ulong) * 16;
                return (offset >= 64 ? 0 : (V0 >> offset)) | (offset > 0 ? (V1 << (64 - offset)) : 0);
            }

            private static ulong SignedShift(ulong v, int k)
            {
                return k > 0 ? (v << k) : (v >> -k);
            }

            public string ToLimitedString(int len)
            {
                var result = new ulong[len];
                // Mix all bits in the hash across the entire string
                for (int j = 0, bitOffset = 0; bitOffset < sizeof(ulong) * 16 || j < len; j++, bitOffset++)
                    result[j % len] ^= GetShifted(bitOffset);

                var cbuf = new char[len];
                for (var j = 0; j < len; j++)
                    cbuf[j] = ToStringBuffer[result[j] % (ulong) ToStringBuffer.Length];
                return new string(cbuf);
            }

            public string ToCompactString()
            {
                const int bitsPerChar = 6;
                const int bitsPerCharMask = (1 << bitsPerChar) - 1;
                const int charsPerLong = (64 + bitsPerChar - 1) / bitsPerChar;
                var sb = new char[charsPerLong*2];
                void Append(int offset, ulong value)
                {
                    for (int i = 0, j = 0; i < charsPerLong; i++, j+=bitsPerChar)
                        sb[offset + i] = ToStringBuffer[(value >> j) & bitsPerCharMask];
                }
                Append(0, V0);
                Append(charsPerLong, V1);
                return new string(sb);
            }

            public override string ToString()
            {
                return $"{V0:X16}{V1:X16}";
            }
        }

        private static ulong RotL64(ulong x, int r)
        {
            return (x << r) | (x >> (64 - r));
        }

        private static ulong FMix64(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccdUL;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53UL;
            k ^= k >> 33;
            return k;
        }

        private const ulong Seed = 0x6f02e9ee7427ba2bUL;
        private const ulong Filler = 0x199bff89cb700a15UL;

        public static Hash128 MurmurHash3_128(ulong seed, ulong[] data, int offset, int count)
        {
            var blockCount = count / 2;
            if ((count & 1) != 0)
                throw new NotSupportedException("Not allowed to compute murmur hash of non-even set of longs");

            var builder = new HashBuilder(seed);

            for (var i = 0; i < blockCount; i++) builder.Add(data[i * 2], data[i * 2 + 1]);
            return builder.Build();
        }

        public static HashBuilder Builder()
        {
            return new HashBuilder(Seed);
        }

        public struct HashBuilder
        {
            private ulong _count;
            private ulong _h1, _h2;

            private byte _wipBytes;
            private ulong _wipH1, _wipH2;

            public HashBuilder(ulong seed)
            {
                _h1 = _h2 = seed;
                _count = 0;
                _wipBytes = 0;
                _wipH1 = _wipH2 = 0;
            }

            public void Add(in Hash128 hash)
            {
                Add(hash.V0, hash.V1);
            }

            public void Add(ulong[] array)
            {
                int i;
                for (i = 0; i < array.Length / 2; i++)
                    Add(array[i], array[i + 1]);
                if (i < array.Length)
                    Add((long) array[i]);
            }

            public void Add(string s)
            {
                if (string.IsNullOrEmpty(s))
                {
                    Add(Filler, Filler ^ Seed);
                    return;
                }

                foreach (var c in s)
                    Add(c);
            }

            public void Add(long c)
            {
                if (_wipBytes > sizeof(ulong) * 2 - sizeof(long))
                {
                    for (var h = 0; h < sizeof(long); h++)
                        Add((byte) (c >> (h * 8)));
                    return;
                }

                if (_wipBytes < sizeof(ulong))
                    _wipH1 |= (ulong) c << (_wipBytes * 8);
                else
                    _wipH2 |= (ulong) c << (_wipBytes * 8);
                _wipBytes += sizeof(long);
                if (_wipBytes < sizeof(ulong) * 2)
                    return;
                Add(_wipH1, _wipH2);
                _wipBytes = 0;
                _wipH1 = _wipH2 = 0;
            }

            public void Add(int c)
            {
                if (_wipBytes > sizeof(ulong) * 2 - sizeof(int))
                {
                    for (var h = 0; h < sizeof(int); h++)
                        Add((byte) (c >> (h * 8)));
                    return;
                }

                if (_wipBytes < sizeof(ulong))
                    _wipH1 |= (ulong) c << (_wipBytes * 8);
                else
                    _wipH2 |= (ulong) c << (_wipBytes * 8);
                _wipBytes += sizeof(int);
                if (_wipBytes < sizeof(ulong) * 2)
                    return;
                Add(_wipH1, _wipH2);
                _wipBytes = 0;
                _wipH1 = _wipH2 = 0;
            }

            public void Add(char c)
            {
                if (_wipBytes > sizeof(ulong) * 2 - sizeof(char))
                {
                    Add((byte) c);
                    Add((byte) (c >> 8));
                    return;
                }

                if (_wipBytes < sizeof(ulong))
                    _wipH1 |= (ulong) c << (_wipBytes * 8);
                else
                    _wipH2 |= (ulong) c << (_wipBytes * 8);
                _wipBytes += sizeof(char);
                if (_wipBytes < sizeof(ulong) * 2)
                    return;
                Add(_wipH1, _wipH2);
                _wipBytes = 0;
                _wipH1 = _wipH2 = 0;
            }

            public void Add(byte b)
            {
                if (_wipBytes < sizeof(ulong))
                    _wipH1 |= (ulong) b << (_wipBytes * 8);
                else
                    _wipH2 |= (ulong) b << (_wipBytes * 8);
                _wipBytes += sizeof(byte);
                if (_wipBytes < sizeof(ulong) * 2)
                    return;
                Add(_wipH1, _wipH2);
                _wipBytes = 0;
                _wipH1 = _wipH2 = 0;
            }

            public void Add(float f) => Add(BitConverter.DoubleToInt64Bits(f));

            public void Add(double f) => Add(BitConverter.DoubleToInt64Bits(f));

            public void Add(bool v) => Add(v ? (byte)1 : (byte)0);

            public void Add(ulong k1, ulong k2)
            {
                _count += 16;

                const ulong c1 = 0x87c37b91114253d5;
                const ulong c2 = 0x4cf5ad432745937f;

                k1 *= c1;
                k1 = RotL64(k1, 31);
                k1 *= c2;
                _h1 ^= k1;

                _h1 = RotL64(_h1, 27);
                _h1 += _h2;
                _h1 = _h1 * 5 + 0x52dce729;

                k2 *= c2;
                k2 = RotL64(k2, 33);
                k2 *= c1;
                _h2 ^= k2;

                _h2 = RotL64(_h2, 31);
                _h2 += _h1;
                _h2 = _h2 * 5 + 0x38495ab5;
            }

            public Hash128 Build()
            {
                if (_wipBytes > sizeof(ulong))
                    Add(_wipH1, _wipH2);
                else if (_wipBytes > 0)
                    Add(_wipH1, Filler);

                _h1 ^= _count;
                _h2 ^= _count;

                _h1 += _h2;
                _h2 += _h1;

                _h1 = FMix64(_h1);
                _h2 = FMix64(_h2);

                _h1 += _h2;
                _h2 += _h1;

                return new Hash128(_h1, _h2);
            }
        }
    }
}