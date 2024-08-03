using System;
using DirectXTexNet;
using Equinox76561198048419394.Core.Cli.Util.Tasks;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public enum AlphaMaskMode
    {
        /// <summary>
        /// Uses the alpha channel verbatim.
        /// </summary>
        Verbatim,

        /// <summary>
        /// Rescales the alpha channel so the minimum value is 1/3 instead of zero (for AlphaMask textures).
        /// This is good for alpha masks with a lot of very fine details where things should bias towards being opaque when far away.
        /// </summary>
        OneThird,

        /// <summary>
        /// Rescales the alpha channels of mip maps so the same percentage of pixels pass the alpha test (for AlphaMask textures).
        /// </summary>
        CoveragePreserving,
    }

    public sealed class AlphaMaskTextureTask : TextureTask
    {
        private const TEX_FILTER_FLAGS MipMapFilter = TEX_FILTER_FLAGS.BOX;

        [InputNested]
        public readonly Property<BandReference> AlphaMask;

        [Input]
        public readonly Property<AlphaMaskMode> Mode;

        public AlphaMaskTextureTask(KeenMod taskManager) : base(taskManager)
        {
            AlphaMask = taskManager.Property<BandReference>();
            Mode = taskManager.Property<AlphaMaskMode>().Convention(AlphaMaskMode.CoveragePreserving);
        }

        protected override void ExecuteInternal()
        {
            using var image = LoadBand(AlphaMask.Value);
            using var withMipMaps = ResizeAndProcess(image);

            withMipMaps.WriteDds(Output.Value, DXGI_FORMAT.BC4_UNORM);
        }

        private TextureImage<RedPixel> ResizeAndProcess(TextureImage<RedPixel> raw)
        {
            DesiredResolution(out var width, out var height, raw);
            var mode = Mode.Value;
            switch (mode)
            {
                case AlphaMaskMode.Verbatim:
                {
                    using var resized = raw.Resize(width, height, MipMapFilter);
                    return resized.GenerateMipMaps(MipMapFilter);
                }
                case AlphaMaskMode.OneThird:
                {
                    raw.EditImage((inOut, row) =>
                    {
                        foreach (ref var pixel in inOut)
                            pixel.Red = (byte)MathHelper.Lerp(byte.MaxValue * 1 / 3f, byte.MaxValue, pixel.Red);
                    });
                    using var resized = raw.Resize(width, height, MipMapFilter);
                    return resized.GenerateMipMaps(MipMapFilter);
                }
                case AlphaMaskMode.CoveragePreserving:
                {
                    var data = new PreserveCoverageData(raw, 0);
                    using var resized = raw.Resize(width, height, MipMapFilter);
                    using var mips = resized.GenerateMipMaps(MipMapFilter);
                    for (var i = 0; i < mips.ImageCount; i++)
                        data.ApplyCoverage(mips, i);
                    return mips.AddRef();
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private class PreserveCoverageData
        {
            private const byte AlphaClip = 128;
            private readonly int[] _counts = new int[256];
            private readonly float _coverage;

            internal PreserveCoverageData(TextureImage<RedPixel> target, int index)
            {
                var img = target.Image(index);
                AnalyzeImage(target, 0, _counts);
                var coveredPixels = 0;
                for (int i = AlphaClip; i < _counts.Length; i++)
                    coveredPixels += _counts[i];
                _coverage = coveredPixels / (img.Width * (float)img.Height);
            }

            public void ApplyCoverage(TextureImage<RedPixel> target, int index)
            {
                _counts.AsSpan().Fill(0);
                AnalyzeImage(target, index, _counts);
                var img = target.Image(index);
                byte adjustedClip = 255;
                var temp = _coverage * img.Width * img.Height;
                while (adjustedClip > 0)
                {
                    temp -= _counts[adjustedClip];
                    if (temp < 0)
                        break;
                    --adjustedClip;
                }

                if (adjustedClip == AlphaClip)
                    return;

                for (var y = 0; y < img.Height; y++)
                    foreach (ref var b in target.Row(index, y))
                        if (adjustedClip == 0)
                            b.Red = byte.MaxValue;
                        else if (b.Red != byte.MaxValue)
                            b.Red = (byte)MathHelper.Clamp(b.Red * AlphaClip / adjustedClip, 0, byte.MaxValue);
            }

            private static void AnalyzeImage(TextureImage<RedPixel> src, int index, Span<int> target)
            {
                var img = src.Image(index);
                for (var y = 0; y < img.Height; y++)
                    foreach (var b in src.Row(index, y))
                        target[b.Red]++;
            }
        }
    }
}