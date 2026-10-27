using ImageMinify.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Tests;

public sealed class EncodingCancellationTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    [InlineData("bmp")]
    public void AlreadyCanceled_DoesNotWriteOrStripSource(string format)
    {
        IFormatEncoder encoder = format switch
        {
            "png" => new PngCompressor(),
            "jpeg" => new JpegCompressor(),
            "webp" => new WebpCompressor(),
            _ => new BmpPassthroughEncoder(),
        };
        using var source = new Image<Rgba32>(16, 16);
        var exif = new ExifProfile();
        source.Metadata.ExifProfile = exif;
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => encoder.Encode(source, output,
            new EncodeRequest(100, true) { CancellationToken = cancellation.Token }));
        Assert.Equal(0, output.Length);
        Assert.Same(exif, source.Metadata.ExifProfile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancelDuringPngHeader_StopsBeforePixelDataAndRestoresMetadata(bool stripMetadata)
    {
        using var source = new Image<Rgba32>(128, 128);
        var random = new Random(20261027);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                source[x, y] = new Rgba32((byte)random.Next(256), (byte)random.Next(256),
                    (byte)random.Next(256), 255);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Artist, "private artist");
        source.Metadata.ExifProfile = exif;
        using var complete = new MemoryStream();
        var encoder = new PngEncoder { ColorType = PngColorType.Rgb };
        EncodingMetadata.Encode(source, complete, encoder, stripMetadata, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        using var output = new CancelOnWriteStream(cancellation);
        var error = Assert.ThrowsAny<OperationCanceledException>(() =>
            EncodingMetadata.Encode(source, output, encoder, stripMetadata, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.InRange(output.Length, 1, complete.Length - 1);
        Assert.Same(exif, source.Metadata.ExifProfile);
        Assert.True(source.Metadata.ExifProfile.TryGetValue(ExifTag.Artist, out var artist));
        Assert.Equal("private artist", artist.Value);
    }

    private sealed class CancelOnWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            cancellation.Cancel();
        }
    }
}
