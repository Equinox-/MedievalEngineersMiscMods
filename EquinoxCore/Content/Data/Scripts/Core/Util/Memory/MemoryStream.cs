using System;
using System.IO;
using VRageMath;

namespace Equinox76561198048419394.Core.Util.Memory
{
    public class MemoryStream : Stream
    {
        private byte[] _buffer;
        private long _position;
        private long _length;

        public MemoryStream(int capacity)
        {
            _buffer = new byte[capacity];
            _position = 0;
            _length = 0;
        }
        
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case  SeekOrigin.End:
                    _position = _length + offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.Begin:
                default:
                    _position = offset;
                    break;
            }
            return _position;
        }

        public override void SetLength(long value)
        {
            _length = value;
            if (value < _buffer.LongLength)
            {
                Array.Resize(ref _buffer, MathHelper.GetNearestBiggerPowerOfTwo(checked((int) _length)));
            }
        }

        private void EnsureRemaining(int n)
        {
            if (_position + n <= _buffer.LongLength)
            {
                return;
            }
            Array.Resize(ref _buffer, MathHelper.GetNearestBiggerPowerOfTwo(_buffer.Length));
        }

        public override int ReadByte()
        {
            if (_position >= _length)
                throw new IndexOutOfRangeException();
            return _buffer[_position++];
        }

        public override void WriteByte(byte value)
        {
            EnsureRemaining(1);
            _buffer[_position++] = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var readable = Math.Min(count, _length - _position);
            if (readable == 0)
            {
                return 0;
            }
            Array.Copy(_buffer, _position, buffer, offset, readable);
            _position += readable;
            return checked((int) readable);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureRemaining(count);
            Array.Copy(buffer, offset, _buffer, _position, count);
            _position += count;
            _length = Math.Max(_length, _position);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public byte[] GetBuffer()
        {
            return _buffer;
        }
    }
}