using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>Privacy stripping must not remove the colour interpretation of unchanged samples.</summary>
internal static class EncodingMetadata
{
    public static void Encode<TPixel>(Image<TPixel> image, Stream output, IImageEncoder encoder,
        bool stripMetadata, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ImageSharp's synchronous public entry point drops cancellation. Its async
        // entry forwards the token to the encoder, including on our memory streams.
        // The compression pipeline already runs on a worker thread.
        Write(image, stripMetadata, () => encoder.EncodeAsync(image, output, cancellationToken)
            .GetAwaiter().GetResult());
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void Write(Image image, bool stripMetadata, Action write)
    {
        if (!stripMetadata)
        {
            write();
            return;
        }

        var metadata = image.Metadata;
        var exif = metadata.ExifProfile;
        var iptc = metadata.IptcProfile;
        var xmp = metadata.XmpProfile;
        metadata.ExifProfile = null;
        metadata.IptcProfile = null;
        metadata.XmpProfile = null;
        try
        {
            write();
        }
        finally
        {
            metadata.ExifProfile = exif;
            metadata.IptcProfile = iptc;
            metadata.XmpProfile = xmp;
        }
    }
}
