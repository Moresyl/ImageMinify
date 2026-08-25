using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class JpegCompressorTests : IDisposable
{
    private readonly JpegCompressor _encoder = new();
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-jpeg-{Guid.NewGuid():N}");

    public JpegCompressorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Extension_IsJpg()
    {
        Assert.Equal(".jpg", _encoder.Extension);
        Assert.True(_encoder.SupportsQuality);
    }

    [Fact]
    public void Encode_WritesAReadableJpegAtTheRequestedSize()
    {
        using var image = CreateNoise(96, 64);

        var bytes = Encode(image, new EncodeRequest(80, StripMetadata: true));

        using var decoded = Image.Load(bytes);
        Assert.Equal(96, decoded.Width);
        Assert.Equal(64, decoded.Height);
        Assert.IsType<JpegFormat>(decoded.Metadata.DecodedImageFormat);
    }

    /// <summary>
    /// Size has to rise with quality across the whole range. The target-size search bisects on this
    /// assumption, so if it ever stopped holding the search would settle on the wrong quality.
    /// </summary>
    [Fact]
    public void Encode_SizeRisesMonotonicallyWithQuality()
    {
        using var image = CreateNoise(160, 160);

        var sizes = new[] { 10, 30, 50, 70, 90, 100 }
            .Select(quality => Encode(image, new EncodeRequest(quality, StripMetadata: true)).Length)
            .ToList();

        for (var index = 1; index < sizes.Count; index++)
        {
            Assert.True(sizes[index] >= sizes[index - 1], $"质量升高反而变小：{string.Join(", ", sizes)}");
        }
    }

    [Fact]
    public void Encode_ClampsAnOutOfRangeQualityRatherThanThrowing()
    {
        using var image = CreateNoise(32, 32);

        Assert.NotEmpty(Encode(image, new EncodeRequest(0, StripMetadata: true)));
        Assert.NotEmpty(Encode(image, new EncodeRequest(1000, StripMetadata: true)));
    }

    [Fact]
    public void Encode_StripMetadataLeavesNoExifBehind()
    {
        using var image = CreateNoise(48, 48);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "ShouldBeRemoved");
        image.Metadata.ExifProfile = exif;

        using var stripped = Image.Load(Encode(image, new EncodeRequest(85, StripMetadata: true)));
        using var kept = Image.Load(Encode(image, new EncodeRequest(85, StripMetadata: false)));

        Assert.Null(stripped.Metadata.ExifProfile);
        Assert.Equal("ShouldBeRemoved", ReadString(kept.Metadata.ExifProfile, ExifTag.Software));
    }

    /// <summary>
    /// A stripped encode must not consume the image's metadata. The target-size search encodes the
    /// same surface up to seven times, so a destructive first pass would make every later decision
    /// about what to keep meaningless.
    /// </summary>
    [Fact]
    public void Encode_StrippingDoesNotAlterTheImageItWasGiven()
    {
        using var image = CreateNoise(32, 32);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "KeepOnTheImage");
        image.Metadata.ExifProfile = exif;

        Encode(image, new EncodeRequest(60, StripMetadata: true));

        Assert.Equal("KeepOnTheImage", ReadString(image.Metadata.ExifProfile, ExifTag.Software));

        using var kept = Image.Load(Encode(image, new EncodeRequest(60, StripMetadata: false)));
        Assert.Equal("KeepOnTheImage", ReadString(kept.Metadata.ExifProfile, ExifTag.Software));
    }

    /// <summary>
    /// JPEG has no alpha channel. Without the composite, a transparent pixel is encoded from whatever
    /// happens to be in its unused colour bytes — which for a cleared surface is black.
    /// </summary>
    [Fact]
    public void Prepare_CompositesTransparencyOntoWhite()
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 0, 0));

        using var surface = _encoder.Prepare(image);

        Assert.NotSame(image, surface);
        Assert.Equal(new Rgba32(255, 255, 255, 255), surface[0, 0]);
    }

    /// <summary>
    /// The composite costs a full image copy, so an opaque image — every photo — must come back as
    /// the same instance rather than being duplicated for nothing.
    /// </summary>
    [Fact]
    public void Prepare_ReturnsAnOpaqueImageUntouched()
    {
        using var image = CreateNoise(16, 16);

        Assert.Same(image, _encoder.Prepare(image));
    }

    /// <summary>
    /// The composite starts as a blank canvas with no metadata of its own. The pipeline has already
    /// decided what this file should carry, so that decision has to survive the copy.
    /// </summary>
    [Fact]
    public void Prepare_CarriesTheChosenMetadataOntoTheComposite()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(20, 40, 60, 128));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Canon");
        image.Metadata.ExifProfile = exif;

        using var surface = _encoder.Prepare(image);

        Assert.Equal("Canon", ReadString(surface.Metadata.ExifProfile, ExifTag.Make));
    }

    /// <summary>
    /// The optimiser is an optional external tool. Whether it is installed or not, the file it was
    /// handed has to still be a valid JPEG afterwards, and never larger than it was.
    /// </summary>
    [Fact]
    public void PostProcess_LeavesAValidFileWhetherOrNotTheToolIsInstalled()
    {
        var path = Path.Combine(_tempDirectory, "output.jpg");
        using (var image = CreateNoise(128, 128))
        {
            File.WriteAllBytes(path, Encode(image, new EncodeRequest(85, StripMetadata: true)));
        }

        var before = new FileInfo(path).Length;
        _encoder.PostProcess(path, stripMetadata: true);
        var after = new FileInfo(path).Length;

        Assert.True(after <= before, $"优化后反而变大：{before} → {after}");

        using var decoded = Image.Load(path);
        Assert.Equal(128, decoded.Width);
    }

    [Fact]
    public void PostProcess_IgnoresAMissingFile()
    {
        _encoder.PostProcess(Path.Combine(_tempDirectory, "does-not-exist.jpg"), stripMetadata: true);
    }

    [Fact]
    public void OptimizerName_MatchesWhetherTheToolWasFound()
    {
        Assert.Equal(_encoder.HasMozJpeg ? "MozJPEG" : "ImageSharp", _encoder.OptimizerName);
    }

    // ==== Helpers =============================================================================

    /// <summary>Reads a text tag, or null when the profile is absent or does not carry it.</summary>
    private static string? ReadString(ExifProfile? profile, ExifTag<string> tag) =>
        profile is not null && profile.TryGetValue(tag, out var value) ? value.Value : null;

    private byte[] Encode(Image<Rgba32> image, EncodeRequest request)
    {
        using var buffer = new MemoryStream();
        _encoder.Encode(image, buffer, request);
        return buffer.ToArray();
    }

    /// <summary>Noise, because a flat colour compresses to the encoder's floor at every quality.</summary>
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

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
