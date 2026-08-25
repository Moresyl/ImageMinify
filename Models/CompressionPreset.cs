using ImageMinify.Helpers;

namespace ImageMinify.Models;

/// <summary>
/// A named set of options for one job. Presets exist because the settings that matter — format,
/// quality, longest edge, metadata — are not independent: the combination that is right for a web
/// page is wrong for an archive, and picking each one separately is how people end up with a 4000px
/// JPEG at quality 95 attached to an email.
/// </summary>
/// <param name="Key">Stable identifier, persisted in settings.</param>
/// <param name="NameKey">String-table key for the short label on the chip.</param>
/// <param name="DetailKey">String-table key for the settings, spelled out, so choosing one is not a guess.</param>
public sealed record CompressionPreset(
    string Key,
    string NameKey,
    string DetailKey,
    string OutputFormat,
    int Quality,
    ScaleMode ScaleMode,
    int ScaleValue,
    bool StripMetadata)
{
    /// <summary>The built-in presets, in the order they are shown.</summary>
    public static IReadOnlyList<CompressionPreset> All { get; } =
    [
        new("web", "Str.Preset.Web", "Str.Preset.Web.Detail", "webp", 80, ScaleMode.LongestEdge, 1920, true),
        new("social", "Str.Preset.Social", "Str.Preset.Social.Detail", "jpeg", 85, ScaleMode.LongestEdge, 2048, true),
        new("archive", "Str.Preset.Archive", "Str.Preset.Archive.Detail", "original", 92, ScaleMode.None, 0, false),
        new("minimum", "Str.Preset.Minimum", "Str.Preset.Minimum.Detail", "webp", 60, ScaleMode.LongestEdge, 1280, true),
    ];

    /// <summary>Short label on the chip.</summary>
    /// <remarks>
    /// Resolved on read rather than stored, so the four presets survive a language change without
    /// being rebuilt — <see cref="Key"/> is what the settings file and the highlight both hold on to.
    /// </remarks>
    public string Name => Strings.Get(NameKey);

    /// <summary>The settings, spelled out, so choosing one is not a guess.</summary>
    public string Detail => Strings.Get(DetailKey);

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
