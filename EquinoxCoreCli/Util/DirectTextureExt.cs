using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DirectXTexNet;
using Equinox76561198048419394.Core.Cli.Util.Keen;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

namespace Equinox76561198048419394.Core.Cli.Util
{
    public interface IPixel
    {
        DXGI_FORMAT Format { get; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RedPixel : IPixel
    {
        public const DXGI_FORMAT Format = DXGI_FORMAT.R8_UNORM;

        [FieldOffset(0)]
        public byte Red;

        DXGI_FORMAT IPixel.Format => Format;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct RgbaPixel : IPixel
    {
        public const DXGI_FORMAT Format = DXGI_FORMAT.R8G8B8A8_UNORM;

        [FieldOffset(0)]
        public fixed byte Rgba[4];

        [FieldOffset(0)]
        public byte Red;

        [FieldOffset(1)]
        public byte Green;

        [FieldOffset(2)]
        public byte Blue;

        [FieldOffset(3)]
        public byte Alpha;

        DXGI_FORMAT IPixel.Format => Format;
    }

    public static class TextureImages
    {
        public static TextureImage<T> Allocate<T>(int width, int height, bool includeMipMaps) where T : unmanaged, IPixel
        {
            return new TextureImage<T>(TexHelper.Instance.Initialize2D(default(T).Format, width, height, 1,
                includeMipMaps ? ComputeMipCount(Math.Max(width, height)) : 0, CP_FLAGS.NONE));

            int ComputeMipCount(int res)
            {
                var mips = 0;
                while (res > 4)
                {
                    mips++;
                    res /= 2;
                }

                return mips;
            }
        }

        public static TextureImage LoadWic(string path, WIC_FLAGS flags)
        {
            var scratch = TexHelper.Instance.LoadFromWICFile(path, flags);
            var img = scratch.GetImage(0);
            // ReSharper disable once SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
            return img.Format switch
            {
                RedPixel.Format => new TextureImage<RedPixel>(scratch),
                RgbaPixel.Format => new TextureImage<RgbaPixel>(scratch),
                _ => throw new Exception($"Unsupported format {img.Format}")
            };
        }

        public static TextureImage LoadDds(string path)
        {
            using var compressed = TexHelper.Instance.LoadFromDDSFile(path, DDS_FLAGS.NONE);
            var img = compressed.GetImage(0);
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (TexHelper.Instance.MakeTypeless(img.Format))
            {
                case DXGI_FORMAT.BC1_TYPELESS:
                case DXGI_FORMAT.BC2_TYPELESS:
                case DXGI_FORMAT.BC3_TYPELESS:
                    return new TextureImage<RgbaPixel>(compressed.Decompress(RgbaPixel.Format));
                case DXGI_FORMAT.BC4_TYPELESS:
                case DXGI_FORMAT.BC7_TYPELESS:
                    return new TextureImage<RedPixel>(compressed.Decompress(RedPixel.Format));
                default:
                    throw new Exception($"Unsupported format {img.Format}");
            }
        }

        public static TextureImage<T> AssertFormat<T>(this TextureImage img) where T : unmanaged, IPixel
        {
            if (img is TextureImage<T> fmt)
                return fmt;
            throw new ArgumentException(nameof(img), $"Expected image to have format {typeof(T)} but was {img.GetType()}");
        }

        public static TextureImage<RedPixel> SelectBand(this TextureImage src, int band)
        {
            switch (src)
            {
                case TextureImage<RedPixel> red:
                    if (band != 0) throw new ArgumentOutOfRangeException(nameof(band), "Only band 0 is selectable from a Red image");
                    return red.AddRef();
                case TextureImage<RgbaPixel> rgba:
                    return rgba.SelectBand(band);
                default:
                    throw new ArgumentOutOfRangeException(nameof(src), src.GetType().Name);
            }
        }

        public static TextureImage<RedPixel> SelectBand(this TextureImage<RgbaPixel> src, int band)
        {
            if (band < 0 || band >= 4) throw new ArgumentOutOfRangeException(nameof(band), "Only bands [0, 4) are selectable from a RGBA image");
            return src.TransformImage<RgbaPixel, RedPixel>((output, input, row) =>
            {
                unsafe
                {
                    for (var x = 0; x < input.Length; x++)
                        output[x].Red = input[x].Rgba[band];
                }
            });
        }

        public delegate void DelTransformImage<TIn, TOut>(Span<TOut> output, ReadOnlySpan<TIn> input, int row);

        public static TextureImage<TOut> TransformImage<TIn, TOut>(this TextureImage<TIn> input, DelTransformImage<TIn, TOut> transform)
            where TIn : unmanaged, IPixel where TOut : unmanaged, IPixel
        {
            var metadata = input.MetadataCopy;
            metadata.Format = default(TOut).Format;
            using var dest = new TextureImage<TOut>(TexHelper.Instance.Initialize(metadata, CP_FLAGS.NONE));
            for (var i = 0; i < input.ImageCount; i++)
            {
                var height = input.Image(i).Height;
                for (var y = 0; y < height; y++)
                    transform(dest.Row(i, y), input.Row(i, y), y);
            }

            return dest.AddRef();
        }

        public delegate void DelEditImage<T>(Span<T> inOut, int row);

        public static void EditImage<T>(this TextureImage<T> input, DelEditImage<T> transform) where T : unmanaged, IPixel
        {
            for (var i = 0; i < input.ImageCount; i++)
            {
                var height = input.Image(i).Height;
                for (var y = 0; y < height; y++)
                    transform(input.Row(i, y), y);
            }
        }

        public static TextureImage<T> GenerateMipMaps<T>(this TextureImage<T> input, TEX_FILTER_FLAGS flags) where T : unmanaged, IPixel
        {
            return new TextureImage<T>(input.Raw.GenerateMipMaps(flags, 0));
        }

        public static TextureImage<T> Resize<T>(this TextureImage<T> input, int width, int height, TEX_FILTER_FLAGS flags) where T : unmanaged, IPixel
        {
            var root = input.Image(0);
            if (root.Width == width && root.Height == height) return input.AddRef();
            return new TextureImage<T>(input.Raw.Resize(0, width, height, flags));
        }

        public static void WriteDds(this TextureImage input, string outputFile, DXGI_FORMAT format)
        {
            var device = format == DXGI_FORMAT.BC7_UNORM || format == DXGI_FORMAT.BC7_TYPELESS || format == DXGI_FORMAT.BC7_UNORM_SRGB ? Device : null;
            using var compressed = device != null
                ? input.Raw.Compress(device.NativePointer, format, TEX_COMPRESS_FLAGS.PARALLEL, 1f)
                : input.Raw.Compress(format, TEX_COMPRESS_FLAGS.PARALLEL, default);

            var outputDir = Path.GetDirectoryName(outputFile);
            if (outputDir != null)
                Directory.CreateDirectory(outputDir);
            compressed.SaveToDDSFile(DDS_FLAGS.NONE, outputFile);
        }

        #region GPU Accelerated Compression

        private static volatile bool _deviceInitialized;
        private static volatile Device _device;

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

        #endregion
    }

    public abstract class TextureImage : IDisposable
    {
        private int _refCount = 1;
        public ScratchImage Raw { get; private set; }

        internal TextureImage(ScratchImage raw) => Raw = raw;

        public TexMetadata MetadataCopy => Raw.GetMetadata();
        public int ImageCount => Raw.GetImageCount();
        public Image Image(int index) => Raw.GetImage(index);

        internal TextureImage AddRef()
        {
            Interlocked.Increment(ref _refCount);
            return this;
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) > 0)
                return;
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        ~TextureImage() => DisposeInternal();

        private void DisposeInternal()
        {
            Raw?.Dispose();
            Raw = null;
        }
    }

    public sealed class TextureImage<T> : TextureImage where T : unmanaged, IPixel
    {
        internal TextureImage(ScratchImage raw) : base(raw)
        {
        }

        public Span<T> Row(int imageIndex, int y)
        {
            unsafe
            {
                var img = Image(imageIndex);
                if (y < 0 || y >= img.Height) throw new ArgumentOutOfRangeException(nameof(y), $"Y value must be [0, {img.Height})");
                return new Span<T>((T*)((byte*)img.Pixels.ToPointer() + img.RowPitch * y), img.Width);
            }
        }

        internal new TextureImage<T> AddRef() => (TextureImage<T>)base.AddRef();
    }
}