using ImageMinify.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class ExifServiceTests : IDisposable
{
    private readonly ExifService _service = new();
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-exif-{Guid.NewGuid():N}");

    public ExifServiceTests()
    {
        System.IO.Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// The "keep metadata" path is deliberately not a straight copy. A camera profile carries GPS
    /// coordinates, an embedded thumbnail and maker notes that can outweigh the compressed pixels;
    /// what survives is which camera, which software, and when.
    /// </summary>
    [Fact]
    public void Reduce_KeepsCameraSoftwareAndDateAndNothingElse()
    {
        var inputPath = WriteJpegWithExif("input.jpg", exif =>
        {
            exif.SetValue(ExifTag.Make, "Canon");
            exif.SetValue(ExifTag.Model, "R6");
            exif.SetValue(ExifTag.Software, "Original");
            exif.SetValue(ExifTag.DateTime, "2026:03:16 10:11:12");
            exif.SetValue(ExifTag.Artist, "RemoveMe");
            exif.SetValue(ExifTag.Copyright, "RemoveMeToo");
        });

        var outputPath = Path.Combine(_tempDirectory, "output.jpg");
        using (var image = Image.Load<Rgba32>(inputPath))
        {
            _service.Reduce(image, inputPath);
            image.Save(outputPath, new JpegEncoder { Quality = 85 });
        }

        var ifd0 = ImageMetadataReader.ReadMetadata(outputPath).OfType<ExifIfd0Directory>().FirstOrDefault();

        Assert.NotNull(ifd0);
        Assert.Equal("Canon", ifd0!.GetString(ExifDirectoryBase.TagMake));
        Assert.Equal("R6", ifd0.GetString(ExifDirectoryBase.TagModel));
        Assert.Equal("Original", ifd0.GetString(ExifDirectoryBase.TagSoftware));
        Assert.Equal("2026:03:16 10:11:12", ifd0.GetString(ExifDirectoryBase.TagDateTime));
        Assert.Null(ifd0.GetString(ExifDirectoryBase.TagArtist));
        Assert.Null(ifd0.GetString(ExifDirectoryBase.TagCopyright));
    }

    /// <summary>
    /// The rotation is baked into the pixels before this runs, so the tag has to say "upright".
    /// Carrying the original value forward rotates the image a second time in every viewer.
    /// </summary>
    [Fact]
    public void Reduce_ResetsTheOrientationTag()
    {
        var inputPath = WriteJpegWithExif("rotated.jpg", exif =>
        {
            exif.SetValue(ExifTag.Make, "Canon");
            exif.SetValue(ExifTag.Orientation, (ushort)6);
        });

        using var image = Image.Load<Rgba32>(inputPath);
        _service.Reduce(image, inputPath);

        Assert.Equal((ushort)1, Read(image.Metadata.ExifProfile, ExifTag.Orientation));
    }

    [Fact]
    public void Reduce_DropsIptcAndXmpAlongWithTheRest()
    {
        var inputPath = WriteJpegWithExif("input.jpg", exif => exif.SetValue(ExifTag.Make, "Canon"));

        using var image = Image.Load<Rgba32>(inputPath);
        image.Metadata.XmpProfile = new SixLabors.ImageSharp.Metadata.Profiles.Xmp.XmpProfile("<x:xmpmeta/>"u8.ToArray());

        _service.Reduce(image, inputPath);

        Assert.Null(image.Metadata.IptcProfile);
        Assert.Null(image.Metadata.XmpProfile);
    }

    /// <summary>A file with no EXIF at all is a normal outcome, not a failure.</summary>
    [Fact]
    public void Reduce_LeavesNoProfileWhenTheSourceHadNone()
    {
        var inputPath = Path.Combine(_tempDirectory, "plain.png");
        using (var source = new Image<Rgba32>(16, 16, Color.CornflowerBlue))
        {
            source.Save(inputPath);
        }

        using var image = Image.Load<Rgba32>(inputPath);
        _service.Reduce(image, inputPath);

        Assert.Null(image.Metadata.ExifProfile);
    }

    /// <summary>An unreadable file must not take a compression down with it.</summary>
    [Fact]
    public void Reduce_SurvivesAPathItCannotRead()
    {
        using var image = new Image<Rgba32>(8, 8, Color.Red);

        _service.Reduce(image, Path.Combine(_tempDirectory, "does-not-exist.jpg"));

        Assert.Null(image.Metadata.ExifProfile);
    }

    [Fact]
    public void Strip_RemovesPersonalProfilesAndKeepsTheColourProfile()
    {
        using var image = new Image<Rgba32>(16, 16, Color.Red);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "Canon");
        image.Metadata.ExifProfile = exif;
        var icc = new SixLabors.ImageSharp.Metadata.Profiles.Icc.IccProfile();
        image.Metadata.IccProfile = icc;

        _service.Strip(image);

        Assert.Null(image.Metadata.ExifProfile);
        Assert.Null(image.Metadata.IptcProfile);
        Assert.Null(image.Metadata.XmpProfile);
        Assert.Same(icc, image.Metadata.IccProfile);
    }

    /// <summary>
    /// A phone photo is stored in the sensor's orientation with a tag saying which way up it is.
    /// Every viewer honours the tag but a resize does not, so the rotation has to be baked in before
    /// anything measures the image — otherwise "longest edge 1920" is applied to the wrong axis.
    /// </summary>
    [Fact]
    public void AutoOrient_RotatesThePixelsAndClearsTheTag()
    {
        using var image = new Image<Rgba32>(40, 20, Color.Red);
        var exif = new ExifProfile();

        // 6 means "rotate 90° clockwise to display", so a 40×20 stored image is really 20×40.
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile = exif;

        _service.AutoOrient(image);

        Assert.Equal(20, image.Width);
        Assert.Equal(40, image.Height);

        // AutoOrient either rewrites the tag to "upright" or drops the profile; both say the same
        // thing, which is that nothing downstream should rotate these pixels again.
        Assert.Equal((ushort)1, Read(image.Metadata.ExifProfile, ExifTag.Orientation) ?? 1);
    }

    [Fact]
    public void AutoOrient_LeavesAnUnmarkedImageAlone()
    {
        using var image = new Image<Rgba32>(40, 20, Color.Red);

        _service.AutoOrient(image);

        Assert.Equal(40, image.Width);
        Assert.Equal(20, image.Height);
    }

    // ==== Helpers =============================================================================

    /// <summary>Reads a tag, or null when the profile is absent or does not carry it.</summary>
    private static T? Read<T>(ExifProfile? profile, ExifTag<T> tag)
        where T : struct =>
        profile is not null && profile.TryGetValue(tag, out var value) ? value.Value : null;

    private string WriteJpegWithExif(string name, Action<ExifProfile> configure)
    {
        var path = Path.Combine(_tempDirectory, name);

        using var image = new Image<Rgba32>(64, 64, Color.Red);
        var exif = new ExifProfile();
        configure(exif);
        image.Metadata.ExifProfile = exif;
        image.Save(path, new JpegEncoder { Quality = 90 });

        return path;
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempDirectory))
        {
            System.IO.Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
