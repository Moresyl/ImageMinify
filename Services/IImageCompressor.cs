using ImageMinify.Models;

namespace ImageMinify.Services;

public interface IImageCompressor
{
    IReadOnlyList<string> SupportedExtensions { get; }

    bool IsSupportedFormat(string path);

    /// <summary>
    /// Turns a mixed list of files and folders into the list of images a run will actually touch:
    /// folders walked, unsupported files dropped, duplicates removed, and this tool's own previous
    /// output left alone.
    /// </summary>
    IReadOnlyList<string> Expand(IEnumerable<string> paths, CompressionOptions options);

    Task<CompressionResult> CompressAsync(
        string inputPath,
        CompressionOptions options,
        string? sourceRoot = null,
        CancellationToken ct = default);

    /// <summary>
    /// Compresses every file, several at a time, reporting each one as it finishes.
    /// </summary>
    /// <param name="sourceRoot">
    /// The folder the selection came from, used by <see cref="OutputMode.CustomFolder"/> to mirror
    /// sub-folders into the destination. Null flattens everything into one folder.
    /// </param>
    Task<(IReadOnlyList<CompressionResult> Results, CompressionSummary Summary)> CompressBatchAsync(
        IReadOnlyList<string> files,
        CompressionOptions options,
        string? sourceRoot = null,
        IProgress<CompressionProgress>? progress = null,
        CancellationToken ct = default);

    EngineCapabilities GetCapabilities();
}
