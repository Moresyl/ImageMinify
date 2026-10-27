using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageMinify.Services;

/// <summary>
/// One attempt at encoding an image.
/// </summary>
/// <param name="Quality">1–100, however the format chooses to interpret it.</param>
/// <param name="StripMetadata">Remove personal metadata, keeping the display colour profile.</param>
/// <param name="Fast">
/// This is a probe during the target-size search, not the file that will be kept, so trade a little
/// size for a lot of speed.
/// <para>
/// Encoder effort and palette decisions can change the size in either direction.
/// The search must check the final encoded size before reporting that the target was met.
/// </para>
/// </param>
public readonly record struct EncodeRequest(int Quality, bool StripMetadata, bool Fast = false)
{
    public CancellationToken CancellationToken { get; init; }
}

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
