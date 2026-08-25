namespace ImageMinify.Models;

/// <summary>
/// A named set of options for one job. Presets exist because the settings that matter — format,
/// quality, longest edge, metadata — are not independent: the combination that is right for a web
/// page is wrong for an archive, and picking each one separately is how people end up with a 4000px
/// JPEG at quality 95 attached to an email.
/// </summary>
/// <param name="Key">Stable identifier, persisted in settings.</param>
/// <param name="Name">Short label on the chip.</param>
/// <param name="Detail">The settings, spelled out, so choosing one is not a guess.</param>
public sealed record CompressionPreset(
    string Key,
    string Name,
    string Detail,
    string OutputFormat,
    int Quality,
    ScaleMode ScaleMode,
    int ScaleValue,
    bool StripMetadata)
{
    /// <summary>The built-in presets, in the order they are shown.</summary>
    public static IReadOnlyList<CompressionPreset> All { get; } =
    [
        new("web", "网页", "WebP · 质量 80 · 长边 1920 · 清除元数据", "webp", 80, ScaleMode.LongestEdge, 1920, true),
        new("social", "社交媒体", "JPEG · 质量 85 · 长边 2048 · 清除元数据", "jpeg", 85, ScaleMode.LongestEdge, 2048, true),
        new("archive", "存档", "保持原格式 · 质量 92 · 原始尺寸 · 保留元数据", "original", 92, ScaleMode.None, 0, false),
        new("minimum", "极限压缩", "WebP · 质量 60 · 长边 1280 · 清除元数据", "webp", 60, ScaleMode.LongestEdge, 1280, true),
    ];

    /// <summary>Applies this preset over <paramref name="options"/>, leaving output routing alone.</summary>
    public CompressionOptions ApplyTo(CompressionOptions options) => options with
    {
        OutputFormat = OutputFormat,
        Quality = Quality,
        ScaleMode = ScaleMode,
        ScaleValue = ScaleValue == 0 ? options.ScaleValue : ScaleValue,
        StripMetadata = StripMetadata,
        TargetSizeBytes = 0,
    };

    /// <summary>
    /// Whether <paramref name="options"/> currently matches this preset, so the view can show which
    /// chip is live and drop the highlight the moment a slider is touched.
    /// </summary>
    public bool Matches(CompressionOptions options)
    {
        if (options.TargetSizeBytes > 0)
        {
            return false;
        }

        if (!string.Equals(options.OutputFormat, OutputFormat, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.Quality != Quality || options.StripMetadata != StripMetadata || options.ScaleMode != ScaleMode)
        {
            return false;
        }

        // A preset with no scaling does not care what the scale box happens to hold.
        return ScaleMode == ScaleMode.None || options.ScaleValue == ScaleValue;
    }
}
