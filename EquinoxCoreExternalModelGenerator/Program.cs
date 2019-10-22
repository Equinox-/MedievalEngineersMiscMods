using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Equinox76561198048419394.Core.ModelCreator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var installDir = SteamLibrary.FindAppInstallDir(333950);
            if (!Directory.Exists(installDir))
            {
                Console.WriteLine("Can't find install directory for ME, will not run " + installDir);
                Console.ReadKey();
                return;
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

            var find = "setup.xml";
            var path = Path.GetFullPath("./");
            while (true)
            {
                if (string.IsNullOrEmpty(path))
                {
                    Console.WriteLine("Can't find setup.xml file.");
                    Console.ReadKey();
                    return;
                }

                if (File.Exists(Path.Combine(path, find)))
                    break;
                path = Path.GetDirectoryName(path);
            }

            if (!File.Exists(Path.Combine(path, find)))
            {
                Console.WriteLine("Can't find setup.xml file.");
                Console.ReadKey();
                return;
            }

            typeof(ProgramBootstrapped).GetMethod("MainBootstrap")
                .Invoke(null, new object[] {new[] {Path.Combine(installDir, "Content"), Path.Combine(path, find)}});
        }
    }
}