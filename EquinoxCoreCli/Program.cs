using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CommandLine;
using Equinox76561198048419394.Core.Cli.BlockVariant;
using Equinox76561198048419394.Core.Cli.Gltf;
using Equinox76561198048419394.Core.Cli.Tree;
using Equinox76561198048419394.Core.Cli.Util;

namespace Equinox76561198048419394.Core.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<BlockVariantCli.Options, SpeedTreeCli.Options, GltfCli.Options>(args);
            int code;
            if (result is Parsed<object> parsed && parsed.Value is SharedOptions opts)
                code = SetupResolverAndRun(opts);
            else
                code = 1;

            if (code != 0)
            {
                Console.WriteLine("Waiting for key... ");
                Console.ReadKey();
            }

            return code;
        }

        public static int SetupResolverAndRun(SharedOptions opts)
        {
            opts.GameDirectory = opts.GameDirectory ?? SteamLibrary.FindAppInstallDir(333950);
            var installDir = opts.GameDirectory;
            if (!Directory.Exists(installDir))
            {
                Console.WriteLine("Can't find install directory for ME, will not run " + installDir);
                return 1;
            }

            try
            {
                Assembly.Load("VRage");
            }
            catch (FileNotFoundException ex)
            {
                var binaryDir = Path.Combine(installDir, "Bin64");
                var bound = new Dictionary<string, Assembly>();
                AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
                {
                    var asmName = new AssemblyName(eventArgs.Name).Name;
                    if (bound.TryGetValue(asmName, out var tmp))
                        return tmp;
                    var asmPath = Path.Combine(binaryDir, asmName + ".dll");
                    return bound[asmName] = Assembly.LoadFrom(asmPath);
                };
            }

            return ProgramBootstrapped.SetupEngineAndRun(opts);
        }
    }
}