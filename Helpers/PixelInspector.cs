using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Helpers;

/// <summary>
/// Questions about an image's pixels that the encoders have to answer before they can choose how to
/// write it.
/// </summary>
internal static class PixelInspector
{
    public static bool HasTransparency(Image<Rgba64> image, CancellationToken cancellationToken = default)
    {
        var found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    if (pixel.A == ushort.MaxValue) continue;
                    found = true;
                    return;
                }
            }
        });
        return found;
    }

    /// <summary>
    /// Whether any pixel is not fully opaque.
    /// </summary>
    /// <remarks>
    /// A full scan, but it stops at the first translucent pixel and it is far cheaper than the work
    /// it exists to avoid — a composite in the JPEG encoder, a slower quantiser in the PNG one. A
    /// source decoded from JPEG cannot carry alpha at all, so the common case skips the scan
    /// entirely.
    /// </remarks>
    public static bool HasTransparency(Image<Rgba32> image)
    {
        if (image.Metadata.DecodedImageFormat is JpegFormat)
        {
            return false;
        }

        var found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A != byte.MaxValue)
                    {
                        found = true;
                        return;
                    }
                }
            }
        });

        return found;
    }
}
