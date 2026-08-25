using System.Diagnostics;
using ImageMinify.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace ImageMinify.Services;

public sealed class PngCompressor : IFormatEncoder
{
    /// <summary>
    /// At and above this quality the palette is abandoned for true colour. Below it, quantisation is
    /// where essentially all of the saving comes from.
    /// </summary>
    private const int LosslessThreshold = 95;

    private readonly ImagequantNativeQuantizer _imagequantNativeQuantizer = new();
    private readonly string? _oxipngToolPath = NativeLibraryLoader.ResolveToolPath("oxipng.exe", "oxipng");

    public string Extension => ".png";

    public bool SupportsQuality => true;

    public bool HasImagequant => _imagequantNativeQuantizer.IsAvailable;

    public bool HasOxipng => _oxipngToolPath is not null;

    public string QuantizationName => HasImagequant ? "imagequant" : "ImageSharp";

    public string LosslessOptimizerName => HasOxipng ? "oxipng" : "未安装";

    public Image<Rgba32> Prepare(Image<Rgba32> image) => image;

    /// <summary>
    /// Encodes as a quantised palette PNG, or losslessly at the top of the quality range.
    /// </summary>
    /// <remarks>
    /// The split at <see cref="LosslessThreshold"/> is what makes "quality 100" mean something for
    /// PNG. Quantisation is the only real lever the format has — 24-bit colour down to a 256-entry
    /// palette, which is where the 60–70% savings on screenshots and flat artwork come from — but it
    /// is lossy, and a user who asks for maximum quality is asking for the pixels back exactly.
    /// Above the threshold the palette step is skipped entirely and the file stays bit-identical to
    /// the source, with only zlib and oxipng working on it.
    /// </remarks>
    public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
    {
        var quality = Math.Clamp(request.Quality, 1, 100);

        // A probe during the target-size search uses the cheaper zlib level. It is never larger than
        // BestCompression at the same palette, so a probe that fits the target still fits once the
        // real encode runs.
        var compressionLevel = request.Fast
            ? PngCompressionLevel.DefaultCompression
            : PngCompressionLevel.BestCompression;

        var encoder = quality >= LosslessThreshold
            ? new PngEncoder
            {
                CompressionLevel = compressionLevel,
                SkipMetadata = request.StripMetadata,
            }
            : new PngEncoder
            {
                CompressionLevel = compressionLevel,
                ColorType = PngColorType.Palette,
                Quantizer = CreateQuantizer(image, quality),
                SkipMetadata = request.StripMetadata,
            };

        image.Save(output, encoder);
    }

    /// <summary>
    /// Re-deflates the file and prunes redundant chunks. Lossless, so it runs whatever the quality.
    /// </summary>
    public void PostProcess(string filePath, bool stripMetadata)
    {
        var toolPath = _oxipngToolPath;
        if (toolPath is null)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("4");

            // oxipng defaults to keeping chunks. Only strip when the run asked to — the previous
            // behaviour was an unconditional "--strip all", which quietly discarded the metadata of
            // a user who had explicitly chosen to keep it.
            if (stripMetadata)
            {
                startInfo.ArgumentList.Add("--strip");
                startInfo.ArgumentList.Add("all");
            }

            // oxipng only ever writes the file back if its result is smaller, so there is no
            // size check to do here.
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            if (!process.WaitForExit(60000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone between the timeout and the kill.
                }
            }
        }
        catch
        {
            // oxipng is optional and must never fail the main compression flow.
        }
    }

    private IQuantizer CreateQuantizer(Image<Rgba32> image, int quality)
    {
        if (_imagequantNativeQuantizer.TryCreateQuantizer(image, quality, out var quantizer) && quantizer is not null)
        {
            return quantizer;
        }

        // Without libimagequant, scale the palette with the quality: 32 colours at the bottom of the
        // range up to the format's maximum of 256 at the top.
        var options = new QuantizerOptions
        {
            MaxColors = Math.Clamp(32 + (int)(224 * quality / 100D), 32, 256),
        };

        // Octree builds its tree over RGB alone and hands back opaque colours, so a logo or an icon
        // would come out of the palette path with its transparency flattened — silent, irreversible,
        // and on exactly the kind of PNG people compress. Wu quantises over all four channels, so it
        // is the one that gets used whenever there is alpha to lose. Octree keeps the images it is
        // better at: flat artwork and screenshots, which have no transparency to begin with.
        return PixelInspector.HasTransparency(image)
            ? new WuQuantizer(options)
            : new OctreeQuantizer(options);
    }
}
