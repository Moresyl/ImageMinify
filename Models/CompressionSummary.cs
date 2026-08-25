using ImageMinify.Helpers;

namespace ImageMinify.Models;

public record CompressionSummary
{
    public int TotalFiles { get; init; }

    public int ProcessedFiles { get; init; }

    public int SkippedFiles { get; init; }

    public int FailedFiles { get; init; }

    public long TotalOriginalSize { get; init; }

    public long TotalCompressedSize { get; init; }

    public string TotalOriginalSizeFormatted { get; init; } = "0 B";

    public string TotalCompressedSizeFormatted { get; init; } = "0 B";

    public double OverallCompressionRatio { get; init; }

    public TimeSpan Elapsed { get; init; }

    public long BytesSaved => Math.Max(0, TotalOriginalSize - TotalCompressedSize);

    public string BytesSavedFormatted => FileSizeFormatter.Format(BytesSaved);

    public string OverallCompressionRatioDisplay => $"{OverallCompressionRatio:F1}%";

    /// <summary>Wall-clock for the run, in the largest unit that keeps it readable.</summary>
    public string ElapsedDisplay => Elapsed.TotalSeconds < 1
        ? $"{Elapsed.TotalMilliseconds:F0} ms"
        : Elapsed.TotalSeconds < 60
            ? $"{Elapsed.TotalSeconds:F1} s"
            : $"{(int)Elapsed.TotalMinutes} 分 {Elapsed.Seconds} 秒";

    public bool HasResults => TotalFiles > 0;

    /// <summary>
    /// Totals over the files that were actually rewritten. Skipped files are excluded from both
    /// sides of the ratio on purpose: counting a file that was left alone as "0% saved" drags the
    /// headline figure down and makes a good run on a mixed folder look like a bad one.
    /// </summary>
    public static CompressionSummary FromResults(
        int totalFiles,
        IEnumerable<CompressionResult> results,
        TimeSpan elapsed = default)
    {
        var all = results as IReadOnlyList<CompressionResult> ?? results.ToList();
        var compressed = all.Where(result => result.State == ResultState.Compressed).ToList();

        var totalOriginalSize = compressed.Sum(result => result.OriginalSize);
        var totalCompressedSize = compressed.Sum(result => result.CompressedSize);
        var overallRatio = totalOriginalSize == 0
            ? 0
            : (1 - (double)totalCompressedSize / totalOriginalSize) * 100;

        return new CompressionSummary
        {
            TotalFiles = totalFiles,
            ProcessedFiles = compressed.Count,
            SkippedFiles = all.Count(result => result.State == ResultState.Skipped),
            FailedFiles = all.Count(result => result.State == ResultState.Failed),
            TotalOriginalSize = totalOriginalSize,
            TotalCompressedSize = totalCompressedSize,
            TotalOriginalSizeFormatted = FileSizeFormatter.Format(totalOriginalSize),
            TotalCompressedSizeFormatted = FileSizeFormatter.Format(totalCompressedSize),
            OverallCompressionRatio = overallRatio,
            Elapsed = elapsed,
        };
    }
}
