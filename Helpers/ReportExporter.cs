using System.Globalization;
using System.Text;
using ImageMinify.Models;

namespace ImageMinify.Helpers;

/// <summary>
/// Writes a run's results as CSV.
/// </summary>
public static class ReportExporter
{
    public static async Task WriteCsvAsync(
        string path,
        IEnumerable<CompressionResult> results,
        CompressionSummary summary,
        CancellationToken ct = default)
    {
        var builder = new StringBuilder();

        // Excel reads a bare UTF-8 CSV as the system ANSI code page and turns every non-ASCII heading
        // or path into mojibake. A BOM is what makes it detect UTF-8 — written explicitly rather than
        // left to the encoder, because the file is also a plain-text artefact people diff and grep.
        builder.Append('﻿');

        builder.AppendLine(Strings.Get("Str.Report.Header"));

        foreach (var result in results)
        {
            ct.ThrowIfCancellationRequested();

            builder
                .Append(Escape(result.FileName)).Append(',')
                .Append(Escape(result.OriginalPath)).Append(',')
                .Append(Escape(result.CompressedPath)).Append(',')
                .Append(result.OriginalSize.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.CompressedSize.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.BytesSaved.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.CompressionRatio.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape($"{result.OriginalWidth}x{result.OriginalHeight}")).Append(',')
                .Append(Escape($"{result.Width}x{result.Height}")).Append(',')
                .Append(result.QualityUsed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(Describe(result.State))).Append(',')
                .Append(result.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(result.Error ?? string.Empty))
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine(Strings.Format(
            "Str.Report.Total",
            summary.TotalFiles.ToString(CultureInfo.InvariantCulture),
            summary.ProcessedFiles.ToString(CultureInfo.InvariantCulture),
            summary.SkippedFiles.ToString(CultureInfo.InvariantCulture),
            summary.FailedFiles.ToString(CultureInfo.InvariantCulture),
            summary.BytesSaved.ToString(CultureInfo.InvariantCulture),
            summary.OverallCompressionRatio.ToString("F2", CultureInfo.InvariantCulture)));

        // Encoding without a second BOM: the one above is already in the text.
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    private static string Describe(ResultState state) => state switch
    {
        ResultState.Compressed => Strings.Get("Str.State.Compressed"),
        ResultState.Skipped => Strings.Get("Str.State.Skipped"),
        _ => Strings.Get("Str.State.Failed"),
    };

    /// <summary>
    /// Quotes a field if it contains anything that would otherwise break the row apart.
    /// </summary>
    /// <remarks>
    /// Windows paths routinely contain commas, and an error message can contain a newline. Either
    /// one silently shifts every following column of that row without this.
    /// </remarks>
    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
