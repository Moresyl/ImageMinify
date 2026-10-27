using ImageMinify.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>
/// The encoder for BMP output, which exists mostly so that "keep the original format" has an answer
/// for a BMP source.
/// </summary>
/// <remarks>
/// BMP stores raw pixels and has no quality dial, so there is nothing here for a quality slider or a
/// target-size search to work with. The one saving available is dropping an alpha channel that is
/// fully opaque, which takes a 32-bit bitmap down to 24 — a flat 25%. Anyone wanting a BMP to get
/// substantially smaller wants a different output format, and the view says so.
/// </remarks>
public sealed class BmpPassthroughEncoder : IFormatEncoder
{
    public string Extension => ".bmp";

    public bool SupportsQuality => false;

    public Image<Rgba32> Prepare(Image<Rgba32> image) => image;

    public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var transparent = PixelInspector.HasTransparency(image);
        EncodingMetadata.Encode(image, output, new BmpEncoder
        {
            BitsPerPixel = transparent ? BmpBitsPerPixel.Pixel32 : BmpBitsPerPixel.Pixel24,
            SupportTransparency = transparent,
            SkipMetadata = request.StripMetadata,
        }, request.StripMetadata, request.CancellationToken);
    }

    public void PostProcess(string filePath, bool stripMetadata)
    {
    }
}
