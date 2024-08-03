using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public readonly struct FileFingerprint : IEquatable<FileFingerprint>
    {
        public readonly long Size;
        public readonly long Time;
        public readonly string Hash;

        private static readonly ConcurrentDictionary<string, FileFingerprint> Cache = new ConcurrentDictionary<string, FileFingerprint>();

        public static FileFingerprint Compute(string path, FileFingerprint hint = default)
        {
            return Cache.AddOrUpdate(Path.GetFullPath(path), p => Recompute(p, hint), Recompute);
        }

        public FileFingerprint(long size, long time, string hash)
        {
            Size = size;
            Time = time;
            Hash = hash;
        }

        public bool Equals(FileFingerprint other) => Size == other.Size && Time == other.Time && Hash == other.Hash;

        public override bool Equals(object obj) => obj is FileFingerprint other && Equals(other);

        public override int GetHashCode()
        {
            var hashCode = Size.GetHashCode();
            hashCode = (hashCode * 397) ^ Time.GetHashCode();
            hashCode = (hashCode * 397) ^ (Hash != null ? Hash.GetHashCode() : 0);
            return hashCode;
        }

        private static readonly ThreadLocal<SHA1> Hasher = new ThreadLocal<SHA1>(SHA1.Create);

        private static FileFingerprint Recompute(string path, FileFingerprint cached)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new FileFingerprint(0, 0, "missing");

            if (cached.Hash != null && cached.Size == info.Length && cached.Time == info.LastWriteTime.Ticks)
                return cached;
            using var stream = File.OpenRead(path);
            return new FileFingerprint(info.Length, info.LastWriteTime.Ticks, Convert.ToBase64String(Hasher.Value.ComputeHash(stream)));
        }
    }
}