using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>
/// One attempt at encoding an image.
/// </summary>
/// <param name="Quality">1–100, however the format chooses to interpret it.</param>
/// <param name="StripMetadata">Leave EXIF, IPTC, XMP and the colour profile out of the file.</param>
/// <param name="Fast">
/// This is a probe during the target-size search, not the file that will be kept, so trade a little
/// size for a lot of speed.
/// <para>
/// The search relies on a guarantee that runs the other way: every encoder's slow settings produce a
/// file no larger than its fast ones at the same quality. So a probe that comes in under the target
/// stays under it once re-encoded properly, and the search never has to verify its own answer.
/// </para>
/// </param>
public readonly record struct EncodeRequest(int Quality, bool StripMetadata, bool Fast = false);

/// <summary>
/// What the pipeline needs from a single output format. Implemented by the three real encoders and
/// by the pass-through used for formats with no settings of their own.
/// </summary>
public interface IFormatEncoder
{
    /// <summary>The file extension this encoder writes, including the dot.</summary>
    string Extension { get; }

    /// <summary>
    /// Whether <see cref="EncodeRequest.Quality"/> changes the output. False for formats that have
    /// only one representation, which is what tells the engine a target-size search is pointless.
    /// </summary>
    bool SupportsQuality { get; }

    /// <summary>
    /// Returns an image this encoder can accept, converting if it has to.
    /// </summary>
    /// <remarks>
    /// If the result is not the same instance as the input, the caller owns it and must dispose it.
    /// Kept separate from <see cref="Encode"/> because a conversion costs a full image copy and the
    /// target-size search encodes the same surface up to seven times.
    /// </remarks>
    Image<Rgba32> Prepare(Image<Rgba32> image);

    void Encode(Image<Rgba32> image, Stream output, EncodeRequest request);

    /// <summary>
    /// Runs any external lossless optimiser over the file that was just written. Best-effort: if the
    /// tool is absent or fails, the already-valid file it was given stays as it is.
    /// </summary>
    void PostProcess(string filePath, bool stripMetadata);
}
