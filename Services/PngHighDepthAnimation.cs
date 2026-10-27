using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>
/// Encode full 16-bit frames independently. ImageSharp 3.1's animation deduplication
/// converts later frames through Rgba32, losing precision even with a 16-bit IHDR.
/// Chunk layout follows https://www.w3.org/TR/png-3/#11acTL.
/// </summary>
internal static class PngHighDepthAnimation
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static byte[] Encode(Image<Rgba64> image, bool stripMetadata, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var metadata = image.Metadata.GetPngMetadata();
        uint sequence = 0;
        for (var index = 0; index < image.Frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var frame = image.Frames.CloneFrame(index);
            using var encoded = new MemoryStream();
            // All frames must share the IHDR layout, including opaque frames.
            EncodingMetadata.Encode(frame, encoded, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit16,
                ColorType = PngColorType.RgbWithAlpha,
                CompressionLevel = PngCompressionLevel.BestCompression,
                TransparentColorMode = PngTransparentColorMode.Preserve,
                SkipMetadata = false,
                ChunkFilter = stripMetadata
                    ? PngChunkFilter.ExcludeExifChunk | PngChunkFilter.ExcludeTextChunks
                    : PngChunkFilter.None,
            }, stripMetadata, cancellationToken);
            var png = encoded.GetBuffer().AsSpan(0, checked((int)encoded.Length));
            if (index == 0) output.Write(png[..8]);
            var wroteControl = false;
            for (var offset = 8; offset < png.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4)));
                var type = png.Slice(offset + 4, 4);
                var data = png.Slice(offset + 8, length);
                if (type.SequenceEqual("IDAT"u8))
                {
                    if (!wroteControl)
                    {
                        if (index == 0)
                        {
                            var animation = new byte[8];
                            BinaryPrimitives.WriteUInt32BigEndian(animation,
                                (uint)(image.Frames.Count - (metadata.AnimateRootFrame ? 0 : 1)));
                            BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(4), metadata.RepeatCount);
                            WriteChunk(output, "acTL"u8, animation);
                        }
                        if (index > 0 || metadata.AnimateRootFrame)
                            WriteControl(output, image, index, sequence++);
                        wroteControl = true;
                    }
                    if (index == 0) output.Write(png.Slice(offset, length + 12));
                    else
                    {
                        var frameData = new byte[checked(length + 4)];
                        BinaryPrimitives.WriteUInt32BigEndian(frameData, sequence++);
                        data.CopyTo(frameData.AsSpan(4));
                        WriteChunk(output, "fdAT"u8, frameData);
                    }
                }
                else if (index == 0 && !type.SequenceEqual("IEND"u8))
                    output.Write(png.Slice(offset, length + 12));
                offset = checked(offset + length + 12);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteControl(Stream output, Image<Rgba64> image, int index, uint sequence)
    {
        var control = new byte[26];
        var delay = image.Frames[index].Metadata.GetPngMetadata().FrameDelay;
        BinaryPrimitives.WriteUInt32BigEndian(control, sequence);
        BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4), (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(8), (uint)image.Height);
        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), checked((ushort)delay.Numerator));
        BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), checked((ushort)delay.Denominator));
        // Decoded frames contain the complete canvas. Source blend and no disposal
        // replace it in full, preserving transparency without compositing twice.
        WriteChunk(output, "fcTL"u8, control);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            table[index] = crc;
        }
        return table;
    }
}
