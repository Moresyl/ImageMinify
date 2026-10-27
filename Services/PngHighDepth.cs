using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>Keep 16-bit PNG samples out of the 8-bit quantization pipeline.</summary>
internal static class PngHighDepth
{
    public static bool RequiresPreservation(string inputPath, string outputExtension)
    {
        if (!Path.GetExtension(inputPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            || !outputExtension.Equals(".png", StringComparison.OrdinalIgnoreCase)) return false;
        var info = Image.Identify(inputPath);
        return info.Metadata.DecodedImageFormat is PngFormat
            && info.Metadata.GetPngMetadata().BitDepth == PngBitDepth.Bit16;
    }

    public static byte[] Encode(Image<Rgba64> image, bool stripMetadata)
    {
        using var buffer = new MemoryStream();
        var encoder = new PngEncoder
        {
            BitDepth = PngBitDepth.Bit16,
            ColorType = PngColorType.RgbWithAlpha,
            CompressionLevel = PngCompressionLevel.BestCompression,
            TransparentColorMode = PngTransparentColorMode.Preserve,
            SkipMetadata = false,
            ChunkFilter = stripMetadata
                ? PngChunkFilter.ExcludeExifChunk | PngChunkFilter.ExcludeTextChunks
                : PngChunkFilter.None,
        };
        EncodingMetadata.Write(image, stripMetadata, () => image.Save(buffer, encoder));
        return buffer.ToArray();
    }
}
