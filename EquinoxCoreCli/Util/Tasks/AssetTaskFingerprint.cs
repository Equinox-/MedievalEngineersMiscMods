using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    internal sealed class AssetTaskType
    {
        public static AssetTaskType Of(Type type) => Metadata.GetOrAdd(type, typeValue => new AssetTaskType(typeValue));

        private static readonly ConcurrentDictionary<Type, AssetTaskType> Metadata = new ConcurrentDictionary<Type, AssetTaskType>();

        public readonly struct InputProperty
        {
            public readonly Func<object, object> Getter;
            public readonly ObjectFingerprintCalculator FingerprintCalculator;

            public InputProperty(Func<object, object> getter, ObjectFingerprintCalculator fingerprintCalculator)
            {
                Getter = getter;
                FingerprintCalculator = fingerprintCalculator;
            }
        }

        internal readonly Dictionary<string, InputProperty> InputProperties = new Dictionary<string, InputProperty>();
        internal readonly Dictionary<string, Func<object, IEnumerable<string>>> InputFiles = new Dictionary<string, Func<object, IEnumerable<string>>>();
        internal readonly Dictionary<string, Func<object, IEnumerable<string>>> OutputFiles = new Dictionary<string, Func<object, IEnumerable<string>>>();

        private AssetTaskType(Type taskType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (var type = taskType; type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                    Process(field, field.FieldType, instance => field.GetValue(instance));
                foreach (var prop in type.GetProperties(flags))
                    Process(prop, prop.PropertyType, instance => prop.GetValue(instance));
            }

            return;

            void Process(MemberInfo member, Type memberType, Func<object, object> getter)
            {
                try
                {
                    var nestedAttr = member.GetCustomAttribute<InputNestedAttribute>();
                    if (nestedAttr != null)
                    {
                        var converter = NestedConverter(memberType, nestedAttr.Optional, out var ordered, out var elementType);
                        var elementMetadata = Of(elementType);
                        if (elementMetadata.OutputFiles.Count > 0)
                            throw new Exception(
                                $"Nested attribute with file output properties not supported: {taskType} {member.Name}.{elementMetadata.OutputFiles.Keys.First()}");
                        if (elementMetadata.InputFiles.Count == 0 && elementMetadata.InputProperties.Count == 0)
                            throw new Exception($"Nested attribute with no inputs not allowed: {taskType} {member.Name}");
                        foreach (var inputProp in elementMetadata.InputProperties)
                            InputProperties.Add(
                                $"{member.Name}.{inputProp.Key}",
                                new InputProperty(
                                    WrapFunc(instance =>
                                    {
                                        var items = converter(getter(instance)).Select(inputProp.Value.Getter);
                                        return ordered ? items : items.ToHashSet();
                                    }), inputProp.Value.FingerprintCalculator));
                        foreach (var inputFile in elementMetadata.InputFiles)
                            InputFiles.Add(
                                $"{member.Name}.{inputFile.Key}",
                                WrapFunc(instance => converter(getter(instance)).SelectMany(inputFile.Value)));
                    }

                    var inputFileAttr = member.GetCustomAttribute<InputFileAttribute>();
                    if (inputFileAttr != null)
                    {
                        var converter = FileConverter(memberType, inputFileAttr.Optional);
                        InputFiles.Add(member.Name, WrapFunc(instance => converter(getter(instance))));
                    }

                    var inputValueAttr = member.GetCustomAttribute<InputAttribute>();
                    if (inputValueAttr != null)
                    {
                        var converter = PropertyConverter(memberType, inputValueAttr.Optional);
                        var calculator = ObjectFingerprintCalculator.Of(inputValueAttr);
                        InputProperties.Add(member.Name,
                            new InputProperty(
                                WrapFunc(instance => converter(getter(instance), out var value) ? value : null),
                                calculator));
                    }

                    if (member.GetCustomAttribute<OutputFileAttribute>() != null)
                    {
                        var converter = FileConverter(memberType, false);
                        OutputFiles.Add(member.Name, WrapFunc(instance => converter(getter(instance))));
                    }
                }
                catch (Exception err)
                {
                    throw new Exception($"While processing {member.DeclaringType}.{member.Name} via {taskType}", err);
                }

                return;

                Func<object, T> WrapFunc<T>(Func<object, T> backing) => val =>
                {
                    try
                    {
                        return backing(val);
                    }
                    catch (Exception err)
                    {
                        throw new Exception($"Failed to read property '{member.Name}'", err);
                    }
                };
            }

            TryFunc<object, object> UnwrapProvider(ref Type memberType, bool optional)
            {
                if (!memberType.TryGetGenericArgument(typeof(IProvider<>), out var providedType))
                    return (object val, out object result) =>
                    {
                        result = val;
                        return true;
                    };
                memberType = providedType;
                return (object val, out object result) =>
                {
                    var provider = (IProvider<object>)val;
                    if (optional && !provider.HasValue)
                    {
                        result = default;
                        return false;
                    }

                    result = provider.Value;
                    return true;
                };
            }

            Func<object, IEnumerable<string>> FileConverter(Type memberType, bool optional)
            {
                var unwrap = UnwrapProvider(ref memberType, optional);
                if (typeof(IEnumerable<string>).IsAssignableFrom(memberType))
                    return val => unwrap(val, out var upstreamResult) ? (IEnumerable<string>)upstreamResult : Array.Empty<string>();
                if (typeof(string).IsAssignableFrom(memberType))
                    return val => unwrap(val, out var upstreamResult) ? new[] { (string)upstreamResult } : Array.Empty<string>();
                throw new Exception($"Marked as member with type {memberType} as a file but not the right type");
            }

            TryFunc<object, object> PropertyConverter(Type memberType, bool optional) => UnwrapProvider(ref memberType, optional);

            Func<object, IEnumerable<object>> NestedConverter(Type memberType, bool optional, out bool ordered, out Type elementType)
            {
                var unwrap = UnwrapProvider(ref memberType, optional);
                if (memberType.TryGetGenericArgument(typeof(IEnumerable<>), out elementType))
                {
                    ordered = !memberType.TryGetGenericArgument(typeof(ISet<>), out elementType);
                    return val => unwrap(val, out var upstreamResult) ? (IEnumerable<string>)upstreamResult : Array.Empty<string>();
                }

                ordered = false;
                return val => unwrap(val, out var upstreamResult) ? new[] { (string)upstreamResult } : Array.Empty<string>();
            }
        }
    }

    internal sealed class AssetTaskFileProperty : IEquatable<AssetTaskFileProperty>
    {
        public readonly HashSet<string> BasePaths = new HashSet<string>();
        public readonly HashSet<string> ResolvedPaths = new HashSet<string>();

        public bool Equals(AssetTaskFileProperty other)
        {
            return other != null && EqualityUtils.Set<string>().Equals(BasePaths, other.BasePaths)
                                 && EqualityUtils.Set<string>().Equals(ResolvedPaths, other.ResolvedPaths);
        }

        public override bool Equals(object obj) => obj is AssetTaskFileProperty other && Equals(other);

        public override int GetHashCode() => (EqualityUtils.Set<string>().GetHashCode(BasePaths) * 397)
                                             ^ EqualityUtils.Set<string>().GetHashCode(ResolvedPaths);
    }

    internal sealed class AssetTaskFiles : IEquatable<AssetTaskFiles>
    {
        public readonly Dictionary<string, FileFingerprint> Files = new Dictionary<string, FileFingerprint>();
        public readonly Dictionary<string, AssetTaskFileProperty> Properties = new Dictionary<string, AssetTaskFileProperty>();

        public bool Equals(AssetTaskFiles other)
        {
            return other != null && EqualityUtils.Dictionary<string, FileFingerprint>().Equals(Files, other.Files)
                                 && EqualityUtils.Dictionary<string, AssetTaskFileProperty>().Equals(Properties, other.Properties);
        }

        public override bool Equals(object obj) => obj is AssetTaskFiles other && Equals(other);

        public override int GetHashCode() => (EqualityUtils.Dictionary<string, FileFingerprint>().GetHashCode(Files) * 397)
                                             ^ EqualityUtils.Dictionary<string, AssetTaskFileProperty>().GetHashCode(Properties);
    }

    internal sealed class AssetTaskInputs : IEquatable<AssetTaskInputs>
    {
        public readonly Dictionary<string, ObjectFingerprint> InputProperties = new Dictionary<string, ObjectFingerprint>();
        public readonly AssetTaskFiles InputFiles = new AssetTaskFiles();

        public bool Equals(AssetTaskInputs other) => other != null
                                                     && EqualityUtils.Dictionary<string, ObjectFingerprint>().Equals(InputProperties, other.InputProperties)
                                                     && Equals(InputFiles, other.InputFiles);

        public override bool Equals(object obj) => obj is AssetTaskInputs other && Equals(other);

        public override int GetHashCode() => (EqualityUtils.Dictionary<string, ObjectFingerprint>().GetHashCode(InputProperties) * 397)
                                             ^ InputFiles.GetHashCode();
    }

    internal sealed class AssetTaskOutputs : IEquatable<AssetTaskOutputs>
    {
        public readonly AssetTaskFiles OutputFiles = new AssetTaskFiles();

        public bool Equals(AssetTaskOutputs other) => other != null && Equals(OutputFiles, other.OutputFiles);

        public override bool Equals(object obj) => obj is AssetTaskOutputs other && Equals(other);

        public override int GetHashCode() => OutputFiles.GetHashCode();
    }

    internal sealed class AssetTaskFingerprint : IEquatable<AssetTaskFingerprint>
    {
        public readonly AssetTaskInputs Inputs;
        public readonly AssetTaskOutputs Outputs;
        public readonly TaskTiming Timing;

        public AssetTaskFingerprint()
        {
            Inputs = new AssetTaskInputs();
            Outputs = new AssetTaskOutputs();
            Timing = new TaskTiming();
        }

        public AssetTaskFingerprint(AssetTaskInputs inputs, AssetTaskOutputs outputs, TaskTiming timing)
        {
            Inputs = inputs;
            Outputs = outputs;
            Timing = timing;
        }

        public bool Equals(AssetTaskFingerprint other) => other != null && Inputs.Equals(other.Inputs) && Outputs.Equals(other.Outputs);

        public override bool Equals(object obj) => obj is AssetTaskFingerprint other && Equals(other);

        public override int GetHashCode() => (Inputs.GetHashCode() * 397) ^ Outputs.GetHashCode();

        public static AssetTaskFingerprint ReadFrom(string path)
        {
            if (!File.Exists(path))
                return new AssetTaskFingerprint();
            try
            {
                using var reader = new JsonTextReader(new StreamReader(new FileStream(path, FileMode.Open)));
                return Serializer.Deserialize<AssetTaskFingerprint>(reader);
            }
            catch (Exception err)
            {
                Console.WriteLine("Failed to load fingerprint file\n" + err);
                return new AssetTaskFingerprint();
            }
        }

        private static readonly JsonSerializer Serializer = new JsonSerializer();

        public void WriteTo(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var writer = new JsonTextWriter(new StreamWriter(new FileStream(path, FileMode.Create)));
            writer.Formatting = Formatting.Indented;
            Serializer.Serialize(writer, this);
        }
    }

    internal sealed class AssetPropertiesDiff
    {
        public readonly HashSet<string> Files = new HashSet<string>();
        public readonly HashSet<string> Properties = new HashSet<string>();

        private void ComputeFiles(AssetTaskFiles prior, AssetTaskFiles curr)
        {
            // Compute files that changed.
            EqualityUtils.DifferingKeys(prior.Files, curr.Files, Files);

            // Compute properties with changed paths.
            EqualityUtils.DifferingKeys(prior.Properties, curr.Properties, Properties);

            // Compute properties with files that changed.
            foreach (var prop in curr.Properties)
                if (!Properties.Contains(prop.Key))
                    foreach (var file in prop.Value.ResolvedPaths)
                        if (Files.Contains(file))
                        {
                            // Property changed because a file changed.
                            Properties.Add(prop.Key);
                            break;
                        }
        }

        public static AssetPropertiesDiff Compute(AssetTaskInputs prior, AssetTaskInputs curr)
        {
            var diff = new AssetPropertiesDiff();
            diff.ComputeFiles(prior.InputFiles, curr.InputFiles);

            EqualityUtils.DifferingKeys(prior.InputProperties, curr.InputProperties, diff.Properties);
            return diff;
        }

        public static AssetPropertiesDiff Compute(AssetTaskOutputs prior, AssetTaskOutputs curr)
        {
            var diff = new AssetPropertiesDiff();
            diff.ComputeFiles(prior.OutputFiles, curr.OutputFiles);
            return diff;
        }
    }
}