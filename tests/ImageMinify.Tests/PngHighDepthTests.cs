using ImageMinify.Models;
using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class PngHighDepthTests
{
    [Theory]
    [InlineData(65535, PngColorType.Rgb)]
    [InlineData(65534, PngColorType.RgbWithAlpha)]
    [InlineData(0, PngColorType.RgbWithAlpha)]
    public void ChoosesChannelsWithoutLosingSixteenBitSamples(int alpha, PngColorType colorType)
    {
        using var source = new Image<Rgba64>(16, 16, new Rgba64(12345, 23456, 34567, ushort.MaxValue));
        source[15, 15] = new Rgba64(54321, 43210, 32109, (ushort)alpha);
        var payload = PngHighDepth.Encode(source, true);
        using var result = Image.Load<Rgba64>(payload);
        Assert.Equal(PngBitDepth.Bit16, result.Metadata.GetPngMetadata().BitDepth);
        Assert.Equal(colorType, result.Metadata.GetPngMetadata().ColorType);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                Assert.Equal(source[x, y], result[x, y]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnimatedPng_KeepsLaterTransparencyTimingAndLoopCount(bool animateRootFrame)
    {
        using var source = new Image<Rgba64>(16, 16, new Rgba64(12345, 23456, 34567, ushort.MaxValue));
        source.Metadata.GetPngMetadata().RepeatCount = 3;
        source.Metadata.GetPngMetadata().AnimateRootFrame = animateRootFrame;
        source.Frames.RootFrame.Metadata.GetPngMetadata().FrameDelay = new Rational(1, 10);
        source.Frames.AddFrame(source.Frames.RootFrame);
        source.Frames[1].Metadata.GetPngMetadata().FrameDelay = new Rational(3, 10);
        source.Frames[1][15, 15] = new Rgba64(54321, 43210, 32109, 12345);
        source.Frames.AddFrame(source.Frames[1]);
        source.Frames[2][0, 0] = new Rgba64(123, 456, 789, 0);
        var payload = PngHighDepth.Encode(source, true);
        using var result = Image.Load<Rgba64>(payload);
        Assert.Equal(PngBitDepth.Bit16, result.Metadata.GetPngMetadata().BitDepth);
        Assert.Equal(PngColorType.RgbWithAlpha, result.Metadata.GetPngMetadata().ColorType);
        Assert.Equal(3u, result.Metadata.GetPngMetadata().RepeatCount);
        Assert.Equal(animateRootFrame, result.Metadata.GetPngMetadata().AnimateRootFrame);
        Assert.Equal(3, result.Frames.Count);
        for (var frame = 0; frame < source.Frames.Count; frame++)
        {
            if (frame > 0 || animateRootFrame)
                Assert.Equal(source.Frames[frame].Metadata.GetPngMetadata().FrameDelay,
                    result.Frames[frame].Metadata.GetPngMetadata().FrameDelay);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    Assert.Equal(source.Frames[frame][x, y], result.Frames[frame][x, y]);
        }
    }

    [Fact]
    public async Task AnimatedPng_MultipleDataChunksSurviveFullPipeline()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"imageminify-apng16-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var source = new Image<Rgba64>(128, 128);
            source.Frames.AddFrame(source.Frames.RootFrame);
            var random = new Random(20261027);
            for (var frame = 0; frame < source.Frames.Count; frame++)
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        source.Frames[frame][x, y] = new Rgba64((ushort)random.Next(65536),
                            (ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536));
            var path = Path.Combine(directory, "source.png");
            File.WriteAllBytes(path, PngHighDepth.Encode(source, true));
            var compressor = new ImageCompressor(new ExifService(), new JpegCompressor(),
                new PngCompressor(), new WebpCompressor());
            var result = await compressor.CompressAsync(path, new CompressionOptions
            {
                Quality = 82, SkipWhenLarger = false, StripMetadata = true,
            });
            Assert.Equal(ResultState.Compressed, result.State);
            using var output = File.OpenRead(result.CompressedPath);
            using var decoded = PngDecoder.Instance.Decode<Rgba64>(new PngDecoderOptions
            {
                PngCrcChunkHandling = PngCrcChunkHandling.IgnoreNone,
            }, output);
            Assert.Equal(source.Frames.Count, decoded.Frames.Count);
            for (var frame = 0; frame < source.Frames.Count; frame++)
                for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        Assert.Equal(source.Frames[frame][x, y], decoded.Frames[frame][x, y]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CanceledEncoding_DoesNotModifySource()
    {
        using var source = new Image<Rgba64>(16, 16, new Rgba64(12345, 23456, 34567, ushort.MaxValue));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PngHighDepth.Encode(source, true, cancellation.Token));
        Assert.Equal(new Rgba64(12345, 23456, 34567, ushort.MaxValue), source[0, 0]);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(82, 0)]
    [InlineData(100, 1)]
    public async Task OriginalPng_PreservesSixteenBitSamples(int quality, int targetBytes)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"imageminify-png16-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.png");
            using var source = new Image<Rgba64>(16, 16);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    source[x, y] = new Rgba64((ushort)(x * 3001 + 13), (ushort)(y * 3007 + 29),
                        (ushort)(x * 1009 + y * 1013), (ushort)(x == 0 ? 0 : x * 3001 + 17));
            source.Save(path, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit16,
                ColorType = PngColorType.RgbWithAlpha,
                TransparentColorMode = PngTransparentColorMode.Preserve,
            });
            var compressor = new ImageCompressor(new ExifService(), new JpegCompressor(),
                new PngCompressor(), new WebpCompressor());
            var result = await compressor.CompressAsync(path, new CompressionOptions
            {
                Quality = quality, TargetSizeBytes = targetBytes, SkipWhenLarger = false,
                StripMetadata = true,
            });
            Assert.Equal(ResultState.Compressed, result.State);
            Assert.Equal(targetBytes > 0, result.TargetMissed);
            Assert.Equal(100, result.QualityUsed);
            Assert.Equal(PngBitDepth.Bit16, Image.Identify(result.CompressedPath).Metadata.GetPngMetadata().BitDepth);
            using var decoded = Image.Load<Rgba64>(result.CompressedPath);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    Assert.Equal(source[x, y], decoded[x, y]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
