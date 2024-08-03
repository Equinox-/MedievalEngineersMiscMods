using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using VRage.Collections;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public sealed class AssetTaskIdentifier
    {
        private const int TaskNameFileCount = 5;
        public readonly string UniqueId;
        public readonly string Name;
        public readonly HashSetReader<string> OutputBasePaths;

        internal readonly double IdGenerationSec;

        private static readonly ThreadLocal<SHA1> Hasher = new ThreadLocal<SHA1>(SHA1.Create);

        public AssetTaskIdentifier(AssetTask task, string taskNameOverride)
        {
            MiscExt.StopwatchStart(out var stopwatch);
            var taskType = AssetTaskType.Of(task.GetType());
            var outputBasePaths = new HashSet<string>();
            var hasher = Hasher.Value;
            var hash = new byte[hasher.HashSize / 8];

            foreach (var outputProp in taskType.OutputFiles.Values)
            foreach (var outputFile in outputProp(task))
                if (outputBasePaths.Add(Path.GetFullPath(outputFile)))
                {
                    var fileNameHash = hasher.ComputeHash(Encoding.UTF8.GetBytes(task.TaskManager.RelativizePath(outputFile)));
                    for (var i = 0; i < hash.Length; i++)
                        hash[i] ^= fileNameHash[i];
                }

            UniqueId = $"{task.GetType().Name}-{Convert.ToBase64String(hash)}";
            Name = taskNameOverride ??
                   $"{task.GetType().Name}[{string.Join(", ", outputBasePaths.Take(TaskNameFileCount).Select(Path.GetFileNameWithoutExtension))}]";
            OutputBasePaths = outputBasePaths;

            IdGenerationSec = MiscExt.StopwatchReadAndReset(ref stopwatch);
        }

        public override string ToString() => Name;
    }
}