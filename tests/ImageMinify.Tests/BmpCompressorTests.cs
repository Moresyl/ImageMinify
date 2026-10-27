using ImageMinify.Services;
using ImageMinify.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class BmpCompressorTests
{
    [Fact]
    public async Task OriginalFormatPipeline_PreservesTransparentBmp()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"imageminify-bmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "source.bmp");
            using var source = new Image<Rgba32>(8, 8, new Rgba32(123, 45, 67, 0));
            source[3, 4] = new Rgba32(20, 40, 60, 127);
            source.Save(path, new BmpEncoder
            {
                BitsPerPixel = BmpBitsPerPixel.Pixel32,
                SupportTransparency = true,
            });
            var compressor = new ImageCompressor(new ExifService(), new JpegCompressor(),
                new PngCompressor(), new WebpCompressor());
            var result = await compressor.CompressAsync(path, new CompressionOptions
            {
                OutputFormat = "original",
                SkipWhenLarger = false,
            });
            Assert.Equal(ResultState.Compressed, result.State);
            Assert.NotEqual(path, result.CompressedPath);
            using var decoded = Image.Load<Rgba32>(result.CompressedPath);
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    Assert.Equal(source[x, y], decoded[x, y]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Encode_PreservesAllSamplesAndOnlyOmitsOpaqueAlpha(bool transparent)
    {
        using var source = new Image<Rgba32>(16, 16);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source[x, y] = new Rgba32((byte)(x * 15), (byte)(y * 15), 123,
                    transparent ? (byte)(x + y * 16) : (byte)255);
        using var buffer = new MemoryStream();
        new BmpPassthroughEncoder().Encode(source, buffer, new EncodeRequest(100, true));
        var bytes = buffer.ToArray();
        Assert.Equal(transparent ? 32 : 24, BitConverter.ToUInt16(bytes, 28));
        using var result = Image.Load<Rgba32>(bytes);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                Assert.Equal(source[x, y], result[x, y]);
    }
}
