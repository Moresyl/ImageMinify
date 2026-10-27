using System.Collections.Concurrent;
using ImageMinify.Models;
using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class ImageCompressorTests : IDisposable
{
    private readonly IImageCompressor _compressor = new ImageCompressor(
        new ExifService(),
        new JpegCompressor(),
        new PngCompressor(),
        new WebpCompressor());

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-compressor-{Guid.NewGuid():N}");

    public ImageCompressorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Theory]
    [InlineData("example.jpg")]
    [InlineData("example.jpeg")]
    [InlineData("example.png")]
    [InlineData("example.webp")]
    [InlineData("example.bmp")]
    public void SupportedExtensions_ContainExpectedImageTypes(string fileName)
    {
        Assert.Contains(
            _compressor.SupportedExtensions,
            extension => string.Equals(extension, Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompressAsync_ConvertsToRequestedFormat()
    {
        var inputPath = CreateNoiseImage("source.jpg", 160, 160);

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputFormat = "webp", Quality = 80 });

        Assert.Equal(ResultState.Compressed, result.State);
        Assert.True(File.Exists(result.CompressedPath));
        Assert.EndsWith(".webp", result.CompressedPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.OriginalSize > 0);
        Assert.True(result.CompressedSize > 0);
        Assert.Equal(160, result.Width);
    }

    [Fact]
    public void Expand_TakesSupportedFilesAndLeavesEverythingElse()
    {
        CreateNoiseImage("one.jpg", 48, 48);
        CreateNoiseImage("two.png", 48, 48);
        File.WriteAllText(Path.Combine(_tempDirectory, "skip.txt"), "not an image");

        var files = _compressor.Expand([_tempDirectory], new CompressionOptions());

        Assert.Equal(2, files.Count);
        Assert.DoesNotContain(files, file => file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A second run over the same folder must not compress the first run's output. Without this,
    /// re-running on a folder degrades the same images again and again.
    /// </summary>
    [Fact]
    public void Expand_SkipsTheToolsOwnOutput()
    {
        var original = CreateNoiseImage("photo.jpg", 48, 48);
        CreateNoiseImage("photo_compressed.jpg", 48, 48);
        CreateNoiseImage(Path.Combine("compressed", "photo.jpg"), 48, 48);

        var files = _compressor.Expand(
            [_tempDirectory],
            new CompressionOptions { Suffix = "_compressed", Recursive = true });

        Assert.Equal(original, Assert.Single(files));
    }

    /// <summary>Two selected folders that overlap still yield each image once.</summary>
    [Fact]
    public void Expand_DeduplicatesFilesReachableByTwoRoutes()
    {
        CreateNoiseImage(Path.Combine("nested", "one.jpg"), 32, 32);
        var nested = Path.Combine(_tempDirectory, "nested");

        var files = _compressor.Expand(
            [_tempDirectory, nested],
            new CompressionOptions { Recursive = true });

        Assert.Single(files);
    }

    [Fact]
    public async Task CompressBatchAsync_ReportsEveryFileAndSummarisesTheRun()
    {
        var files = new[]
        {
            CreateNoiseImage("one.jpg", 64, 64),
            CreateNoiseImage("two.jpg", 64, 64),
            CreateNoiseImage("three.jpg", 64, 64),
        };

        // The workers report from the thread pool and Progress<T> has no synchronisation context to
        // marshal onto here, so the callbacks genuinely run in parallel.
        var reported = new ConcurrentQueue<CompressionProgress>();
        var progress = new Progress<CompressionProgress>(reported.Enqueue);

        var (results, summary) = await _compressor.CompressBatchAsync(
            files,
            new CompressionOptions { OutputFormat = "webp", Quality = 70 },
            progress: progress);

        Assert.Equal(3, results.Count);
        Assert.Equal(3, summary.TotalFiles);
        Assert.Equal(3, summary.ProcessedFiles);
        Assert.True(summary.TotalOriginalSize > 0);
        Assert.True(summary.TotalCompressedSize > 0);
        Assert.True(summary.Elapsed > TimeSpan.Zero);

        await WaitUntilAsync(() => reported.Count == 3);
        Assert.All(reported, item => Assert.Equal(3, item.Total));
    }

    /// <summary>Order in, order out — the batch runs in parallel but must not shuffle the results.</summary>
    [Fact]
    public async Task CompressBatchAsync_KeepsTheCallersOrder()
    {
        var files = Enumerable.Range(0, 8).Select(index => CreateNoiseImage($"{index}.jpg", 48, 48)).ToList();

        var (results, _) = await _compressor.CompressBatchAsync(files, new CompressionOptions());

        Assert.Equal(files, results.Select(result => result.OriginalPath));
    }

    /// <summary>"Longest edge" is a ceiling, not a target: a smaller image is left at its own size.</summary>
    [Fact]
    public async Task CompressAsync_LongestEdgeFitsTheBoxWithoutEnlarging()
    {
        var large = CreateNoiseImage("large.jpg", 400, 200);
        var small = CreateNoiseImage("small.jpg", 80, 40);

        var options = new CompressionOptions { ScaleMode = ScaleMode.LongestEdge, ScaleValue = 200 };

        var scaled = await _compressor.CompressAsync(large, options);
        var untouched = await _compressor.CompressAsync(small, options);

        Assert.Equal(200, scaled.Width);
        Assert.Equal(100, scaled.Height);
        Assert.Equal(400, scaled.OriginalWidth);
        Assert.Equal(80, untouched.Width);
        Assert.Equal(40, untouched.Height);
    }

    [Fact]
    public async Task CompressAsync_PercentScalesBothAxes()
    {
        var inputPath = CreateNoiseImage("source.png", 200, 120);

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { ScaleMode = ScaleMode.Percent, ScaleValue = 50 });

        Assert.Equal(100, result.Width);
        Assert.Equal(60, result.Height);
    }

    /// <summary>
    /// Re-compressing an image that is already smaller than the encoder's floor grows it. The
    /// original is kept instead, and the result says so rather than reporting a negative saving.
    /// </summary>
    [Fact]
    public async Task CompressAsync_KeepsTheOriginalWhenTheResultWouldBeLarger()
    {
        var inputPath = CreateNoiseImage("coarse.jpg", 96, 96, quality: 5);
        var originalSize = new FileInfo(inputPath).Length;

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { Quality = 100, SkipWhenLarger = true });

        Assert.Equal(ResultState.Skipped, result.State);
        Assert.Equal(inputPath, result.CompressedPath);
        Assert.Equal(originalSize, new FileInfo(inputPath).Length);
        Assert.Equal(result.OriginalSize, result.CompressedSize);
    }

    /// <summary>
    /// A conversion was asked for explicitly, so it is delivered even when the bytes go up —
    /// otherwise a folder converted to PNG quietly keeps half its JPEGs.
    /// </summary>
    [Fact]
    public async Task CompressAsync_DeliversAConversionEvenWhenItGrowsTheFile()
    {
        var inputPath = CreateNoiseImage("coarse.jpg", 96, 96, quality: 5);

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputFormat = "png", Quality = 100, SkipWhenLarger = true });

        Assert.Equal(ResultState.Compressed, result.State);
        Assert.EndsWith(".png", result.CompressedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompressAsync_PrivacyStrippingNeverReturnsTheUnsanitizedOriginal()
    {
        var inputPath = CreateNoiseImage("private.jpg", 96, 96, quality: 5);
        using (var image = Image.Load<Rgba32>(inputPath))
        {
            var exif = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
            exif.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Artist, "Private artist");
            image.Metadata.ExifProfile = exif;
            image.Save(inputPath, new JpegEncoder { Quality = 5 });
        }
        var result = await _compressor.CompressAsync(inputPath,
            new CompressionOptions { Quality = 100, StripMetadata = true, SkipWhenLarger = true });
        Assert.Equal(ResultState.Compressed, result.State);
        Assert.NotEqual(inputPath, result.CompressedPath);
        using var output = Image.Load(result.CompressedPath);
        Assert.Null(output.Metadata.ExifProfile);
    }

    [Fact]
    public async Task CompressAsync_OverwriteReplacesTheSourceAndKeepsItsFormat()
    {
        var inputPath = CreateNoiseImage("source.jpg", 240, 240, quality: 95);
        var originalSize = new FileInfo(inputPath).Length;

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputFormat = "webp", Quality = 30, OutputMode = OutputMode.Overwrite });

        Assert.Equal(ResultState.Compressed, result.State);
        Assert.Equal(inputPath, result.CompressedPath);
        Assert.Single(Directory.GetFiles(_tempDirectory));
        Assert.True(new FileInfo(inputPath).Length < originalSize);
    }

    [Fact]
    public async Task CompressAsync_SubfolderWritesBesideTheSource()
    {
        var inputPath = CreateNoiseImage("source.jpg", 120, 120);

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputMode = OutputMode.Subfolder, Quality = 60 });

        var expected = Path.Combine(_tempDirectory, "compressed", "source.jpg");
        Assert.Equal(expected, result.CompressedPath);
        Assert.True(File.Exists(expected));
        Assert.True(File.Exists(inputPath));
    }

    /// <summary>
    /// A recursive run into a custom folder has to reproduce the source's shape, or every
    /// <c>DSC_0001.jpg</c> in a photo library overwrites the last one.
    /// </summary>
    [Fact]
    public async Task CompressAsync_CustomFolderMirrorsTheSourceStructure()
    {
        var inputPath = CreateNoiseImage(Path.Combine("source", "2026", "photo.jpg"), 96, 96);
        var destination = Path.Combine(_tempDirectory, "out");

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputMode = OutputMode.CustomFolder, OutputFolder = destination },
            sourceRoot: Path.Combine(_tempDirectory, "source"));

        Assert.Equal(Path.Combine(destination, "2026", "photo.jpg"), result.CompressedPath);
        Assert.True(File.Exists(result.CompressedPath));
    }

    [Fact]
    public async Task CompressAsync_TargetSizeSearchLandsUnderTheBudget()
    {
        var inputPath = CreateNoiseImage("photo.jpg", 900, 600, quality: 100);

        // A quarter of the source rather than an absolute figure: the point is that the search finds
        // a quality that fits, not that this machine's encoder hits a particular byte count.
        var target = new FileInfo(inputPath).Length / 4;

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputFormat = "jpeg", TargetSizeBytes = target });

        Assert.Equal(ResultState.Compressed, result.State);
        Assert.True(result.CompressedSize <= target, $"目标 {target} 字节，实际 {result.CompressedSize} 字节。");
        Assert.False(result.TargetMissed);
        Assert.InRange(result.QualityUsed, 5, 100);
    }

    /// <summary>
    /// A target no quality can reach is reported rather than swallowed: the fix is a resize or a
    /// different format, and the user cannot know to reach for either if the run claims success.
    /// </summary>
    [Fact]
    public async Task CompressAsync_FlagsAnUnreachableTargetSize()
    {
        var inputPath = CreateNoiseImage("photo.jpg", 1200, 900, quality: 100);

        var result = await _compressor.CompressAsync(
            inputPath,
            new CompressionOptions { OutputFormat = "jpeg", TargetSizeBytes = 512 });

        Assert.True(result.TargetMissed);
        Assert.Equal(5, result.QualityUsed);
    }

    [Fact]
    public async Task CompressAsync_ReportsAFailureRatherThanThrowing()
    {
        var inputPath = Path.Combine(_tempDirectory, "broken.jpg");
        File.WriteAllText(inputPath, "this is not a JPEG");

        var result = await _compressor.CompressAsync(inputPath, new CompressionOptions());

        Assert.Equal(ResultState.Failed, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task CompressAsync_RejectsAnUnsupportedFormat()
    {
        var inputPath = Path.Combine(_tempDirectory, "notes.txt");
        File.WriteAllText(inputPath, "text");

        var result = await _compressor.CompressAsync(inputPath, new CompressionOptions());

        Assert.Equal(ResultState.Failed, result.State);
    }

    /// <summary>A file that cannot be read must not take the rest of the batch down with it.</summary>
    [Fact]
    public async Task CompressBatchAsync_CarriesOnPastABrokenFile()
    {
        var broken = Path.Combine(_tempDirectory, "broken.jpg");
        File.WriteAllText(broken, "not an image");

        var files = new[] { CreateNoiseImage("one.jpg", 64, 64), broken, CreateNoiseImage("two.jpg", 64, 64) };

        var (results, summary) = await _compressor.CompressBatchAsync(files, new CompressionOptions { Quality = 60 });

        Assert.Equal(3, results.Count);
        Assert.Equal(3, summary.TotalFiles);
        Assert.Equal(1, summary.FailedFiles);
    }

    [Fact]
    public async Task CompressBatchAsync_CancellationKeepsWhatAlreadyFinished()
    {
        var files = Enumerable.Range(0, 6).Select(index => CreateNoiseImage($"{index}.jpg", 64, 64)).ToList();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (results, summary) = await _compressor.CompressBatchAsync(
            files,
            new CompressionOptions(),
            ct: cts.Token);

        // Nothing was allowed to start, but the call still returns a usable, self-consistent answer
        // rather than throwing the whole batch away.
        Assert.Equal(results.Count, summary.TotalFiles);
        Assert.True(results.Count < files.Count);
    }

    [Fact]
    public void GetCapabilities_DescribesTheEngineThatIsActuallyLoaded()
    {
        var capabilities = _compressor.GetCapabilities();

        Assert.False(string.IsNullOrWhiteSpace(capabilities.JpegOptimizer));
        Assert.False(string.IsNullOrWhiteSpace(capabilities.PngQuantization));
        Assert.Equal(Environment.ProcessorCount, capabilities.ProcessorCount);

        // PngLossless is empty on a machine without oxipng, so the line the panel shows is what has
        // to be legible either way.
        Assert.False(string.IsNullOrWhiteSpace(capabilities.PngLosslessDisplay));
    }

    // ==== Helpers =============================================================================

    /// <summary>
    /// Writes an image of deterministic pseudo-random pixels.
    /// </summary>
    /// <remarks>
    /// Noise rather than a flat colour, because a solid rectangle compresses to almost nothing in
    /// every format and the encoder's fixed overhead then dominates every size comparison. The
    /// pattern comes from the pixel coordinates, so the same call always produces the same bytes.
    /// </remarks>
    private string CreateNoiseImage(string relativePath, int width, int height, int quality = 90)
    {
        var path = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var seed = unchecked((uint)((x * 73856093) ^ (y * 19349663) ^ ((x + y) * 83492791)));
                    seed ^= seed >> 13;
                    seed = unchecked(seed * 2654435761);

                    row[x] = new Rgba32((byte)seed, (byte)(seed >> 8), (byte)(seed >> 16), byte.MaxValue);
                }
            }
        });

        if (Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            image.Save(path, new JpegEncoder { Quality = quality });
        }
        else
        {
            image.Save(path);
        }

        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, $"条件在 {timeoutMs} ms 内没有满足。");
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
