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

        // Excel reads a bare UTF-8 CSV as the system ANSI code page and turns every Chinese heading
        // into mojibake. A BOM is what makes it detect UTF-8 — written explicitly rather than left to
        // the encoder, because the file is also a plain-text artefact people diff and grep.
        builder.Append('﻿');

        builder.AppendLine("文件名,原始路径,输出路径,原始大小(字节),压缩后大小(字节),节省(字节),压缩率(%),原始尺寸,输出尺寸,质量,状态,耗时(毫秒),错误");

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
        builder.Append("合计,").Append(summary.TotalFiles.ToString(CultureInfo.InvariantCulture))
            .Append(" 个文件,已压缩 ").Append(summary.ProcessedFiles.ToString(CultureInfo.InvariantCulture))
            .Append(",跳过 ").Append(summary.SkippedFiles.ToString(CultureInfo.InvariantCulture))
            .Append(",失败 ").Append(summary.FailedFiles.ToString(CultureInfo.InvariantCulture))
            .Append(",节省 ").Append(summary.BytesSaved.ToString(CultureInfo.InvariantCulture))
            .Append(",压缩率 ").Append(summary.OverallCompressionRatio.ToString("F2", CultureInfo.InvariantCulture))
            .AppendLine();

        // Encoding without a second BOM: the one above is already in the text.
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    private static string Describe(ResultState state) => state switch
    {
        ResultState.Compressed => "已压缩",
        ResultState.Skipped => "已跳过",
        _ => "失败",
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
