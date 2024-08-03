using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRage;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public abstract class AssetTaskManagerBase : IProviderFactory
    {
        /// <summary>
        /// Locked in version for providers. When the version changes providers will recompute if necessary.
        /// </summary>
        public int ProviderVersion { get; set; }

        public abstract string FingerprintPath(AssetTaskIdentifier id);
        public abstract string RelativizePath(string path);

        public abstract bool CanOutputTo(string path);
    }

    public abstract class AssetTaskManager : AssetTaskManagerBase
    {
        private readonly Dictionary<string, string> _filePrefixes = new Dictionary<string, string>();
        private readonly Dictionary<string, AssetTask> _tasks = new Dictionary<string, AssetTask>();
        private readonly Dictionary<string, AssetTask> _taskForOutput = new Dictionary<string, AssetTask>();

        protected void AddFilePrefix(string key, string path) => _filePrefixes.Add(key.ToUpperInvariant(), Path.GetFullPath(path));

        public override string RelativizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            foreach (var prefix in _filePrefixes)
                if (TryRelativePathInternal(prefix.Value, fullPath, out var relative, null))
                    return $"${{{prefix.Key}}}{Path.DirectorySeparatorChar}{relative}";
            var prefixes = string.Join("", _filePrefixes.Select(x => $"\n  {x.Key}: {x.Value}"));
            throw new Exception($"Path {path} is not relative to a known directory:{prefixes}");
        }

        protected static bool TryRelativePathInternal(string root, string path, out string result, string optionalPrefix = null)
        {
            result = default;
            var stripped = path.AsSpan();
            if (!stripped.StartsWith(root.AsSpan()) || stripped[root.Length] != Path.DirectorySeparatorChar)
                return false;
            stripped = stripped.Slice(root.Length + 1);
            if (optionalPrefix != null && stripped.StartsWith(optionalPrefix.AsSpan()) && stripped[optionalPrefix.Length] == Path.DirectorySeparatorChar)
                stripped = stripped.Slice(optionalPrefix.Length + 1);
            result = stripped.ToString();
            return true;
        }

        public T CreateTask<T>(Func<T> taskCreator) where T : AssetTask
        {
            var task = taskCreator();
            var id = task.Id;
            if (_tasks.TryGetValue(id.UniqueId, out var existing))
                return (T)existing;

            // Validate no outputs overlap verbatim.
            foreach (var outputBase in id.OutputBasePaths)
                if (_taskForOutput.TryGetValue(outputBase, out var existingOutput))
                    throw new TaskOutputOverlap($"Tasks '{id}' and '{existingOutput}' output to the same path:\n  Path: '{outputBase}'");

            foreach (var outputSelf in id.OutputBasePaths)
            foreach (var outputOther in _taskForOutput)
            {
                if (TryRelativePathInternal(outputSelf, outputOther.Key, out var relToSelf))
                    throw new TaskOutputOverlap(
                        $"Task '{id}' outputs a directory with an output from '{outputOther.Value}':\n  Directory: '{outputSelf}'\n  Relative Path: '{relToSelf}'");
                if (TryRelativePathInternal(outputOther.Key, outputSelf, out var relToOther))
                    throw new TaskOutputOverlap(
                        $"Task '{outputOther.Value}' outputs a directory with an output from '{id}':\n  Directory: '{outputOther.Key}'\n  Relative Path: '{relToOther}'");
            }

            _tasks.Add(id.UniqueId, task);
            foreach (var output in id.OutputBasePaths)
                _taskForOutput.Add(output, task);
            return task;
        }

        private sealed class TaskOutputOverlap : Exception
        {
            public TaskOutputOverlap(string msg) : base(msg)
            {
            }
        }

        public AssetTaskExecutionPlan PlanExecution()
        {
            // Resolve dependencies from input -> output mappings.
            foreach (var task in _tasks.Values)
                ResolveFileDependencies(task);
            return new AssetTaskExecutionPlan(this, _tasks.Values);
        }

        private void ResolveFileDependencies(AssetTask task)
        {
            var taskType = AssetTaskType.Of(task.GetType());
            foreach (var prop in taskType.InputFiles)
            foreach (var inputPath in prop.Value(task))
            {
                var fullInputPath = Path.GetFullPath(inputPath);
                if (_taskForOutput.TryGetValue(fullInputPath, out var outputTask))
                {
                    task.DependsOn(outputTask, $"property '{prop.Key}' references task output '{RelativizePath(fullInputPath)}'");
                    continue;
                }

                foreach (var outputPath in _taskForOutput)
                {
                    if (TryRelativePathInternal(fullInputPath, outputPath.Key, out var relToInput))
                        task.DependsOn(outputPath.Value, $"property '{prop.Key}' references task output '{relToInput}' in '{RelativizePath(fullInputPath)}'");
                    else if (TryRelativePathInternal(outputPath.Key, fullInputPath, out var relToOutput))
                        task.DependsOn(outputPath.Value, $"property '{prop.Key}' references task output '{relToOutput}' in '{RelativizePath(outputPath.Key)}'");
                }
            }
        }
    }
}