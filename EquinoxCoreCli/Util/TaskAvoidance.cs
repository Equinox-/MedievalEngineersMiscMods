using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Equinox76561198048419394.Core.Cli.Util.Keen;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class TaskAvoidance
    {
        public static void MaybeRun(this KeenMod mod,
            string taskName, Action action,
            string taskArg = "",
            string inputFile = null,
            string[] inputFiles = null,
            string outputFile = null,
            string[] outputFiles = null,
            string extraInputs = "")
        {
            var fingerprintFile = mod.TaskFingerprint(taskName, taskArg);

            var previous = ReadFingerprints(fingerprintFile);
            var current = new Dictionary<string, Fingerprint>();
            if (inputFile != null)
                current.Add(inputFile, Compute(inputFile));
            if (inputFiles != null)
                foreach (var input in inputFiles)
                    current.Add(input, Compute(input));
            if (outputFile != null)
                current.Add(outputFile, Compute(outputFile));
            if (outputFiles != null)
                foreach (var output in outputFiles)
                    current.Add(output, Compute(output));
            if (!string.IsNullOrEmpty(extraInputs))
                current.Add(extraInputs, default);
            var differences = DifferingFiles(previous, current);
            if (differences.Count == 0)
            {
                Console.WriteLine($"[Skip] {taskName} ({taskArg})");
                return;
            }

            Console.WriteLine($"[Start] {taskName} ({taskArg})");
            var time = Stopwatch.GetTimestamp();
            action();
            if (outputFile != null)
                current[outputFile] = Compute(outputFile);
            if (outputFiles != null)
                foreach (var output in outputFiles)
                    current[output] = Compute(output);
            WriteFingerprints(current, fingerprintFile);
            Console.WriteLine($"[Done] {taskName} ({taskArg}) in {(Stopwatch.GetTimestamp() - time) / (double)Stopwatch.Frequency:F4} s");
        }

        private static Dictionary<string, Fingerprint> ReadFingerprints(string file)
        {
            var result = new Dictionary<string, Fingerprint>();
            if (!File.Exists(file)) return result;
            foreach (var line in File.ReadAllLines(file))
            {
                var chunks = line.Split(new[] { ' ' }, 3);
                if (chunks.Length != 3) continue;
                if (long.TryParse(chunks[0], out var time) && long.TryParse(chunks[1], out var size))
                    result[chunks[2]] = new Fingerprint(time, size);
            }

            return result;
        }

        private static void WriteFingerprints(Dictionary<string, Fingerprint> fingerprints, string file)
        {
            File.WriteAllLines(file, fingerprints.Select(x => $"{x.Value.Time} {x.Value.Size} {x.Key}"));
        }

        private static List<string> DifferingFiles(Dictionary<string, Fingerprint> previous, Dictionary<string, Fingerprint> current)
        {
            var changes = new List<string>();
            foreach (var kv in previous)
                if (!current.TryGetValue(kv.Key, out var fingerprint) || !fingerprint.Equals(kv.Value))
                    changes.Add(kv.Key);
            foreach (var key in current.Keys)
                if (!previous.ContainsKey(key))
                    changes.Add(key);
            return changes;
        }

        private static Fingerprint Compute(string file)
        {
            var info = new FileInfo(file);
            return info.Exists ? new Fingerprint(info.LastWriteTime.Ticks, info.Length) : default;
        }

        private readonly struct Fingerprint : IEquatable<Fingerprint>
        {
            public readonly long Time;
            public readonly long Size;

            public Fingerprint(long time, long size)
            {
                Time = time;
                Size = size;
            }

            public bool Equals(Fingerprint other) => Time == other.Time && Size == other.Size;

            public override bool Equals(object obj) => obj is Fingerprint other && Equals(other);

            public override int GetHashCode() => (Time.GetHashCode() * 397) ^ Size.GetHashCode();
        }
    }
}