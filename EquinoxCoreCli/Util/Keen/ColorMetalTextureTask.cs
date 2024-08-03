using System;
using Equinox76561198048419394.Core.Cli.Util.Tasks;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public sealed class ColorMetalTextureTask : AlphaAndColorMaskAwareTextureTask
    {
        /// <summary>
        /// The RGB albedo of the color metal texture.
        /// </summary>
        [InputNested]
        public readonly PathProperty Albedo;

        /// <summary>
        /// The binary (on / off) texture that determines if a pixel is a metal.
        /// </summary>
        [InputNested]
        public readonly Property<BandReference> Metal;

        public ColorMetalTextureTask(KeenMod taskManager) : base(taskManager)
        {
            Albedo = taskManager.PathProperty();
            Metal = taskManager.Property<BandReference>();
        }

        protected override void ExecuteInternal()
        {
            using var albedo = LoadImage<RgbaPixel>(Albedo.Value);
            using var metal = LoadBand(Metal.Value);

            DesiredResolution(out var width, out var height, albedo, metal);

            using var dest = TextureImages.Allocate<RgbaPixel>(width, height, true);
        }
    }
}