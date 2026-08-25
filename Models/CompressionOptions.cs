namespace ImageMinify.Models;

/// <summary>How the output image is scaled relative to the source.</summary>
public enum ScaleMode
{
    /// <summary>Keep the source's pixel dimensions.</summary>
    None,

    /// <summary>
    /// Fit inside a box whose longest side is <see cref="CompressionOptions.ScaleValue"/> pixels,
    /// preserving the aspect ratio. Images already smaller than the box are left alone.
    /// </summary>
    LongestEdge,

    /// <summary>Scale to <see cref="CompressionOptions.ScaleValue"/> percent of the source.</summary>
    Percent,
}

/// <summary>Where the compressed file is written.</summary>
public enum OutputMode
{
    /// <summary>Beside the original, with <see cref="CompressionOptions.Suffix"/> on the name.</summary>
    Suffix,

    /// <summary>Into a <c>compressed</c> folder beside the original, keeping the original name.</summary>
    Subfolder,

    /// <summary>Into <see cref="CompressionOptions.OutputFolder"/>, mirroring any sub-folder structure.</summary>
    CustomFolder,

    /// <summary>
    /// Over the original file. Destructive and irreversible, so the view is required to confirm it
    /// before a run and to make the current mode visible at all times.
    /// </summary>
    Overwrite,
}

/// <summary>
/// Everything a single run needs to know. Passed whole rather than as a parameter list because the
/// engine grew past the point where positional arguments could be read at the call site — and
/// because a record makes a run's exact settings capturable for the report.
/// </summary>
public sealed record CompressionOptions
{
    /// <summary><c>original</c>, <c>jpeg</c>, <c>png</c> or <c>webp</c>.</summary>
    public string OutputFormat { get; init; } = "original";

    /// <summary>
    /// 1–100. Drives the JPEG and WebP encoders directly, and the size of the PNG palette. Ignored
    /// when <see cref="TargetSizeBytes"/> is set, which searches for its own quality.
    /// </summary>
    public int Quality { get; init; } = 82;

    public ScaleMode ScaleMode { get; init; } = ScaleMode.None;

    /// <summary>Pixels for <see cref="ScaleMode.LongestEdge"/>, percent for <see cref="ScaleMode.Percent"/>.</summary>
    public int ScaleValue { get; init; } = 1920;

    /// <summary>
    /// Drop EXIF, IPTC, XMP and the colour profile. Worth a few kilobytes on a phone photo, and it
    /// is also what removes the GPS coordinates before an image goes anywhere public.
    /// </summary>
    public bool StripMetadata { get; init; }

    /// <summary>
    /// Aim for a file no larger than this many bytes by searching for the quality that lands under
    /// it. Zero disables the search and uses <see cref="Quality"/> as given.
    /// </summary>
    public long TargetSizeBytes { get; init; }

    /// <summary>
    /// Keep the original when compression made the file bigger. Re-encoding an already-optimised
    /// image usually does, and without this a "compression" run can quietly inflate a folder.
    /// </summary>
    public bool SkipWhenLarger { get; init; } = true;

    public OutputMode OutputMode { get; init; } = OutputMode.Suffix;

    public string OutputFolder { get; init; } = string.Empty;

    public string Suffix { get; init; } = "_compressed";

    /// <summary>Walk sub-directories when the source is a folder.</summary>
    public bool Recursive { get; init; }

    /// <summary>
    /// How many images to encode at once. Zero means one per processor. Encoding is CPU-bound and
    /// each image is independent, so this scales close to linearly until the disk becomes the limit.
    /// </summary>
    public int MaxParallelism { get; init; }

    /// <summary>
    /// The number of workers to actually start, resolved against the machine. Capped at 16: past
    /// that the gain is inside the noise and the memory held by that many decoded bitmaps is not.
    /// </summary>
    public int ResolveParallelism()
    {
        var requested = MaxParallelism > 0 ? MaxParallelism : Environment.ProcessorCount;
        return Math.Clamp(requested, 1, 16);
    }
}
