using ImageMinify.Helpers;

namespace ImageMinify.Models;

/// <summary>
/// Everything that survives a restart.
/// </summary>
/// <remarks>
/// The compression settings are held as a whole <see cref="CompressionOptions"/> rather than copied
/// out field by field. When the engine gains an option there is then exactly one place to add it,
/// and no way for the snapshot to drift out of step with what a run actually uses.
/// </remarks>
public record AppSettingsSnapshot
{
    public CompressionOptions Options { get; init; } = new();

    public bool IsDarkTheme { get; init; }

    /// <summary>
    /// The language the interface was in. Stored as the enum rather than an "is English" flag so the
    /// settings file names the choice, and so a third language would be a row in the string table
    /// rather than a change to what is persisted.
    /// </summary>
    public Language Language { get; init; } = Language.Chinese;

    /// <summary>Whether the app quietly checks GitHub Releases after the window has opened.</summary>
    public bool AutoCheckUpdates { get; init; } = true;

    /// <summary>
    /// The files and folders that were in the list when the window closed, in order. Mixed on
    /// purpose — the selection is a single list of sources, not a mode that is either "a folder" or
    /// "some files".
    /// </summary>
    public IReadOnlyList<string> LastPaths { get; init; } = [];
}
