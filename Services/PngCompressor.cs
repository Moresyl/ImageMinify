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

    /// <summary>The lossless post-processor in play, or empty when there is none.</summary>
    /// <remarks>
    /// An encoder reports what it found, not what to call the absence of it — the wording for that
    /// is a translation, and it belongs with the rest of them.
    /// </remarks>
    public string LosslessOptimizerName => HasOxipng ? "oxipng" : string.Empty;

    public Image<Rgba32> Prepare(Image<Rgba32> image) => image;

    /// <summary>
    /// Encodes as a quantised palette PNG, or losslessly at the top of the quality range.
    /// </summary>
    /// <remarks>
    /// The split at <see cref="LosslessThreshold"/> is what makes "quality 100" mean something for
    /// PNG. Quantisation is the only real lever the format has — 24-bit colour down to a 256-entry
    /// palette, which is where the 60–70% savings on screenshots and flat artwork come from — but it
    /// is lossy, and a user who asks for maximum quality is asking for the pixels back exactly.
    /// Above the threshold the palette step is skipped and the decoded RGBA pixels are preserved.
    /// </remarks>
    public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
    {
        var quality = Math.Clamp(request.Quality, 1, 100);

        // Probes use cheaper deflate; the target-size search checks the final encoded size again.
        var compressionLevel = request.Fast
            ? PngCompressionLevel.DefaultCompression
            : PngCompressionLevel.BestCompression;

        // Always measure the lossless candidate: smooth gradients can compress better
        // as true colour than as a palette, even at a lower requested quality.
        using var exact = new MemoryStream();
        Write(exact, palette: false);
        if (quality >= LosslessThreshold)
        {
            exact.WriteTo(output);
            return;
        }

        using var palette = new MemoryStream();
        Write(palette, palette: true);
        if (palette.Length < exact.Length)
        {
            palette.Position = 0;
            using var decoded = Image.Load<Rgba32>(palette);
            if (PngQuality.Accepts(image, decoded, quality))
            {
                palette.WriteTo(output);
                return;
            }
        }

        exact.WriteTo(output);

        void Write(Stream destination, bool palette)
        {
            var encoder = new PngEncoder
            {
                CompressionLevel = compressionLevel,
                ColorType = palette ? PngColorType.Palette : PngColorType.RgbWithAlpha,
                BitDepth = PngBitDepth.Bit8,
                Quantizer = palette ? CreateQuantizer(image, quality) : null,
                TransparentColorMode = PngTransparentColorMode.Preserve,
                SkipMetadata = false,
                ChunkFilter = request.StripMetadata
                    ? PngChunkFilter.ExcludeExifChunk | PngChunkFilter.ExcludeTextChunks
                    : PngChunkFilter.None,
            };
            EncodingMetadata.Write(image, request.StripMetadata, () => image.Save(destination, encoder));
        }
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

            // Personal metadata was removed during Encode. Preserve colour and display
            // chunks here; --strip all would change the appearance of unchanged pixels.

            // oxipng only ever writes the file back if its result is smaller, so there is no
            // size check to do here.
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            // Drain both redirected pipes so a verbose tool cannot block on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

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
