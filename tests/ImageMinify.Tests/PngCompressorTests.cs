using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class PngCompressorTests : IDisposable
{
    private readonly PngCompressor _encoder = new();
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-png-{Guid.NewGuid():N}");

    public PngCompressorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Extension_IsPng()
    {
        Assert.Equal(".png", _encoder.Extension);
        Assert.True(_encoder.SupportsQuality);
    }

    /// <summary>
    /// Quantisation is the only real lever PNG has, and it is where the 60–70% saving on screenshots
    /// and flat artwork comes from.
    /// </summary>
    [Fact]
    public void Encode_QuantisesBelowTheLosslessThreshold()
    {
        // Noise, not a gradient: a smooth ramp is the one image where a palette loses, because the
        // row filters flatten it almost perfectly and quantisation only adds banding for zlib to
        // encode. Detailed artwork is what the quality slider is actually for.
        using var image = CreateNoise(160, 160);

        var quantised = Encode(image, new EncodeRequest(70, StripMetadata: true));

        // Counted rather than read off the PNG header: what matters is that the pixels went through
        // a palette, and a colour count is the same answer whatever the encoder wrote in the chunk.
        using var decoded = Image.Load<Rgba32>(quantised);
        Assert.True(CountDistinctColours(image) > 256);
        Assert.True(CountDistinctColours(decoded) <= 256);

        Assert.True(quantised.Length < Encode(image, new EncodeRequest(100, StripMetadata: true)).Length);
    }

    /// <summary>
    /// A user who asks for maximum quality is asking for the pixels back exactly, so the palette step
    /// is skipped entirely and only zlib works on the file.
    /// </summary>
    [Fact]
    public void Encode_IsLosslessAtTheTopOfTheRange()
    {
        using var image = CreateGradient(64, 64);

        using var decoded = Image.Load<Rgba32>(Encode(image, new EncodeRequest(100, StripMetadata: true)));

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                Assert.Equal(image[x, y], decoded[x, y]);
            }
        }
    }

    /// <summary>
    /// The palette path must not flatten alpha. A logo or an icon coming back opaque is silent,
    /// irreversible damage to exactly the kind of PNG people bring to a compressor.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(94)]
    [InlineData(100)]
    public void Encode_PreservesTransparency(int quality)
    {
        using var image = CreateTranslucentBadge(48, 48);

        using var decoded = Image.Load<Rgba32>(Encode(image, new EncodeRequest(quality, StripMetadata: true)));

        Assert.Equal(0, decoded[2, 2].A);
        Assert.InRange(decoded[24, 24].A, 80, 100);
    }

    [Fact]
    public void Encode_StripMetadataLeavesNoExifBehind()
    {
        using var image = CreateGradient(48, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "ShouldBeRemoved");
        image.Metadata.ExifProfile = exif;

        using var stripped = Image.Load(Encode(image, new EncodeRequest(70, StripMetadata: true)));

        Assert.Null(stripped.Metadata.ExifProfile);
    }

    /// <summary>
    /// The fast path is only used for probes during the target-size search, and the search relies on
    /// the slow path never producing a larger file at the same quality.
    /// </summary>
    [Fact]
    public void Encode_TheSlowPathIsNeverLargerThanTheFastOne()
    {
        using var image = CreateGradient(128, 128);

        var fast = Encode(image, new EncodeRequest(70, StripMetadata: true, Fast: true));
        var slow = Encode(image, new EncodeRequest(70, StripMetadata: true));

        Assert.True(slow.Length <= fast.Length, $"最佳压缩反而更大：{fast.Length} → {slow.Length}");
    }

    [Fact]
    public void Encode_ClampsAnOutOfRangeQualityRatherThanThrowing()
    {
        using var image = CreateGradient(32, 32);

        Assert.NotEmpty(Encode(image, new EncodeRequest(0, StripMetadata: true)));
        Assert.NotEmpty(Encode(image, new EncodeRequest(1000, StripMetadata: true)));
    }

    [Fact]
    public void Prepare_ReturnsTheSameImage()
    {
        using var image = CreateGradient(16, 16);

        Assert.Same(image, _encoder.Prepare(image));
    }

    /// <summary>
    /// oxipng is an optional external tool and its pass is lossless, so with or without it the file
    /// has to decode to the same pixels afterwards and never grow.
    /// </summary>
    [Fact]
    public void PostProcess_LeavesAValidFileWhetherOrNotTheToolIsInstalled()
    {
        var path = Path.Combine(_tempDirectory, "output.png");

        using var source = CreateGradient(96, 96);
        File.WriteAllBytes(path, Encode(source, new EncodeRequest(100, StripMetadata: true)));

        var before = new FileInfo(path).Length;
        _encoder.PostProcess(path, stripMetadata: true);
        var after = new FileInfo(path).Length;

        Assert.True(after <= before, $"优化后反而变大：{before} → {after}");

        using var decoded = Image.Load<Rgba32>(path);
        Assert.Equal(source[10, 10], decoded[10, 10]);
    }

    [Fact]
    public void PostProcess_IgnoresAMissingFile()
    {
        _encoder.PostProcess(Path.Combine(_tempDirectory, "does-not-exist.png"), stripMetadata: true);
    }

    [Fact]
    public void CapabilityNames_MatchWhatWasActuallyFound()
    {
        Assert.Equal(_encoder.HasImagequant ? "imagequant" : "ImageSharp", _encoder.QuantizationName);
        Assert.Equal(_encoder.HasOxipng ? "oxipng" : "未安装", _encoder.LosslessOptimizerName);
    }

    // ==== Helpers =============================================================================

    private byte[] Encode(Image<Rgba32> image, EncodeRequest request)
    {
        using var buffer = new MemoryStream();
        _encoder.Encode(image, buffer, request);
        return buffer.ToArray();
    }

    /// <summary>
    /// A smooth gradient: more than 256 distinct colours, so quantisation has something to do, but
    /// still the kind of image PNG is used for.
    /// </summary>
    private static Image<Rgba32> CreateGradient(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)(x * 255 / Math.Max(1, accessor.Width - 1)),
                        (byte)(y * 255 / Math.Max(1, accessor.Height - 1)),
                        (byte)((x + y) % 256),
                        byte.MaxValue);
                }
            }
        });

        return image;
    }

    /// <summary>Noise, so a palette has something to win and the encoder's floor cannot dominate.</summary>
    private static Image<Rgba32> CreateNoise(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var seed = unchecked((uint)((x * 73856093) ^ (y * 19349663)));
                    seed ^= seed >> 13;
                    seed = unchecked(seed * 2654435761);

                    row[x] = new Rgba32((byte)seed, (byte)(seed >> 8), (byte)(seed >> 16), byte.MaxValue);
                }
            }
        });

        return image;
    }

    /// <summary>
    /// A shape on a fully transparent background with a partly transparent body — the two kinds of
    /// alpha a logo carries, and the two a quantiser can throw away.
    /// </summary>
    private static Image<Rgba32> CreateTranslucentBadge(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            var centreX = accessor.Width / 2D;
            var centreY = accessor.Height / 2D;
            var radius = Math.Min(centreX, centreY) * 0.8;

            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var inside = ((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY)) <= radius * radius;
                    row[x] = inside
                        ? new Rgba32((byte)(120 + (x % 60)), 60, 30, 90)
                        : new Rgba32(0, 0, 0, 0);
                }
            }
        });

        return image;
    }

    private static int CountDistinctColours(Image<Rgba32> image)
    {
        var colours = new HashSet<uint>();

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    colours.Add(pixel.PackedValue);
                }
            }
        });

        return colours.Count;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
