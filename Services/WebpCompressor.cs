using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

public sealed class WebpCompressor : IFormatEncoder
{
    /// <summary>At and above this quality WebP switches to its lossless mode.</summary>
    private const int LosslessThreshold = 100;

    public string Extension => ".webp";

    public bool SupportsQuality => true;

    public Image<Rgba32> Prepare(Image<Rgba32> image) => image;

    public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
    {
        var quality = Math.Clamp(request.Quality, 1, 100);
        var lossless = quality >= LosslessThreshold;

        image.Save(output, new WebpEncoder
        {
            Quality = quality,
            FileFormat = lossless ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy,

            // Method is WebP's speed/size dial, from 0 to 6. The top setting costs several times the
            // CPU of the default for a few percent of size — worth paying for the file that gets
            // kept, and not worth paying six times over for probes the search is going to discard.
            Method = request.Fast ? WebpEncodingMethod.Level2 : WebpEncodingMethod.BestQuality,
            SkipMetadata = request.StripMetadata,
        });
    }

    /// <summary>
    /// Nothing to do. WebP's encoder already searches its own parameter space at
    /// <see cref="WebpEncodingMethod.BestQuality"/>, and there is no equivalent of jpegtran or
    /// oxipng to run over the result.
    /// </summary>
    public void PostProcess(string filePath, bool stripMetadata)
    {
    }
}
