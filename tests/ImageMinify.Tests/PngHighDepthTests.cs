using ImageMinify.Models;
using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class PngHighDepthTests
{
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
