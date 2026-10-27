using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace ImageMinify.Services;

internal static class PngLosslessPalette
{
    public static IQuantizer? Create(Image<Rgba32> image)
    {
        // Animated output keeps its existing shared-palette and frame metadata path.
        if (image.Frames.Count != 1) return null;
        var colors = new HashSet<Rgba32>();
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
                foreach (var pixel in rows.GetRowSpan(y))
                    if (colors.Add(pixel) && colors.Count > 256) return;
        });
        if (colors.Count > 256) return null;
        var palette = colors.OrderBy(pixel => pixel.PackedValue).Select(Color.FromPixel).ToArray();
        return new PaletteQuantizer(palette, new QuantizerOptions { Dither = null });
    }

    public static bool Matches(Image<Rgba32> source, Image<Rgba32> candidate)
    {
        if (source.Frames.Count != 1 || candidate.Frames.Count != 1
            || source.Width != candidate.Width || source.Height != candidate.Height) return false;
        var exact = true;
        source.ProcessPixelRows(candidate, (left, right) =>
        {
            for (var y = 0; y < left.Height; y++)
                if (!left.GetRowSpan(y).SequenceEqual(right.GetRowSpan(y)))
                { exact = false; return; }
        });
        return exact;
    }
}
