using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CommandLine;
using DirectXTexNet;
using Equinox76561198048419394.Core.Cli.BlockVariant;
using Equinox76561198048419394.Core.Cli.Gltf;
using Equinox76561198048419394.Core.Cli.Tree;
using Equinox76561198048419394.Core.Cli.Util;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using Equinox76561198048419394.Core.Cli.Util.Tasks;
using Newtonsoft.Json;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities.Planet;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Equinox76561198048419394.Core.Cli
{
    public static class Program
    {
        private static void Test()
        {
const int size = 1024;
const float r = 16_000;
using var image = SixLabors.ImageSharp.Image.LoadPixelData(new SixLabors.ImageSharp.PixelFormats.La32[size * size], size, size);
for (var y = 0; y < size; y++)
{
    for (var x = 0; x < size; x++)
    {
        var px = default(SixLabors.ImageSharp.PixelFormats.La32);
        px.L = Sample(x);
        px.A = Sample(size - y);
        image[x, y] = px;
    }
}

ushort Sample(int pt) => (ushort)(r + r * MyCubemapHelpers.ProjectionToUniform(-1 + 2 * pt / (float)size));

image.SaveAsPng(@"C:\Tmp\me-geolocation-reverse-16k.png", new PngEncoder()
{
    BitDepth = PngBitDepth.Bit16,
    ColorType = PngColorType.GrayscaleWithAlpha,
    Gamma = 1,
});
        }
        public static async Task<int> Main(string[] args)
        {
            Test();
            return 0;

            var result = Parser.Default.ParseArguments<BlockVariantCli.Options, SpeedTreeCli.Options, GltfCli.Options>(args);
            int code;
            if (result is Parsed<object> parsed && parsed.Value is SharedOptions opts)
                code = await SetupResolverAndRun(opts);
            else
                code = 1;

            if (code != 0)
            {
                Console.WriteLine("Waiting for key... ");
                Console.ReadKey();
            }

            return code;
        }

        public static async Task<int> SetupResolverAndRun(SharedOptions opts)
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
            catch (FileNotFoundException)
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

            return await ProgramBootstrapped.SetupEngineAndRun(opts);
        }
    }
}