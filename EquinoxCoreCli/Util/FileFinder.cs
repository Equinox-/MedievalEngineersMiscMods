using System.IO;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public static class FileFinder
    {
        public static bool TryFindInParents(string file, out string path)
        {
            path = default;
            var dir = Path.GetFullPath("./");
            while (true)
            {
                if (string.IsNullOrEmpty(dir))
                    return false;
                path = Path.Combine(dir, file);
                if (File.Exists(path))
                    return true;
                dir = Path.GetDirectoryName(dir);
            }
        }
    }
}