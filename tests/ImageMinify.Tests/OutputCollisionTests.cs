using ImageMinify.Models;
using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class OutputCollisionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"imageminify-collision-{Guid.NewGuid():N}");
    private readonly ImageCompressor _compressor = new(new ExifService(), new JpegCompressor(),
        new PngCompressor(), new WebpCompressor());

    public OutputCollisionTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(OutputMode.Suffix)]
    [InlineData(OutputMode.Subfolder)]
    [InlineData(OutputMode.CustomFolder)]
    public async Task Batch_SameStemDifferentFormatsPreservesBothOutputs(OutputMode mode)
    {
        var png = Path.Combine(_directory, "photo.png");
        var bmp = Path.Combine(_directory, "photo.bmp");
        using (var red = new Image<Rgba32>(16, 16, Color.Red)) red.Save(png);
        using (var blue = new Image<Rgba32>(16, 16, Color.Blue)) blue.Save(bmp);
        var options = new CompressionOptions
        {
            OutputMode = mode, OutputFolder = Path.Combine(_directory, "out"),
            OutputFormat = "png", Quality = 100, SkipWhenLarger = false, MaxParallelism = 2,
        };
        var (results, _) = await _compressor.CompressBatchAsync([png, bmp], options);
        Assert.All(results, result => Assert.Equal(ResultState.Compressed, result.State));
        Assert.Equal(2, results.Select(result => result.CompressedPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        using var first = Image.Load<Rgba32>(results[0].CompressedPath);
        using var second = Image.Load<Rgba32>(results[1].CompressedPath);
        Assert.Equal(new Rgba32(255, 0, 0), first[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 255), second[0, 0]);
        Assert.DoesNotContain(results[0].CompressedPath, _compressor.Expand([_directory], options));
        Assert.DoesNotContain(results[1].CompressedPath, _compressor.Expand([_directory], options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("_compressed")]
    public async Task NonOverwriteMode_NeverReplacesAnExistingFile(string suffix)
    {
        var source = Path.Combine(_directory, "photo.png");
        using (var image = new Image<Rgba32>(16, 16, Color.Red)) image.Save(source);
        var occupied = Path.Combine(_directory, $"photo{suffix}.png");
        if (occupied != source) File.WriteAllText(occupied, "existing user file");
        var original = File.ReadAllBytes(occupied);
        var result = await _compressor.CompressAsync(source, new CompressionOptions
        {
            Suffix = suffix, Quality = 100, SkipWhenLarger = false,
        });
        Assert.Equal(ResultState.Compressed, result.State);
        Assert.NotEqual(occupied, result.CompressedPath);
        Assert.Equal(original, File.ReadAllBytes(occupied));
        using var decoded = Image.Load<Rgba32>(result.CompressedPath);
        Assert.Equal(new Rgba32(255, 0, 0), decoded[0, 0]);
        Assert.Empty(Directory.GetFiles(_directory, "*.part"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
