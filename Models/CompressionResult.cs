using ImageMinify.Helpers;

namespace ImageMinify.Models;

/// <summary>Why a file is not counted as compressed.</summary>
public enum ResultState
{
    /// <summary>A smaller file was written.</summary>
    Compressed,

    /// <summary>
    /// Compression made the file larger, so the original was kept. Not a failure — for an image
    /// that was already optimised it is the correct outcome, and saying so is more useful than
    /// reporting a negative saving.
    /// </summary>
    Skipped,

    /// <summary>The file could not be read, decoded or written. <see cref="CompressionResult.Error"/> says why.</summary>
    Failed,
}

public record CompressionResult
{
    public string OriginalPath { get; init; } = string.Empty;

    public string CompressedPath { get; init; } = string.Empty;

    public long OriginalSize { get; init; }

    public long CompressedSize { get; init; }

    public string OriginalSizeFormatted { get; init; } = "0 B";

    public string CompressedSizeFormatted { get; init; } = "0 B";

    public double CompressionRatio { get; init; }

    public ResultState State { get; init; } = ResultState.Failed;

    public string? Error { get; init; }

    /// <summary>Source pixel dimensions, for the row's detail line.</summary>
    public int OriginalWidth { get; init; }

    public int OriginalHeight { get; init; }

    /// <summary>Output pixel dimensions. Equal to the source unless the run scaled the image.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// The quality actually used. Differs from the requested quality when a target file size was
    /// set and the engine searched for the quality that met it — which is worth showing, because it
    /// is the number to reach for if the result looks wrong.
    /// </summary>
    public int QualityUsed { get; init; }

    /// <summary>
    /// A target size was set and the file still came out over it, because the format could not get
    /// there at any quality. Surfaced rather than swallowed: the fix is a smaller resize or a
    /// different format, and the user cannot know to reach for either if the run claims success.
    /// </summary>
    public bool TargetMissed { get; init; }

    public TimeSpan Elapsed { get; init; }

    public bool Success => State is ResultState.Compressed or ResultState.Skipped;

    /// <summary>
    /// Whether there are two different pictures to put either side of the wipe.
    /// </summary>
    /// <remarks>
    /// A failure wrote nothing and a skip kept the original, so in both cases the comparison would
    /// be a picture against itself. So would overwriting, where the output path <em>is</em> the input
    /// path and the file the run started from no longer exists to compare against. The row hides the
    /// eye when this is false rather than offering a button that opens a preview of nothing.
    /// </remarks>
    public bool CanPreview =>
        State == ResultState.Compressed &&
        !string.Equals(OriginalPath, CompressedPath, StringComparison.OrdinalIgnoreCase);

    public string FileName => Path.GetFileName(OriginalPath);

    public long BytesSaved => State == ResultState.Compressed ? OriginalSize - CompressedSize : 0;

    public string CompressionRatioDisplay => State switch
    {
        ResultState.Compressed => $"{CompressionRatio:F1}%",
        ResultState.Skipped => Strings.Get("Str.State.Skipped"),
        _ => Strings.Get("Str.State.Failed"),
    };

    /// <summary>The pixel dimensions for the row, with an arrow only when the run changed them.</summary>
    public string DimensionsDisplay
    {
        get
        {
            if (OriginalWidth == 0 || OriginalHeight == 0)
            {
                return string.Empty;
            }

            return Width == OriginalWidth && Height == OriginalHeight
                ? $"{OriginalWidth}×{OriginalHeight}"
                : $"{OriginalWidth}×{OriginalHeight} → {Width}×{Height}";
        }
    }

    public static CompressionResult Failure(string inputPath, string error) => new()
    {
        OriginalPath = inputPath,
        State = ResultState.Failed,
        Error = error,
    };
}
