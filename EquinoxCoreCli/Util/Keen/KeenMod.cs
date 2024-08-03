using System;
using System.IO;
using Equinox76561198048419394.Core.Cli.Util.Tasks;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public sealed class KeenMod : AssetTaskManager
    {
        private static readonly char[] InvalidFileName = Path.GetInvalidFileNameChars();

        public readonly string BaseDirectory;
        public readonly string OriginalContent;
        public readonly string Content;

        public KeenMod(string root)
        {
            BaseDirectory = Path.GetFullPath(root);
            OriginalContent = Path.GetFullPath(Path.Combine(root, "OriginalContent"));
            Content = Path.GetFullPath(Path.Combine(root, "Content"));

            AddFilePrefix("my_content", Content);
            AddFilePrefix("my_original_content", OriginalContent);
        }

        public string RelativePath(string path, string stripOptionalPrefix = null)
        {
            path = Path.GetFullPath(path);
            if (TryRelativePathInternal(OriginalContent, path, out var result, stripOptionalPrefix))
                return $"<original_content>{Path.DirectorySeparatorChar}{result}";
            if (TryRelativePathInternal(Content, path, out result, stripOptionalPrefix))
                return $"<content>{Path.DirectorySeparatorChar}{result}";
            throw new Exception("Not relative to content or original content");
        }

        public bool TryContentPath(string path, out string relativePath)
        {
            return TryRelativePathInternal(Content, Path.GetFullPath(path), out relativePath, null);
        }

        public string TaskFingerprint(string taskName, string taskArg = "")
        {
            const string ext = ".fingerprint";
            var fullName = new char[taskName.Length + (taskArg.Length > 0 ? taskArg.Length + 1 : 0) + ext.Length].AsSpan();
            var fullNameOffset = 0;
            fullName.Append(taskName.AsSpan(), ref fullNameOffset);
            if (taskArg.Length > 0)
            {
                fullName.Append("_".AsSpan(), ref fullNameOffset);
                fullName.Append(taskArg.AsSpan(), ref fullNameOffset);
            }

            fullName.Append(ext.AsSpan(), ref fullNameOffset);

            var search = fullName;
            while (true)
            {
                var i = search.IndexOfAny(InvalidFileName);
                if (i == -1)
                    break;
                search[i] = '_';
                search = search.Slice(i + 1);
            }

            var fingerprintDir = Path.Combine(OriginalContent, "Fingerprint");
            if (!File.Exists(fingerprintDir))
                Directory.CreateDirectory(fingerprintDir);
            return Path.Combine(fingerprintDir, fullName.ToString());
        }

        public override string FingerprintPath(AssetTaskIdentifier id) => TaskFingerprint(id.UniqueId);

        public override bool CanOutputTo(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return TryRelativePathInternal(Content, fullPath, out _, null) || TryRelativePathInternal(OriginalContent, fullPath, out _, null);
        }
    }
}