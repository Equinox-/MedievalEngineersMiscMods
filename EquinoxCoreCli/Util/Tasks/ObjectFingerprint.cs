using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    using CalculatorCacheKey = ValueTuple<Type, Type>;

    public readonly struct ObjectFingerprint : IEquatable<ObjectFingerprint>
    {
        public readonly string Hash;

        public ObjectFingerprint(string hash) => Hash = hash;

        public bool Equals(ObjectFingerprint other) => Hash == other.Hash;

        public override bool Equals(object obj) => obj is ObjectFingerprint other && Equals(other);

        public override int GetHashCode() => Hash.GetHashCode();

        public override string ToString() => Hash;
    }

    public interface IObjectFingerprintSettings
    {
        /// <summary>
        /// JSON converter to use.
        /// </summary>
        public Type Converter { get; }

        /// <summary>
        /// Force objects assignable to this interfaces to be serialized only accessing the properties on the interface.
        /// </summary>
        public Type ForcedInterface { get; }
    }

    public sealed class ObjectFingerprintCalculator
    {
        private static readonly ConcurrentDictionary<CalculatorCacheKey, ObjectFingerprintCalculator> Cache =
            new ConcurrentDictionary<CalculatorCacheKey, ObjectFingerprintCalculator>(EqualityUtils.Default<CalculatorCacheKey>());

        private const int HashSize = 20;

        private readonly JsonSerializer _serializer;

        // (Converters, ForcedInterfaces)
        public static ObjectFingerprintCalculator Of(IObjectFingerprintSettings settings = null) => Cache.GetOrAdd(
            new CalculatorCacheKey(settings?.Converter, settings?.ForcedInterface),
            key => new ObjectFingerprintCalculator(key));

        private ObjectFingerprintCalculator(CalculatorCacheKey settings)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                Converters = { new UnorderedConverter(this), new OrderedConverter(this) },
                ContractResolver = new ForcedInterfacesContractResolver(settings.Item2 != null ? new[] { settings.Item2 } : Type.EmptyTypes)
            };
            if (settings.Item1 != null)
                jsonSettings.Converters.Add((JsonConverter)Activator.CreateInstance(settings.Item1));
            _serializer = JsonSerializer.Create(jsonSettings);
        }

        private sealed class ForcedInterfacesContractResolver : DefaultContractResolver
        {
            private readonly Type[] _forcedInterfaces;

            public ForcedInterfacesContractResolver(Type[] forcedInterfaces) => _forcedInterfaces = forcedInterfaces;

            public override JsonContract ResolveContract(Type type)
            {
                foreach (var candidate in _forcedInterfaces)
                    if (candidate.IsAssignableFrom(type))
                        return base.ResolveContract(candidate);
                return base.ResolveContract(type);
            }
        }

        public ObjectFingerprint Calculate(object value) => new ObjectFingerprint(Convert.ToBase64String(ComputeInternal(value)));

        private byte[] ComputeInternal(object value)
        {
            var hasher = SHA1.Create();
            hasher.Initialize();
            using var json = new JsonTextWriter(new StreamWriter(new HashStream(hasher)));
            _serializer.Serialize(json, value);
            json.Flush();
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return hasher.Hash;
        }

        private sealed class HashStream : Stream
        {
            private readonly SHA1 _hasher;

            public HashStream(SHA1 hasher) => _hasher = hasher;

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => _hasher.TransformBlock(buffer, offset, count, null, 0);

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotImplementedException();

            public override long Position
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }
        }

        private class UnorderedConverter : JsonConverter
        {
            private readonly ObjectFingerprintCalculator _owner;

            public UnorderedConverter(ObjectFingerprintCalculator owner) => _owner = owner;

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var merged = new byte[HashSize];
                if (value != null)
                    foreach (var item in (IEnumerable)value)
                    {
                        var itemHash = _owner.ComputeInternal(item);
                        for (var i = 0; i < HashSize; i++)
                            merged[i] ^= itemHash[i];
                    }

                writer.WriteValue(merged);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
                throw new NotImplementedException();

            public override bool CanConvert(Type objectType)
            {
                return objectType.TryGetGenericBase(typeof(IDictionary<,>), out _) || objectType.TryGetGenericBase(typeof(IReadOnlyList<>), out _);
            }
        }

        private class OrderedConverter : JsonConverter
        {
            private readonly ObjectFingerprintCalculator _owner;

            public OrderedConverter(ObjectFingerprintCalculator owner) => _owner = owner;

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var merged = new byte[HashSize];
                if (value != null)
                    foreach (var item in (IEnumerable)value)
                    {
                        var itemHash = _owner.ComputeInternal(item);
                        for (var i = 0; i < HashSize; i++)
                            merged[i] = (byte)(merged[i] * 397 + itemHash[i]);
                    }

                writer.WriteValue(merged);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
                throw new NotImplementedException();

            public override bool CanConvert(Type objectType) => objectType.TryGetGenericBase(typeof(IReadOnlyList<>), out _);
        }
    }
}