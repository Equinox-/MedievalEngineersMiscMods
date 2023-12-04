using System;
using System.Diagnostics;
using System.IO;
using DirectXTexNet;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRageMath;
using TEX_FILTER_FLAGS = DirectXTexNet.TEX_FILTER_FLAGS;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public enum KeenTexture
    {
        ColorMetal,
        NormalGloss,
        Extension,
        AlphaMask,
        UserInterface,
    }

    [Flags]
    public enum KeenProcessingFlags
    {
        None = 0,

        /// <summary>
        /// Converts a fully opaque alpha channel into a fully transparent alpha channel (for ColorMetal, NormalGloss, and Extension textures).
        /// </summary>
        FullyOpaqueToFullyTransparent = 1,

        /// <summary>
        /// Assumes the input file is sRGB.
        /// </summary>
        AssumeInputSrgb = 2,

        /// <summary>
        /// Rescales the alpha channel so the minimum value is 1/3 instead of zero (for AlphaMask textures).
        /// This is good for alpha masks with a lot of very fine details where things should bias towards being opaque when far away.
        /// </summary>
        AlphaMaskOneThird = 4,

        /// <summary>
        /// Rescales the alpha channels of mip maps so the same percentage of pixels pass the alpha test (for AlphaMask textures).
        /// </summary>
        AlphaMaskCoveragePreserving = 8,
    }

    public static class KeenTextureConverter
    {
        private static volatile bool _deviceInitialized;
        private static volatile Device _device;

        public static bool HasFlags(this KeenProcessingFlags bitset, KeenProcessingFlags flags) => (bitset & flags) == flags;

        private static Device Device
        {
            get
            {
                if (_deviceInitialized) return _device;
                lock (typeof(KeenTextureConverter))
                {
                    if (_deviceInitialized) return _device;
                    try
                    {
                        _device = new Device(DriverType.Hardware, DeviceCreationFlags.None, FeatureLevel.Level_11_0);
                        var name = _device.DebugName;
                        if (string.IsNullOrWhiteSpace(name))
                            name = _device.QueryInterface<SharpDX.DXGI.Device>().Adapter.Description.Description;
                        Console.WriteLine($"Initialized {name} for texture compression");
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine($"Failed to create D3D11 device, (very slow) software texture compression will be used. {err.Message}");
                    }

                    _deviceInitialized = true;
                    return _device;
                }
            }
        }

        public static string ConvertTexture(this KeenMod mod, string inputFile, KeenTexture type, KeenProcessingFlags flags = KeenProcessingFlags.None)
        {
            if (inputFile == null)
                return null;
            var relative = mod.RelativePath(inputFile, "Textures");
            var relativeWithoutExtension = relative.Substring(0, relative.LastIndexOf('.'));
            var output = Path.Combine("Textures", relativeWithoutExtension + ".dds");
            var outputFile = Path.Combine(mod.Content, output);

            mod.MaybeRun(nameof(ConvertTexture),
                () => ConvertToInternal(inputFile, outputFile, type, flags),
                taskArg: relativeWithoutExtension,
                inputFile: inputFile,
                outputFile: outputFile,
                extraInputs: $"{type} {flags}");
            return output;
        }

        private static void ConvertToInternal(string inputFile, string outputFile, KeenTexture type, KeenProcessingFlags flags)
        {
            var helper = TexHelper.Instance;
            using var file = helper.LoadFromWICFile(inputFile, WIC_FLAGS.IGNORE_SRGB);
            DXGI_FORMAT format;
            var filter = TEX_FILTER_FLAGS.WRAP;
            switch (type)
            {
                case KeenTexture.ColorMetal:
                    format = DXGI_FORMAT.BC7_UNORM_SRGB;
                    filter |= TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
                    break;
                case KeenTexture.NormalGloss:
                    format = DXGI_FORMAT.BC7_UNORM;
                    filter |= TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
                    break;
                case KeenTexture.Extension:
                    format = DXGI_FORMAT.BC7_UNORM_SRGB;
                    filter |= TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
                    break;
                case KeenTexture.AlphaMask:
                    format = DXGI_FORMAT.BC4_UNORM;
                    filter |= TEX_FILTER_FLAGS.RGB_COPY_RED;
                    filter |= flags.HasFlags(KeenProcessingFlags.AlphaMaskOneThird)
                              || flags.HasFlags(KeenProcessingFlags.AlphaMaskCoveragePreserving)
                        ? TEX_FILTER_FLAGS.BOX
                        : TEX_FILTER_FLAGS.POINT;
                    break;
                case KeenTexture.UserInterface:
                    format = DXGI_FORMAT.BC7_UNORM_SRGB;
                    filter |= TEX_FILTER_FLAGS.BOX;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            using var standard = ConvertToStandard(file, type, flags);
            using var preprocessed = Preprocess(standard, type, flags);

            using var mipChain = (preprocessed ?? standard).GenerateMipMaps(filter, 0);
            if (flags.HasFlags(KeenProcessingFlags.AlphaMaskCoveragePreserving))
                PreserveCoverage(mipChain);
            using var dds = Compress(mipChain, format);
            var outputDir = Path.GetDirectoryName(outputFile);
            if (outputDir != null)
                Directory.CreateDirectory(outputDir);
            dds.SaveToDDSFile(DDS_FLAGS.NONE, outputFile);
        }

        /// <summary>
        /// Preserves the percentage of pixels that pass an alpha clip across all mip masks.
        /// </summary>
        private static void PreserveCoverage(ScratchImage image)
        {
            const byte alphaClip = 128;
            var counts = new int[256].AsSpan();
            var root = image.GetImage(0);
            if (!TryAnalyzeImage(root, counts))
                return;

            var coveredPixels = 0;
            for (int i = alphaClip; i < counts.Length; i++)
                coveredPixels += counts[i];
            for (var img = 1; img < image.GetImageCount(); img++)
            {
                coveredPixels /= 4;
                var mip = image.GetImage(img);
                counts.Fill(0);
                if (!TryAnalyzeImage(mip, counts))
                    continue;
                byte adjustedClip = 255;
                var temp = coveredPixels;
                while (adjustedClip > 0)
                {
                    temp -= counts[adjustedClip];
                    if (temp < 0)
                        break;
                    --adjustedClip;
                }

                if (adjustedClip == alphaClip)
                    continue;

                for (var y = 0; y < mip.Height; y++)
                    foreach (ref var b in RowSpan(mip, y))
                        if (adjustedClip == 0)
                            b = byte.MaxValue;
                        else if (b != byte.MaxValue)
                            b = (byte)MathHelper.Clamp(b * alphaClip / adjustedClip, 0, byte.MaxValue);
            }
        }

        private static bool TryAnalyzeImage(Image img, Span<int> target)
        {
            if (img.Format != DXGI_FORMAT.R8_UNORM)
                return false;
            for (var y = 0; y < img.Height; y++)
                foreach (var b in RowSpan(img, y))
                    target[b]++;
            return true;
        }

        private static ScratchImage Compress(ScratchImage image, DXGI_FORMAT format)
        {
            var device = format == DXGI_FORMAT.BC7_UNORM || format == DXGI_FORMAT.BC7_TYPELESS || format == DXGI_FORMAT.BC7_UNORM_SRGB ? Device : null;
            return device != null
                ? image.Compress(device.NativePointer, format, TEX_COMPRESS_FLAGS.PARALLEL, 1f)
                : image.Compress(format, TEX_COMPRESS_FLAGS.PARALLEL, default);
        }

        private static ScratchImage ConvertToStandard(ScratchImage image, KeenTexture type, KeenProcessingFlags flags)
        {
            var filterFlags = default(TEX_FILTER_FLAGS);
            if (flags.HasFlags(KeenProcessingFlags.AssumeInputSrgb))
                filterFlags |= TEX_FILTER_FLAGS.SRGB_IN;
            switch (type)
            {
                case KeenTexture.NormalGloss:
                    return image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, filterFlags, default);
                case KeenTexture.ColorMetal:
                case KeenTexture.Extension:
                    filterFlags |= TEX_FILTER_FLAGS.SRGB_OUT;
                    return image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM_SRGB, filterFlags, default);
                case KeenTexture.AlphaMask:
                    filterFlags |= TEX_FILTER_FLAGS.RGB_COPY_RED;
                    return image.Convert(DXGI_FORMAT.R8_UNORM, filterFlags, default);
                case KeenTexture.UserInterface:
                {
                    filterFlags |= TEX_FILTER_FLAGS.SRGB_OUT;
                    using var srgb = image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM_SRGB, filterFlags, default);
                    return srgb.PremultiplyAlpha(TEX_PMALPHA_FLAGS.DEFAULT);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static ScratchImage Preprocess(ScratchImage image, KeenTexture type, KeenProcessingFlags flags)
        {
            switch (type)
            {
                case KeenTexture.ColorMetal:
                case KeenTexture.NormalGloss:
                case KeenTexture.Extension:
                    if (flags.HasFlags(KeenProcessingFlags.FullyOpaqueToFullyTransparent) && image.IsAlphaAllOpaque())
                        return null;
                    return image.TransformImage((output, input, y) =>
                    {
                        input.CopyTo(output);
                        for (var i = 0; i < output.Length; i++)
                            output[i].Alpha = 0;
                    });
                case KeenTexture.AlphaMask:
                    if (flags.HasFlags(KeenProcessingFlags.AlphaMaskOneThird))
                        return image.TransformImage((output, input, y) =>
                        {
                            for (var i = 0; i < input.Length; i++)
                                output[i].Red = MathHelper.Lerp(.38f, 1f, input[i].Red);
                        });
                    return null;
                case KeenTexture.UserInterface:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static Span<byte> RowSpan(Image img, int y)
        {
            if (img.Format != DXGI_FORMAT.R8_UNORM)
                throw new Exception("Bad image format");
            unsafe
            {
                return new Span<byte>((byte*)img.Pixels.ToPointer() + img.RowPitch * y, (int)img.RowPitch);
            }
        }

        private delegate void TransformImageTyped(Span<RgbaPixel> output, Span<RgbaPixel> input, int row);

        private static ScratchImage TransformImage(this ScratchImage image, TransformImageTyped transform)
        {
            return image.TransformImage((outPixels, inPixels, width, y) =>
            {
                unsafe
                {
                    var output = new Span<RgbaPixel>(outPixels.ToPointer(), (int)width);
                    var input = new Span<RgbaPixel>(inPixels.ToPointer(), (int)width);
                    transform(output, input, (int)y);
                }
            });
        }

        private struct RgbaPixel
        {
            public float Red;
            public float Green;
            public float Blue;
            public float Alpha;
        }
    }
}