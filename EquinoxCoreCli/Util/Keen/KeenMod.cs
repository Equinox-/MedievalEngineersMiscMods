using System;
using System.IO;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public sealed class KeenMod
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
        }

        public string RelativePath(string path, string stripOptionalPrefix = null)
        {
            path = Path.GetFullPath(path);
            if (TryRelativePathInternal(OriginalContent, stripOptionalPrefix, path, out var result))
                return result;
            if (TryRelativePathInternal(Content, stripOptionalPrefix, path, out result))
                return result;
            throw new Exception("Not relative to content or original content");
        }

        private static bool TryRelativePathInternal(string root, string optionalPrefix, string path, out string result)
        {
            result = default;
            var stripped = path.AsSpan();
            if (!stripped.StartsWith(root.AsSpan()) || stripped[root.Length] != Path.DirectorySeparatorChar)
                return false;
            stripped = stripped.Slice(root.Length  + 1);
            if (optionalPrefix != null && stripped.StartsWith(optionalPrefix.AsSpan()) && stripped[optionalPrefix.Length] == Path.DirectorySeparatorChar)
                stripped = stripped.Slice(optionalPrefix.Length + 1);
            result = stripped.ToString();
            return true;
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
    }
}