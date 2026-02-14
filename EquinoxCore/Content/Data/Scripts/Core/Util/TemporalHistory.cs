using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Equinox76561198048419394.Core.Market;
using VRage.Serialization;

namespace Equinox76561198048419394.Core.Util
{
    public interface ITemporalHistoryBucket<TBucket> where TBucket : struct, ITemporalHistoryBucket<TBucket>
    {
        bool IsEmpty { get; }

        void MergeWith(in TBucket other);

        void MergeWith(in TBucket other, float fraction);

        bool DeserializeFrom(string part);

        void SerializeTo(StringBuilder sb);
    }

    public readonly struct TemporalHistoryReader<TBucket> : IEnumerable<TemporalHistory<TBucket>.Bucket> where TBucket : struct, ITemporalHistoryBucket<TBucket>
    {
        internal static readonly TBucket EmptyBucket = default;
        private readonly TemporalHistory<TBucket> _src;

        private TemporalHistoryReader(TemporalHistory<TBucket> src) => _src = src;

        public TBucket BucketsMerged => _src?.BucketsMerged ?? EmptyBucket;

        public bool IsEmpty => _src?.IsEmpty ?? false;

        public ref readonly TBucket BucketAt(DateTime time) => ref _src == null ? ref EmptyBucket : ref _src.BucketAt(time);

        public TBucket BucketOver(DateTime start, TimeSpan duration) => _src?.BucketOver(start, duration) ?? EmptyBucket;

        internal void AddTo(TemporalHistory<TBucket> target)
        {
            if (_src != null) target.Add(_src);
        }

        public TemporalHistory<TBucket> TryCopy() => _src != null ? new TemporalHistory<TBucket>(_src) : null;

        public TemporalHistory<TBucket>.Enumerator GetEnumerator() => _src?.GetEnumerator() ?? default;

        IEnumerator<TemporalHistory<TBucket>.Bucket> IEnumerable<TemporalHistory<TBucket>.Bucket>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static implicit operator TemporalHistoryReader<TBucket>(TemporalHistory<TBucket> src) => new TemporalHistoryReader<TBucket>(src);
    }

    public class TemporalHistory<TBucket> : IEnumerable<TemporalHistory<TBucket>.Bucket> where TBucket : struct, ITemporalHistoryBucket<TBucket>
    {
        // ReSharper disable once StaticMemberInGenericType
        internal static DateTime? OverrideNow;

        // In TimeSpan/DateTime Ticks
        private readonly long _bucketWidth;

        private readonly TBucket[] _buckets;

        // The start time of the last bucket is _lastBucketId * _bucketWidth.
        private long _lastBucketId;

        public TBucket BucketsMerged
        {
            get
            {
                TBucket sum = default;
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < _buckets.Length; i++)
                    sum.MergeWith(in _buckets[i]);
                return sum;
            }
        }

        public bool IsEmpty
        {
            get
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < _buckets.Length; i++)
                    if (!_buckets[i].IsEmpty)
                        return false;
                return true;
            }
        }

        public TemporalHistory(int bucketCount, TimeSpan bucketWidth)
        {
            _buckets = new TBucket[bucketCount];
            _bucketWidth = bucketWidth.Ticks;
            _lastBucketId = BucketId(OverrideNow ?? DateTime.UtcNow);
        }

        // Not safe for public consumption due to not copying the value array.
        private TemporalHistory(MyObjectBuilder_TemporalHistory<TBucket> ob)
        {
            _buckets = ob.Values;
            _bucketWidth = ob.BucketWidth;
            _lastBucketId = ob.LastBucketId;
        }

        internal TemporalHistory(TemporalHistory<TBucket> src)
        {
            _buckets = new TBucket[src._buckets.Length];
            Array.Copy(src._buckets, 0, _buckets, 0, _buckets.Length);
            _bucketWidth = src._bucketWidth;
            _lastBucketId = src._lastBucketId;
        }

        private long BucketId(DateTime time) => time.ToUniversalTime().Ticks / _bucketWidth;

        private double FractionalBucketId(DateTime time) => time.ToUniversalTime().Ticks / (double)_bucketWidth;

        private long IdToIndex => _buckets.Length - 1 - _lastBucketId;

        private void Realign()
        {
            var nowId = BucketId(OverrideNow ?? DateTime.UtcNow);
            if (nowId <= _lastBucketId) return;
            var newBuckets = nowId - _lastBucketId;

            // Fill new buckets with zeros, and shift old buckets.
            for (var to = 0; to < _buckets.Length; to++)
            {
                var from = to + newBuckets;
                _buckets[to] = from >= _buckets.Length ? default : _buckets[from];
            }

            _lastBucketId = nowId;
        }

        /// <summary>
        /// Reads the bucket value that covers the given instant.
        /// If the instant is out of bounds, an empty bucket will be returned.
        /// </summary>
        public ref readonly TBucket BucketAt(DateTime time)
        {
            var ix = BucketId(time) + IdToIndex;
            if (ix < 0 || ix >= _buckets.Length) return ref TemporalHistoryReader<TBucket>.EmptyBucket;
            return ref _buckets[ix];
        }

        /// <summary>
        /// Aggregates the buckets that cover the given time range.
        /// </summary>
        public TBucket BucketOver(DateTime start, TimeSpan length)
        {
            if (start == DateTime.MinValue && length == TimeSpan.MaxValue)
                return BucketsMerged;
            TBucket result = default;
            var enumerator = new RangedEnumerator(this, start, length);
            while (enumerator.TryMoveNext(out var i, out var lengthInBucket, out _))
                result.MergeWith(in _buckets[i], lengthInBucket);
            return result;
        }

        public void AddInstant(DateTime time, in TBucket value)
        {
            Realign();
            var ix = BucketId(time) + IdToIndex;
            if (ix < 0 || ix >= _buckets.Length) return;
            _buckets[ix].MergeWith(in value);
        }

        public void AddRange(DateTime start, TimeSpan length, in TBucket value)
        {
            if (length == TimeSpan.Zero)
            {
                AddInstant(start, value);
                return;
            }

            var enumerator = new RangedEnumerator(this, start, length);
            while (enumerator.TryMoveNext(out var i, out _, out var fractionInBucket))
                _buckets[i].MergeWith(in value, fractionInBucket);
        }

        private struct RangedEnumerator
        {
            private readonly double _from;
            private readonly double _to;
            private readonly double _fractionPerBucket;
            private readonly long _toIx;
            private long _ix;

            internal RangedEnumerator(TemporalHistory<TBucket> owner, DateTime start, TimeSpan length)
            {
                _from = owner.FractionalBucketId(start) + owner.IdToIndex;
                _to = owner.FractionalBucketId(start + length) + owner.IdToIndex;
                _fractionPerBucket = 1 / (_to - _from);

                var fromIx = Math.Max(0, (long)_from);
                _toIx = Math.Min(owner._buckets.Length - 1, (long)_to);
                _ix = fromIx - 1;
            }

            internal bool TryMoveNext(out long index, out float lengthInBucket, out float fractionInBucket)
            {
                if (_ix < _toIx)
                {
                    _ix++;
                    index = _ix;
                    lengthInBucket = (float) (Math.Min(_ix + 1, _to) - Math.Max(_ix, _from));
                    fractionInBucket = (float) (_fractionPerBucket * lengthInBucket);
                    return true;
                }

                index = 0;
                lengthInBucket = 0;
                fractionInBucket = 0;
                return false;
            }
        }

        private DateTime BucketStart(int index) => new DateTime((index - IdToIndex) * _bucketWidth, DateTimeKind.Utc);
        private DateTime BucketEnd(int index) => BucketStart(index + 1);

        public void Add(TemporalHistoryReader<TBucket> other) => other.AddTo(this);

        public void Add(TemporalHistory<TBucket> other)
        {
            if (other._bucketWidth == _bucketWidth)
            {
                var shift = other.IdToIndex - IdToIndex;
                for (var to = 0; to < _buckets.Length; to++)
                {
                    var from = to + shift;
                    if (from >= 0 && from < other._buckets.Length)
                        _buckets[to].MergeWith(in other._buckets[from]);
                }


                return;
            }

            foreach (var bucket in other)
                AddRange(bucket.Left, bucket.Right - bucket.Left, in bucket.Value);

            return;
        }

        public void Add(MyObjectBuilder_TemporalHistory<TBucket> other)
        {
            if (other != null) Add(new TemporalHistory<TBucket>(other));
        }

        public MyObjectBuilder_TemporalHistory<TBucket> Serialize(bool nullIfEmpty = false)
        {
            if (nullIfEmpty && IsEmpty) return null;
            var copy = new TBucket[_buckets.Length];
            Array.Copy(_buckets, 0, copy, 0, _buckets.Length);
            return new MyObjectBuilder_TemporalHistory<TBucket>
            {
                BucketWidth = _bucketWidth,
                LastBucketId = _lastBucketId,
                Values = copy,
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder("{\n");
            foreach (var bucket in this)
                sb.Append("  ")
                    .Append(bucket.Left)
                    .Append(" - ")
                    .Append(bucket.Right)
                    .Append(" = ")
                    .Append(bucket.Value)
                    .Append("\n");

            return sb.Append("}").ToString();
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<Bucket> IEnumerable<Bucket>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly struct Bucket
        {
            private readonly int _index;
            private readonly TemporalHistory<TBucket> _owner;

            public DateTime Left => _owner.BucketStart(_index);
            public DateTime Right => _owner.BucketEnd(_index);
            public ref readonly TBucket Value => ref _owner._buckets[_index];

            internal Bucket(TemporalHistory<TBucket> owner, int index)
            {
                _owner = owner;
                _index = index;
            }
        }

        public struct Enumerator : IEnumerator<Bucket>
        {
            private readonly TemporalHistory<TBucket> _owner;
            private long _lastBucketId;
            private int _ix;

            internal Enumerator(TemporalHistory<TBucket> owner)
            {
                _owner = owner;
                _lastBucketId = owner._lastBucketId;
                _ix = -1;
            }

            public bool MoveNext()
            {
                if (_owner == null) return false;
                if (_lastBucketId != _owner._lastBucketId) throw new InvalidOperationException("histogram modified");
                if (_ix + 1 >= _owner._buckets.Length) return false;
                _ix++;
                return true;
            }

            public void Reset()
            {
                _lastBucketId = _owner._lastBucketId;
                _ix = -1;
            }

            public Bucket Current => new Bucket(_owner, _ix);

            public void Dispose()
            {
            }

            object IEnumerator.Current => Current;
        }
    }

    public class MyObjectBuilder_TemporalHistory<TBucket> where TBucket : struct, ITemporalHistoryBucket<TBucket>
    {
        [XmlElement]
        public long LastBucketId;

        [XmlElement]
        public long BucketWidth;

        [XmlIgnore]
        [Serialize]
        public TBucket[] Values;

        [XmlElement(nameof(Values))]
        [NoSerialize]
        public string ValuesForXml
        {
            get
            {
                if (!(Values?.Length > 0)) return "";
                var sb = new StringBuilder();
                for (var i = 0; i < Values.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    Values[i].SerializeTo(sb);
                }

                return sb.ToString();
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                var chunks = value.Split(' ');
                Values = new TBucket[chunks.Length];
                var i = 0;
                foreach (var part in chunks)
                    if (Values[i].DeserializeFrom(part))
                        i++;
                Array.Resize(ref Values, i);
            }
        }
    }
}