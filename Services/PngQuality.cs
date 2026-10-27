using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>Reject palette candidates that erase alpha or introduce large visible colour errors.</summary>
internal static class PngQuality
{
    public static bool Accepts(Image<Rgba32> source, Image<Rgba32> candidate, int quality)
    {
        if (source.Width != candidate.Width || source.Height != candidate.Height
            || source.Frames.Count != candidate.Frames.Count) return false;

        var maxError = Math.Max(12, 128 - quality);
        var minimumPsnr = Math.Max(20, quality * 0.5 - 10);
        var maxMeanSquare = 255 * 255 / Math.Pow(10, minimumPsnr / 10);
        for (var frame = 0; frame < source.Frames.Count; frame++)
        {
            double squaredError = 0;
            // One row of 8x8 blocks bounds scratch memory independently of height.
            var blockErrors = new double[(source.Width + 7) / 8];
            var accepted = true;
            source.Frames[frame].ProcessPixelRows(candidate.Frames[frame], (original, decoded) =>
            {
                for (var y = 0; y < original.Height; y++)
                {
                    var left = original.GetRowSpan(y);
                    var right = decoded.GetRowSpan(y);
                    for (var x = 0; x < left.Length; x++)
                    {
                        var a = left[x];
                        var b = right[x];
                        if (a.A != b.A) { accepted = false; return; }
                        // Exact alpha makes the RGB difference identical on black and white.
                        var r = (a.R - b.R) * a.A / 255.0;
                        var g = (a.G - b.G) * a.A / 255.0;
                        var blue = (a.B - b.B) * a.A / 255.0;
                        if (Math.Max(Math.Abs(r), Math.Max(Math.Abs(g), Math.Abs(blue))) > maxError)
                        { accepted = false; return; }
                        var error = r * r + g * g + blue * blue;
                        squaredError += error;
                        blockErrors[x / 8] += error;
                    }
                    if (y % 8 == 7 || y == original.Height - 1)
                    {
                        var blockHeight = y % 8 + 1;
                        for (var block = 0; block < blockErrors.Length; block++)
                        {
                            var blockWidth = Math.Min(8, original.Width - block * 8);
                            if (blockErrors[block] / (blockWidth * blockHeight * 3) > maxMeanSquare)
                            { accepted = false; return; }
                        }
                        Array.Clear(blockErrors);
                    }
                }
            });
            if (!accepted || squaredError / ((double)source.Width * source.Height * 3) > maxMeanSquare)
                return false;
        }
        return true;
    }
}
