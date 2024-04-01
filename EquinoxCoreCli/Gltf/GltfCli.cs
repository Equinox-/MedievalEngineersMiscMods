using System.IO;
using CommandLine;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace Equinox76561198048419394.Core.Cli.Gltf
{
    public static class GltfCli
    {
        [Verb("gltf-to-mwm", HelpText = "Converts a GLTF file into a MWM file")]
        public class Options : SharedOptions
        {
            public override int Run() => GltfCli.Run(this);
        }


        public static int Run(Options options)
        {
            var gltfFile = Path.Combine(options.Mod.OriginalContent, "Models/PAX_Horse.glb");
            var model = ModelRoot.Load(gltfFile, new ReadSettings { Validation = ValidationMode.Strict });
            var scene = model.DefaultScene;
            return 0;
        }
    }
}