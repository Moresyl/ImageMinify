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

    public string PngLossless { get; init; } = "未安装";

    public int ProcessorCount { get; init; } = 1;

    public string JpegOptimizerDisplay => $"JPEG · {JpegOptimizer}";

    public string PngQuantizationDisplay => $"PNG 量化 · {PngQuantization}";

    public string PngLosslessDisplay => $"PNG 无损 · {PngLossless}";

    public string ParallelismDisplay => $"并行 · {ProcessorCount} 线程";
}
