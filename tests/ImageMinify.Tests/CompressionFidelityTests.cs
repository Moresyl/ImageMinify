using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class CompressionFidelityTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public void PrivacyStripping_RetainsDisplayProfile(string format)
    {
        IFormatEncoder encoder = format switch
        {
            "png" => new PngCompressor(),
            "jpeg" => new JpegCompressor(),
            _ => new WebpCompressor(),
        };
        using var source = new Image<Rgba32>(16, 16, Color.Red);
        var profile = new IccProfile();
        profile.Header.Size = 132;
        profile.Header.DataColorSpace = IccColorSpaceType.Rgb;
        profile.Header.ProfileConnectionSpace = IccColorSpaceType.CieXyz;
        source.Metadata.IccProfile = profile;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Artist, "Private author");
        source.Metadata.ExifProfile = exif;
        using var buffer = new MemoryStream();
        encoder.Encode(source, buffer, new EncodeRequest(100, true));
        using var result = Image.Load(buffer.ToArray());
        Assert.Null(result.Metadata.ExifProfile);
        Assert.NotNull(result.Metadata.IccProfile);
        Assert.Equal(profile.ToByteArray(), result.Metadata.IccProfile!.ToByteArray());
        Assert.Same(exif, source.Metadata.ExifProfile);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(82)]
    [InlineData(100)]
    public void Png_KeepsEveryAlphaSampleAndNeverExceedsItsLosslessCandidate(int quality)
    {
        using var source = new Image<Rgba32>(64, 64);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                source[x, y] = new Rgba32((byte)(x * 4), (byte)(y * 4), 123, (byte)((x + y * 64) % 256));
        var encoder = new PngCompressor();
        using var smart = new MemoryStream();
        using var exact = new MemoryStream();
        encoder.Encode(source, smart, new EncodeRequest(quality, true));
        encoder.Encode(source, exact, new EncodeRequest(100, true));
        Assert.True(smart.Length <= exact.Length);
        using var result = Image.Load<Rgba32>(smart.ToArray());
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                Assert.Equal(source[x, y].A, result[x, y].A);
                if (quality == 100) Assert.Equal(source[x, y], result[x, y]);
            }
    }

    [Fact]
    public void LosslessWebp_KeepsHiddenRgb()
    {
        using var source = new Image<Rgba32>(8, 8, new Rgba32(123, 45, 67, 0));
        using var buffer = new MemoryStream();
        new WebpCompressor().Encode(source, buffer, new EncodeRequest(100, true));
        using var result = Image.Load<Rgba32>(buffer.ToArray());
        Assert.Equal(source[0, 0], result[0, 0]);
    }
}
