using System.IO;
using System.Threading.Tasks;
using CommandLine;
using DirectXTexNet;

namespace Equinox76561198048419394.Core.Cli.Tree
{
    public static class SpeedTreeCli
    {
        [Verb("speed-tree",
            HelpText = "Generate foliage from a Speed Tree export. The export should have LODs + a billboard and a skeleton if physics are desired.")]
        public class Options : SharedOptions, ISpeedTreeOptions
        {
            [Option("physics-min-radius", HelpText = "Minimum bone radius in meters for physics", Default = 0f)]
            public float PhysicsMinRadius { get; set; }

            [Option("physics-snap-angle", HelpText = "Consider bones within this angle tolerance to parallel as part of the same structure", Default = 10)]
            public float PhysicsSnapAngle { get; set; }

            [Option("fracture-length", HelpText = "Desired length of a fracture piece in meters", Default = 2.5f)]
            public float FractureLength { get; set; }

            [Option("fracture-stump-length", HelpText = "Desired length of the stump fracture piece in meters", Default = .1f)]
            public float FractureStumpLength { get; set; }

            public override ValueTask<int> Run() => SpeedTreeCli.Run(this);
        }

        public static async ValueTask<int> Run(Options options)
        {
            using var img = TexHelper.Instance.LoadFromWICFile(
                @"C:\Users\westin\Programming\MedievalEngineers\ExtraFoliage\OriginalContent\SpeedTree\Export\Sequoia_Giant_Desktop_Forest_Billboard_add.png",
                WIC_FLAGS.NONE);

            var speedTreeDir = Path.Combine(options.Mod.OriginalContent, "SpeedTree/Export");
            var speedTreeFile = Path.Combine(speedTreeDir, "Sequoia_Giant_Desktop_Forest.xml");

            options.Mod.CreateTask(() => new SpeedTreeTask(options.Mod)
            {
                SpeedTreeFile = { Value = speedTreeFile },
                Options = { Value = options }
            });
            await options.Mod.PlanExecution().Execute(Path.Combine(options.Mod.OriginalContent, "execution-report.json"));
            return 0;
        }
    }
}