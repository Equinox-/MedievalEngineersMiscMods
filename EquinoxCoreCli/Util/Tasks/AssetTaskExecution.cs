using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Equinox76561198048419394.Core.Cli.Util.Tasks
{
    public class AssetTaskExecutionPlan
    {
        private readonly AssetTaskManager _owner;
        private readonly List<AssetTask> _order;

        public AssetTaskExecutionPlan(AssetTaskManager owner, IReadOnlyCollection<AssetTask> tasks)
        {
            _owner = owner;

            // Sort tasks.
            var orderedTasks = new HashSet<AssetTask>(EqualityUtils.ReferenceEquality);
            _order = new List<AssetTask>();

            while (orderedTasks.Count < tasks.Count)
            {
                var added = false;
                foreach (var task in tasks)
                    if (!orderedTasks.Contains(task) && task.Dependencies.All(x => orderedTasks.Contains(x.Key)))
                    {
                        _order.Add(task);
                        orderedTasks.Add(task);
                        added = true;
                    }

                if (!added)
                    throw new Exception("Circular dependency in tasks:" + string.Join("", tasks
                        .Where(x => !orderedTasks.Contains(x))
                        .Select(task =>
                        {
                            var deps = string.Join("", task.Dependencies.Select(dep => $"    dep: {dep.Key}, reason: {dep.Value}"));
                            return $"\n  {task}\n{deps}";
                        })));
            }
        }

        public async Task Execute(string reportOut = null)
        {
            var scheduled = new Dictionary<AssetTask, Task>(EqualityUtils.ReferenceEquality);
            var report = new ExecutionReport();
            foreach (var task in _order)
            {
                var deps = task.Dependencies.Select(dep => (dep.Key, dep.Value, scheduled[dep.Key])).ToArray();
                scheduled.Add(task, Task.Run(() => ExecuteOne(task, report, deps)));
            }

            await Task.WhenAll(scheduled.Values);

            if (reportOut != null)
                report.WriteTo(reportOut);
        }

        private static async Task ExecuteOne(AssetTask task, ExecutionReport report, IEnumerable<(AssetTask task, string reason, Task execution)> dependencies)
        {
            var taskReport = new TaskExecutionReport { Name = task.Id.Name };
            foreach (var dep in dependencies)
                try
                {
                    taskReport.Dependencies.Add(dep.task.Id.UniqueId, dep.reason);
                    await dep.execution;
                }
                catch (Exception err)
                {
                    throw new Exception($"Dependency {task} failed", err);
                }

            var context = new AssetTaskExecutionContext(task);
            context.Execute(out var skipped, taskReport.Timing);
            taskReport.Skipped = skipped;
            lock (report)
            {
                report.Tasks.Add(task.Id.UniqueId, taskReport);
            }
        }
    }

    public sealed class AssetTaskExecutionContext
    {
        private const int TaskDifferencesPropertyCount = 5;

        private readonly AssetTask _task;
        private readonly AssetTaskType _metadata;

        public AssetTaskExecutionContext(AssetTask task)
        {
            _task = task;
            _metadata = AssetTaskType.Of(task.GetType());
        }

        public void Execute(out bool skipped, TaskTiming timing)
        {
            var id = _task.Id;
            var fingerprintFile = _task.TaskManager.FingerprintPath(id);

            skipped = false;
            timing.IdentifierSec = id.IdGenerationSec;

            MiscExt.StopwatchStart(out var stopwatch);

            var lastRun = AssetTaskFingerprint.ReadFrom(fingerprintFile);
            var beforeExecute = new AssetTaskFingerprint();
            FingerprintInputs(beforeExecute.Inputs, lastRun.Inputs);
            FingerprintOutputs(beforeExecute.Outputs, lastRun.Outputs);

            timing.PreExecutionFingerprintSec = MiscExt.StopwatchReadAndReset(ref stopwatch);

            if (lastRun.Equals(beforeExecute))
            {
                skipped = true;
                timing.ExecutionSec = lastRun.Timing.ExecutionSec;
                timing.PostExecutionFingerprintSec = lastRun.Timing.PostExecutionFingerprintSec;
                Console.WriteLine($"[Skip] {id.Name}");
                return;
            }

            var inputDiff = AssetPropertiesDiff.Compute(lastRun.Inputs, beforeExecute.Inputs);
            var outputDiff = AssetPropertiesDiff.Compute(lastRun.Outputs, beforeExecute.Outputs);
            Console.WriteLine($"[Start] {id.Name}");
            WriteDifferences("Input", inputDiff);
            WriteDifferences("Output", outputDiff);

            _task.ExecuteFromContext();

            timing.ExecutionSec = MiscExt.StopwatchReadAndReset(ref stopwatch);

            var afterExecute = new AssetTaskFingerprint(beforeExecute.Inputs, new AssetTaskOutputs(), timing);
            Console.WriteLine($"[Done] {id.Name} in {timing.ExecutionSec:F4} s");

            FingerprintOutputs(afterExecute.Outputs, beforeExecute.Outputs);

            timing.PostExecutionFingerprintSec = MiscExt.StopwatchReadAndReset(ref stopwatch);

            afterExecute.WriteTo(fingerprintFile);
            return;

            void WriteDifferences(string name, AssetPropertiesDiff diff)
            {
                var i = 0;
                foreach (var item in diff.Properties)
                {
                    if (i >= TaskDifferencesPropertyCount)
                        break;
                    Console.WriteLine($"  {name} {item} changed");
                    i++;
                }

                if (i < diff.Properties.Count)
                    Console.WriteLine($"  {name} ... and {diff.Properties.Count - i} more");
            }
        }

        private void FingerprintInputs(AssetTaskInputs result, AssetTaskInputs hint = null)
        {
            foreach (var prop in _metadata.InputProperties)
                result.InputProperties.Add(prop.Key, prop.Value.FingerprintCalculator.Calculate(prop.Value.Getter(_task)));
            foreach (var prop in _metadata.InputFiles)
                ResolveFileProperty(result.InputFiles, prop.Key, prop.Value(_task), hint?.InputFiles, false);
        }

        private void FingerprintOutputs(AssetTaskOutputs result, AssetTaskOutputs hint = null)
        {
            foreach (var prop in _metadata.OutputFiles)
                ResolveFileProperty(result.OutputFiles, prop.Key, prop.Value(_task), hint?.OutputFiles, true);
        }

        private void ResolveFileProperty(
            AssetTaskFiles files,
            string property,
            IEnumerable<string> paths,
            AssetTaskFiles hint,
            bool isOutput)
        {
            var fileProp = new AssetTaskFileProperty();
            foreach (var basePath in paths)
            {
                if (!fileProp.BasePaths.Add(_task.TaskManager.RelativizePath(basePath)))
                    continue;
                foreach (var item in ResolveFiles(basePath))
                    ResolvedPath(item);
            }

            foreach (var resolved in fileProp.ResolvedPaths)
                if (!files.Files.ContainsKey(resolved))
                    files.Files.Add(resolved, FileFingerprint.Compute(resolved));
            files.Properties.Add(property, fileProp);
            return;

            void ResolvedPath(string path)
            {
                if (isOutput && !_task.TaskManager.CanOutputTo(path))
                    throw new Exception(
                        $"Task {_task.Id.Name}, property {property} attempting to write to invalid path {Path.GetFullPath(path)}");
                var relativePath = _task.TaskManager.RelativizePath(path);
                fileProp.ResolvedPaths.Add(relativePath);
                if (!hint.Files.TryGetValue(relativePath, out var hintFingerprint))
                    hintFingerprint = default;
                if (!files.Files.ContainsKey(relativePath))
                    files.Files.Add(relativePath, FileFingerprint.Compute(path, hintFingerprint));
            }
        }

        public static IEnumerable<string> ResolveFiles(string basePath)
        {
            return Directory.Exists(basePath) ? Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories) : new[] { basePath };
        }
    }

    public sealed class TaskTiming
    {
        public double IdentifierSec { get; set; }
        public double PreExecutionFingerprintSec { get; set; }
        public double ExecutionSec { get; set; }
        public double PostExecutionFingerprintSec { get; set; }
    }

    public sealed class TaskExecutionReport
    {
        public string Name { get; set; }
        public bool Skipped { get; set; }
        public readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();
        public readonly TaskTiming Timing = new TaskTiming();
    }

    public sealed class ExecutionReport
    {
        public ExecutionSummary Summary
        {
            get => new ExecutionSummary(Tasks.Values);
            // ReSharper disable once ValueParameterNotUsed
            set { }
        }

        public readonly Dictionary<string, TaskExecutionReport> Tasks = new Dictionary<string, TaskExecutionReport>();

        private static readonly JsonSerializer Serializer = new JsonSerializer();

        public void WriteTo(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var writer = new JsonTextWriter(new StreamWriter(new FileStream(path, FileMode.Create)));
            writer.Formatting = Formatting.Indented;
            Serializer.Serialize(writer, this);
        }
    }

    public sealed class ExecutionSummary
    {
        public int TasksExecuted { get; set; }
        public int TasksSkipped { get; set; }

        public readonly TaskTiming TotalSkipped = new TaskTiming();
        public readonly TaskTiming TotalExecuted = new TaskTiming();

        public ExecutionSummary(IEnumerable<TaskExecutionReport> tasks)
        {
            foreach (var task in tasks)
            {
                TaskTiming target;
                if (task.Skipped)
                {
                    TasksSkipped++;
                    target = TotalSkipped;
                }
                else
                {
                    TasksExecuted++;
                    target = TotalExecuted;
                }

                // Identifier and pre-execution are always executed, so put them in the execution bucket regardless.
                TotalExecuted.IdentifierSec += task.Timing.IdentifierSec;
                TotalExecuted.PreExecutionFingerprintSec += task.Timing.PreExecutionFingerprintSec;

                target.ExecutionSec += task.Timing.ExecutionSec;
                target.PostExecutionFingerprintSec += task.Timing.PostExecutionFingerprintSec;
            }
        }
    }
}