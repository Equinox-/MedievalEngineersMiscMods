using System;
using System.Collections.Concurrent;
using DirectXTexNet;
using Equinox76561198048419394.Core.Cli.Util.Tasks;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public abstract class TextureTask : ModTask
    {
        private static readonly ConcurrentDictionary<string, WeakReference<TextureImage>> ImageCache =
            new ConcurrentDictionary<string, WeakReference<TextureImage>>();

        [OutputFile]
        public readonly PathProperty Output;

        [Input(Optional = true)]
        public readonly Property<int> Resolution;

        protected TextureTask(KeenMod taskManager) : base(taskManager)
        {
            Output = taskManager.PathProperty();
            Resolution = taskManager.Property<int>();
        }

        protected static TextureImage LoadImage(string path)
        {
            if (ImageCache.TryGetValue(path, out var imageRef) && imageRef.TryGetTarget(out var image))
                return image.AddRef();
            image = path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                ? TextureImages.LoadDds(path)
                : TextureImages.LoadWic(path, WIC_FLAGS.IGNORE_SRGB);
            imageRef = new WeakReference<TextureImage>(image);
            ImageCache.TryAdd(path, imageRef);
            return image.AddRef();
        }

        protected static TextureImage<T> LoadImage<T>(string path) where T : unmanaged, IPixel
        {
            using var image = LoadImage(path);
            return image.AssertFormat<T>().AddRef();
        }

        protected static TextureImage<RedPixel> LoadBand(BandReference band)
        {
            using var img = LoadImage(band.Path);
            return img.SelectBand(band.Band);
        }

        protected void DesiredResolution(out int width, out int height, params TextureImage[] sources)
        {
            if (Resolution.HasValue)
            {
                width = height = Resolution.Value;
                return;
            }

            width = 0;
            height = 0;
            foreach (var source in sources)
            {
                var img = source.Image(0);
                if (img.Width > width) width = img.Width;
                if (img.Height > height) height = img.Height;
            }
        }
    }

    public class BandReference
    {
        [Input]
        public string Path;

        [Input]
        public int Band;
    }

    public abstract class AlphaAndColorMaskAwareTextureTask : TextureTask
    {
        /// <summary>
        /// Binary (on / off) texture that will be used to determine if a pixel value contributes to
        /// the down-sampled values.
        /// </summary>
        [InputNested(Optional = true)]
        public readonly PathProperty AlphaMask;

        /// <summary>
        /// Binary (on / off) textures that will be used to group pixel values in order to compute
        /// down-sampled values. This prevents bleeding across different material types in a single texture.
        /// Typical usage is including the color mask and metal-ness bands.
        /// </summary>
        [InputNested]
        public readonly ListProperty<BandReference> Partitions;

        protected AlphaAndColorMaskAwareTextureTask(KeenMod taskManager) : base(taskManager)
        {
            AlphaMask = taskManager.PathProperty();
            Partitions = taskManager.ListProperty<BandReference>();
        }
    }

    public class NormalMapReference : BandReference
    {
        [Input]
        public NormalMapReference Mode;

        public enum NormalMapMode
        {
            DirectXNormalMap,
            HeightMap,
        }
    }

    public class GlossinessReference : BandReference
    {
        [Input]
        public GlossinessMode Mode;

        public enum GlossinessMode
        {
            Glossiness,
            Roughness
        }
    }

    public sealed class NormalGlossTextureTask : AlphaAndColorMaskAwareTextureTask
    {
        /// <summary>
        /// Source information for the normal map.
        /// </summary>
        [InputNested]
        public readonly Property<NormalMapReference> NormalMap;

        /// <summary>
        /// Source information for the glossiness map.
        /// </summary>
        [InputNested]
        public readonly Property<GlossinessReference> Glossiness;

        public NormalGlossTextureTask(KeenMod taskManager) : base(taskManager)
        {
            NormalMap = taskManager.Property<NormalMapReference>();
            Glossiness = taskManager.Property<GlossinessReference>();
        }

        protected override void ExecuteInternal()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class AddTextureTask : AlphaAndColorMaskAwareTextureTask
    {
        /// <summary>
        /// Ambient occlusion values for the texture.
        /// If absent, ambient occlusion will be computed from the height map.
        /// If the height map is absent, a constant value of one (no occlusion) will be used.
        /// </summary>
        [InputNested(Optional = true)]
        public readonly Property<BandReference> AmbientOcclusion;

        /// <summary>
        /// Emissivity values for the texture.
        /// If absent, a constant value of zero will be used.
        /// </summary>
        [InputNested(Optional = true)]
        public readonly Property<BandReference> Emissivity;

        /// <summary>
        /// Height map for the texture.
        /// If absent, a constant value of zero will be used.
        /// </summary>
        [InputNested(Optional = true)]
        public readonly Property<BandReference> Height;

        public AddTextureTask(KeenMod taskManager) : base(taskManager)
        {
            AmbientOcclusion = taskManager.Property<BandReference>();
            Emissivity = taskManager.Property<BandReference>();
            Height = taskManager.Property<BandReference>();
        }

        protected override void ExecuteInternal()
        {
            throw new NotImplementedException();
        }
    }
}