using ImageMinify.Helpers;

namespace ImageMinify.Models;

/// <summary>
/// Which optional native tools were found on this machine. All three are genuine upgrades over the
/// managed encoders and none of them is required, so the view reports what is in play rather than
/// letting the user wonder why the same settings gave someone else a smaller file.
/// </summary>
public record EngineCapabilities
{
    public bool HasMozJpeg { get; init; }

    public bool HasImagequant { get; init; }

    public bool HasOxipng { get; init; }

    public string PngQuantization { get; init; } = "ImageSharp";

    public string JpegOptimizer { get; init; } = "ImageSharp";

    /// <summary>The lossless post-processor's name, or empty when there is none on this machine.</summary>
    /// <remarks>
    /// Empty rather than the words "not installed": those words are a translation, and a record that
    /// is built once at startup would hold whichever language happened to be live at the time.
    /// <see cref="PngLosslessDisplay"/> supplies them on read instead.
    /// </remarks>
    public string PngLossless { get; init; } = string.Empty;

    public int ProcessorCount { get; init; } = 1;

    public string JpegOptimizerDisplay => Strings.Format("Str.Engine.Jpeg", JpegOptimizer);

    public string PngQuantizationDisplay => Strings.Format("Str.Engine.PngQuantization", PngQuantization);

    public string PngLosslessDisplay => Strings.Format(
        "Str.Engine.PngLossless",
        PngLossless.Length == 0 ? Strings.Get("Str.Engine.NotInstalled") : PngLossless);

    public string ParallelismDisplay => Strings.Format("Str.Engine.Parallelism", ProcessorCount);
}
