using System.Threading.Tasks;
using CommandLine;

namespace Equinox76561198048419394.Core.Cli.ModManager
{
    public static class ModManagerCli
    {
        [Verb("mod-manager", HelpText = "Manages and processes a mod")]
        public class Options : SharedOptions
        {
            public override ValueTask<int> Run() => ModManagerCli.Run(this);
        }


        public static ValueTask<int> Run(Options options)
        {
            return new ValueTask<int>(0);
        }
    }
}