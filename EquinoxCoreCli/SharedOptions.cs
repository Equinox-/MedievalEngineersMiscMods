using CommandLine;
using Equinox76561198048419394.Core.Cli.Util.Keen;

namespace Equinox76561198048419394.Core.Cli
{
    public abstract class SharedOptions
    {
        [Option]
        public string GameDirectory { get; set; }

        public KeenMod Mod { get; set; }

        [Option(
            'm',
            "mod-directory",
            HelpText = "Mod directory with OriginalContent and Content folders")]
        public string ModDirectory
        {
            get => Mod?.BaseDirectory;
            set => Mod = new KeenMod(value);
        }

        public abstract int Run();
    }
}