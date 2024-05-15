using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DirectXTexNet;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRageMath;

namespace Equinox76561198048419394.Core.Cli.Util.Keen
{
    public readonly struct KeenBand : IEquatable<KeenBand>
    {
        public const string SpecialFilePrefix = "special-file://";

        public enum SpecialFile
        {
            AllWhite,
            AllBlack,
        }

        public static bool IsSpecialFile(string file) => file.StartsWith(SpecialFilePrefix);

        public static bool TryGetSpecialFile(string file, out SpecialFile special)
        {
            var okay = IsSpecialFile(file);
            special = okay ? (SpecialFile)Enum.Parse(typeof(SpecialFile), file.Substring(SpecialFilePrefix.Length)) : default;
            return okay;
        }

        public readonly string File;
        public readonly int Band;

        public KeenBand(string file, int band)
        {
            File = file;
            Band = band;
        }

        public KeenBand(SpecialFile specialFile)
        {
            File = SpecialFilePrefix + specialFile;
            Band = 0;
        }

        public bool Equals(KeenBand other) => File == other.File && Band == other.Band;

        public override bool Equals(object obj) => obj is KeenBand other && Equals(other);

        public override int GetHashCode() => (File.GetHashCode() * 397) ^ Band;
    }

    public abstract class KeenTexture
    {
        public abstract IEnumerable<KeenBand> InputFiles { get; }

        public abstract KeenTexture Clone();

        public KeenProcessingFlags Flags { get; set; }

        internal abstract DXGI_FORMAT UncompressedFormat { get; }
        internal abstract DXGI_FORMAT CompressedFormat { get; }
        internal abstract TEX_FILTER_FLAGS MipGenerationFlags { get; }

        private KeenTexture()
        {
        }

        public sealed class ColorMetal : KeenTexture
        {
            public KeenBand Color;
            public KeenBand Metal;

            public KeenBand Fused
            {
                set
                {
                    Color = new KeenBand(value.File, value.Band);
                    Metal = new KeenBand(value.File, value.Band + 3);
                }
            }

            public override IEnumerable<KeenBand> InputFiles => new[] { Color, Metal };
            public override KeenTexture Clone() => new ColorMetal { Flags = Flags, Color = Color, Metal = Metal };
            internal override DXGI_FORMAT UncompressedFormat => DXGI_FORMAT.R8G8B8A8_UNORM_SRGB;
            internal override DXGI_FORMAT CompressedFormat => DXGI_FORMAT.BC7_UNORM_SRGB;
            internal override TEX_FILTER_FLAGS MipGenerationFlags => TEX_FILTER_FLAGS.WRAP | TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
        }

        public sealed class NormalGloss : KeenTexture
        {
            public KeenBand Normal;
            public KeenBand Gloss;

            public KeenBand Fused
            {
                set
                {
                    Normal = new KeenBand(value.File, value.Band);
                    Gloss = new KeenBand(value.File, value.Band + 3);
                }
            }

            public override IEnumerable<KeenBand> InputFiles => new[] { Normal, Gloss };
            public override KeenTexture Clone() => new NormalGloss { Flags = Flags, Normal = Normal, Gloss = Gloss };
            internal override DXGI_FORMAT UncompressedFormat => DXGI_FORMAT.R8G8B8A8_UNORM;
            internal override DXGI_FORMAT CompressedFormat => DXGI_FORMAT.BC7_UNORM;
            internal override TEX_FILTER_FLAGS MipGenerationFlags => TEX_FILTER_FLAGS.WRAP | TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
        }

        public sealed class Extension : KeenTexture
        {
            public KeenBand AmbientOcclusion;
            public KeenBand Emissivity;
            public KeenBand VoxelDetailOrHeight;
            public KeenBand ColorMask;

            public KeenBand Fused
            {
                set
                {
                    AmbientOcclusion = new KeenBand(value.File, value.Band);
                    Emissivity = new KeenBand(value.File, value.Band + 1);
                    VoxelDetailOrHeight = new KeenBand(value.File, value.Band + 2);
                    ColorMask = new KeenBand(value.File, value.Band + 3);
                }
            }

            public override IEnumerable<KeenBand> InputFiles => new[] { AmbientOcclusion, Emissivity, VoxelDetailOrHeight, ColorMask };

            public override KeenTexture Clone() => new Extension
            {
                Flags = Flags,
                AmbientOcclusion = AmbientOcclusion,
                Emissivity = Emissivity,
                VoxelDetailOrHeight = VoxelDetailOrHeight,
                ColorMask = ColorMask
            };

            internal override DXGI_FORMAT UncompressedFormat => DXGI_FORMAT.R8G8B8A8_UNORM_SRGB;
            internal override DXGI_FORMAT CompressedFormat => DXGI_FORMAT.BC7_UNORM_SRGB;
            internal override TEX_FILTER_FLAGS MipGenerationFlags => TEX_FILTER_FLAGS.WRAP | TEX_FILTER_FLAGS.BOX | TEX_FILTER_FLAGS.SEPARATE_ALPHA;
        }

        public sealed class AlphaMask : KeenTexture
        {
            public KeenBand Mask;

            public override IEnumerable<KeenBand> InputFiles => new[] { Mask };
            public override KeenTexture Clone() => new AlphaMask { Flags = Flags, Mask = Mask };
            internal override DXGI_FORMAT UncompressedFormat => DXGI_FORMAT.R8_UNORM;
            internal override DXGI_FORMAT CompressedFormat => DXGI_FORMAT.BC4_UNORM;
            internal override TEX_FILTER_FLAGS MipGenerationFlags => TEX_FILTER_FLAGS.WRAP;
        }

        public sealed class UserInterface : KeenTexture
        {
            public KeenBand ColorAlpha;

            public override IEnumerable<KeenBand> InputFiles => new[] { ColorAlpha };
            public override KeenTexture Clone() => new UserInterface { Flags = Flags, ColorAlpha = ColorAlpha };
            internal override DXGI_FORMAT UncompressedFormat => DXGI_FORMAT.R8G8B8A8_UNORM_SRGB;
            internal override DXGI_FORMAT CompressedFormat => DXGI_FORMAT.BC7_UNORM_SRGB;
            internal override TEX_FILTER_FLAGS MipGenerationFlags => TEX_FILTER_FLAGS.BOX;
        }
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

        /// <summary>
        /// Forces creation of voxel-compatible (2048px) textures.
        /// </summary>
        ForVoxels = 16,
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

        public static string ConvertTexture(this KeenMod mod, KeenTexture texture)
        {
            var files = new List<string>();
            var realFileCount = 0;
            var hash = 0;
            foreach (var inputBand in texture.InputFiles)
            {
                hash = hash * 397 + inputBand.GetHashCode();
                if (files.Contains(inputBand.File))
                    continue;
                if (KeenBand.IsSpecialFile(inputBand.File))
                    files.Add(inputBand.File);
                else
                    files.Insert(realFileCount++, mod.RelativePath(inputBand.File, "Textures"));
            }

            const int fileNameLimit = 128;

            var output = Path.Combine("Textures", realFileCount == 0
                ? files[0].Substring(KeenBand.SpecialFilePrefix.Length)
                : files[0].Substring(0, files[0].LastIndexOf('.')));
            for (var i = 1; i < files.Count && output.Length < fileNameLimit; i++)
                output += "_" + Path.GetFileNameWithoutExtension(files[i]);
            output += $"_{hash:X}.dds";
            var outputFile = Path.Combine(mod.Content, output);

            mod.MaybeRun(nameof(ConvertTexture),
                () => ConvertToInternal(outputFile, texture),
                taskArg: output,
                inputFiles: files.Take(realFileCount).ToArray(),
                outputFile: outputFile,
                extraInputs: $"{texture.GetType()} {texture.Flags}");
            return output;
        }

        private static void ConvertToInternal(string outputFile, KeenTexture texture)
        {
            using var ctx = new ConverterContext(texture);
            using var dds = ctx.Convert();
            var outputDir = Path.GetDirectoryName(outputFile);
            if (outputDir != null)
                Directory.CreateDirectory(outputDir);
            dds.SaveToDDSFile(DDS_FLAGS.NONE, outputFile);
        }

        private sealed class ConverterContext : IDisposable
        {
            private readonly TexHelper _helper;
            private readonly KeenTexture _texture;
            private readonly Dictionary<string, ScratchImage> _originals = new Dictionary<string, ScratchImage>();
            private ScratchImage _scratch;

            public ConverterContext(KeenTexture texture)
            {
                _helper = TexHelper.Instance;
                _texture = texture;
            }

            public ScratchImage Convert()
            {
                LoadImages();
                CreateScratch();
                using var mipChain = _scratch.GenerateMipMaps(_texture.MipGenerationFlags, 0);
                if (_texture.Flags.HasFlags(KeenProcessingFlags.AlphaMaskCoveragePreserving))
                    PreserveCoverage(mipChain);
                return Compress(mipChain, _texture.CompressedFormat);
            }

            private void LoadImages()
            {
                foreach (var inputBand in _texture.InputFiles)
                    if (!_originals.ContainsKey(inputBand.File))
                    {
                        var loaded = KeenBand.TryGetSpecialFile(inputBand.File, out var special)
                            ? _helper.LoadSpecialFile(special)
                            : _helper.LoadFromWICFile(inputBand.File, WIC_FLAGS.IGNORE_SRGB);
                        _originals.Add(inputBand.File, loaded);
                    }
            }

            private void CreateScratch()
            {
                int width, height;
                if (_texture.Flags.HasFlag(KeenProcessingFlags.ForVoxels))
                {
                    width = 2048;
                    height = 2048;
                }
                else
                {
                    width = 4;
                    height = 4;
                    foreach (var inputFile in _originals.Values)
                    {
                        var img = inputFile.GetImage(0);
                        if (img.Width > width) width = img.Width;
                        if (img.Height > height) height = img.Height;
                    }
                }

                _scratch = _helper.Initialize2D(_texture.UncompressedFormat, width, height, 1, 0, CP_FLAGS.NONE);
            }

            public void Dispose()
            {
                foreach (var loaded in _originals.Values)
                    loaded.Dispose();
                _scratch?.Dispose();
            }
        }

        private static unsafe ScratchImage LoadSpecialFile(this TexHelper helper, KeenBand.SpecialFile special)
        {
            var image = helper.Initialize2D(DXGI_FORMAT.R8G8B8A8_UNORM, 1, 1, 1, 0, CP_FLAGS.NONE);
            ref var pixel = ref ((RgbaPixel<byte>*)image.GetImage(0).Pixels.ToPointer())[0];
            pixel.Red = pixel.Green = pixel.Blue = pixel.Alpha = special switch
            {
                KeenBand.SpecialFile.AllWhite => byte.MaxValue,
                KeenBand.SpecialFile.AllBlack => byte.MinValue,
                _ => throw new ArgumentOutOfRangeException(nameof(special), special, null)
            };

            return image;
        }

        /// <summary>
        /// Preserves the percentage of pixels that pass an alpha clip across all mip masks.
        /// </summary>
        private static void PreserveCoverage(ScratchImage image)
        {
            var data = new PreserveCoverageData(image.GetImage(0));
            for (var img = 1; img < image.GetImageCount(); img++)
                data.ApplyCoverage(image.GetImage(img));
        }

        /// <summary>
        /// Preserves the percentage of pixels that pass an alpha clip across all mip masks.
        /// </summary>
        private class PreserveCoverageData
        {
            private const byte AlphaClip = 128;
            private readonly int[] _counts = new int[256];
            private readonly float _coverage;

            internal PreserveCoverageData(Image root)
            {
                AnalyzeImage(root, _counts);
                var coveredPixels = 0;
                for (int i = AlphaClip; i < _counts.Length; i++)
                    coveredPixels += _counts[i];
                _coverage = coveredPixels / (root.Width * (float)root.Height);
            }

            public void ApplyCoverage(Image target)
            {
                _counts.AsSpan().Fill(0);
                AnalyzeImage(target, _counts);
                byte adjustedClip = 255;
                var temp = _coverage * target.Width * target.Height;
                while (adjustedClip > 0)
                {
                    temp -= _counts[adjustedClip];
                    if (temp < 0)
                        break;
                    --adjustedClip;
                }

                if (adjustedClip == AlphaClip)
                    return;

                for (var y = 0; y < target.Height; y++)
                    foreach (ref var b in RowSpan(target, y))
                        if (adjustedClip == 0)
                            b = byte.MaxValue;
                        else if (b != byte.MaxValue)
                            b = (byte)MathHelper.Clamp(b * AlphaClip / adjustedClip, 0, byte.MaxValue);
            }

            private static void AnalyzeImage(Image img, Span<int> target)
            {
                for (var y = 0; y < img.Height; y++)
                    foreach (var b in RowSpan(img, y))
                        target[b]++;
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
        }

        private static ScratchImage Compress(ScratchImage image, DXGI_FORMAT format)
        {
            var device = format == DXGI_FORMAT.BC7_UNORM || format == DXGI_FORMAT.BC7_TYPELESS || format == DXGI_FORMAT.BC7_UNORM_SRGB ? Device : null;
            return device != null
                ? image.Compress(device.NativePointer, format, TEX_COMPRESS_FLAGS.PARALLEL, 1f)
                : image.Compress(format, TEX_COMPRESS_FLAGS.PARALLEL, default);
        }

        private static ScratchImage ConvertToStandard(Dictionary<string, ScratchImage> images, KeenTexture texture)
        {
            var dest = TexHelper.Instance.Initialize2D();

            var filterFlags = default(TEX_FILTER_FLAGS);
            if (texture.Flags.HasFlags(KeenProcessingFlags.AssumeInputSrgb))
                filterFlags |= TEX_FILTER_FLAGS.SRGB_IN;
            switch (texture)
            {
                case KeenTexture.NormalGloss ng:
                    return image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM, filterFlags, default);
                case KeenTexture.ColorMetal cm:
                case KeenTexture.Extension ext:
                    filterFlags |= TEX_FILTER_FLAGS.SRGB_OUT;
                    return image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM_SRGB, filterFlags, default);
                case KeenTexture.AlphaMask alpha:
                    filterFlags |= TEX_FILTER_FLAGS.RGB_COPY_RED;
                    return image.Convert(DXGI_FORMAT.R8_UNORM, filterFlags, default);
                case KeenTexture.UserInterface ui:
                {
                    filterFlags |= TEX_FILTER_FLAGS.SRGB_OUT;
                    using var srgb = image.Convert(DXGI_FORMAT.R8G8B8A8_UNORM_SRGB, filterFlags, default);
                    return srgb.PremultiplyAlpha(TEX_PMALPHA_FLAGS.DEFAULT);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(texture), texture.GetType(), null);
            }
        }

        private static ScratchImage Preprocess(ScratchImage image, KeenTexture texture)
        {
            switch (texture)
            {
                case KeenTexture.ColorMetal _:
                case KeenTexture.NormalGloss _:
                case KeenTexture.Extension _:
                    if (texture.Flags.HasFlags(KeenProcessingFlags.FullyOpaqueToFullyTransparent) && image.IsAlphaAllOpaque())
                        return null;
                    return image.TransformImage((output, input, y) =>
                    {
                        input.CopyTo(output);
                        for (var i = 0; i < output.Length; i++)
                            output[i].Alpha = 0;
                    });
                case KeenTexture.AlphaMask _:
                    if (texture.Flags.HasFlags(KeenProcessingFlags.AlphaMaskOneThird))
                        return image.TransformImage((output, input, y) =>
                        {
                            for (var i = 0; i < input.Length; i++)
                                output[i].Red = MathHelper.Lerp(.38f, 1f, input[i].Red);
                        });
                    return null;
                case KeenTexture.UserInterface _:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(texture), texture.GetType(), null);
            }
        }

        private delegate void TransformImageTyped(Span<RgbaPixel<float>> output, ReadOnlySpan<RgbaPixel<float>> input, int row);

        private static ScratchImage TransformImage(this ScratchImage image, TransformImageTyped transform)
        {
            return image.TransformImage((outPixels, inPixels, width, y) =>
            {
                unsafe
                {
                    var output = new Span<RgbaPixel<float>>(outPixels.ToPointer(), (int)width);
                    var input = new ReadOnlySpan<RgbaPixel<float>>(inPixels.ToPointer(), (int)width);
                    transform(output, input, (int)y);
                }
            });
        }

        private struct RgbaPixel<T>
        {
            public T Red;
            public T Green;
            public T Blue;
            public T Alpha;
        }
    }
}