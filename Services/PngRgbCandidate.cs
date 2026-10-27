using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>A bounded true-colour candidate for images that cannot fit a useful 256-colour palette.</summary>
internal static class PngRgbCandidate
{
    public static Image<Rgba32> Create(Image<Rgba32> source, int quality)
    {
        var candidate = source.Clone();
        var step = quality >= 85 ? 4 : 8;
        foreach (var frame in candidate.Frames)
        {
            frame.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        ref var pixel = ref row[x];
                        // Preserve both Alpha and the otherwise invisible source RGB.
                        if (pixel.A == 0) continue;
                        pixel.R = Round(pixel.R);
                        pixel.G = Round(pixel.G);
                        pixel.B = Round(pixel.B);
                    }
                }
            });
        }
        return candidate;

        byte Round(byte value) => (byte)Math.Min(255, (value + step / 2) / step * step);
    }
}
