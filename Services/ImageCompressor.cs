using System.Diagnostics;
using ImageMinify.Helpers;
using ImageMinify.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageMinify.Services;

public sealed class ImageCompressor : IImageCompressor
{
    private static readonly IReadOnlyList<string> Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    /// <summary>The folder <see cref="OutputMode.Subfolder"/> writes into, beside each source file.</summary>
    internal const string SubfolderName = "compressed";

    /// <summary>Quality floor for the target-size search. Below this the image is not worth keeping.</summary>
    private const int MinimumSearchQuality = 5;

    private readonly ExifService _exifService;
    private readonly JpegCompressor _jpegCompressor;
    private readonly PngCompressor _pngCompressor;
    private readonly WebpCompressor _webpCompressor;
    private readonly BmpPassthroughEncoder _bmpEncoder = new();

    public ImageCompressor(
        ExifService exifService,
        JpegCompressor jpegCompressor,
        PngCompressor pngCompressor,
        WebpCompressor webpCompressor)
    {
        _exifService = exifService;
        _jpegCompressor = jpegCompressor;
        _pngCompressor = pngCompressor;
        _webpCompressor = webpCompressor;
    }

    public IReadOnlyList<string> SupportedExtensions => Extensions;

    public bool IsSupportedFormat(string path)
    {
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Expand(IEnumerable<string> paths, CompressionOptions options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();

        // IgnoreInaccessible keeps a single locked or permission-denied sub-folder from aborting the
        // walk of an entire drive, which is what the plain enumerator does.
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", enumeration))
                {
                    Add(file);
                }
            }
            else if (File.Exists(path))
            {
                Add(path);
            }
        }

        return files;

        void Add(string file)
        {
            if (!IsSupportedFormat(file) || IsOwnOutput(file, options))
            {
                return;
            }

            // Two selected folders can overlap, and a file reachable by two routes must still be
            // compressed once. The full path is the identity because the same name in two folders is
            // two different images.
            var key = TryGetFullPath(file);
            if (key is not null && seen.Add(key))
            {
                files.Add(key);
            }
        }
    }

    public Task<CompressionResult> CompressAsync(
        string inputPath,
        CompressionOptions options,
        string? sourceRoot = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => CompressCore(inputPath, options, sourceRoot, ct), ct);
    }

    public async Task<(IReadOnlyList<CompressionResult> Results, CompressionSummary Summary)> CompressBatchAsync(
        IReadOnlyList<string> files,
        CompressionOptions options,
        string? sourceRoot = null,
        IProgress<CompressionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();

        // Indexed rather than appended, so the returned list keeps the caller's order however the
        // workers finish. Distinct indices mean no lock is needed. Nullable because a cancelled run
        // leaves gaps, and those gaps are the files that never ran.
        var results = new CompressionResult?[files.Count];
        var completed = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.ResolveParallelism(),
            CancellationToken = ct,
        };

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, files.Count),
                parallelOptions,
                (index, token) =>
                {
                    // Encoding is CPU-bound with no await inside it, so it runs to completion on the
                    // worker Parallel already gave us. Wrapping it in another Task.Run would only add
                    // a hop and a second queue.
                    var result = CompressCore(files[index], options, sourceRoot, token);
                    results[index] = result;

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new CompressionProgress(done, files.Count, result));

                    return ValueTask.CompletedTask;
                });
        }
        catch (OperationCanceledException)
        {
            // A cancelled run still has real work to report. Everything already written stays
            // written, so the caller gets the results for the files that did finish rather than
            // losing the whole batch to the stop button.
            var partial = results.OfType<CompressionResult>().ToList();
            return (partial, CompressionSummary.FromResults(partial.Count, partial, Stopwatch.GetElapsedTime(started)));
        }

        var completedResults = results.OfType<CompressionResult>().ToList();
        return (completedResults, CompressionSummary.FromResults(files.Count, completedResults, Stopwatch.GetElapsedTime(started)));
    }

    public EngineCapabilities GetCapabilities()
    {
        return new EngineCapabilities
        {
            HasMozJpeg = _jpegCompressor.HasMozJpeg,
            HasImagequant = _pngCompressor.HasImagequant,
            HasOxipng = _pngCompressor.HasOxipng,
            PngQuantization = _pngCompressor.QuantizationName,
            JpegOptimizer = _jpegCompressor.OptimizerName,
            PngLossless = _pngCompressor.LosslessOptimizerName,
            ProcessorCount = Environment.ProcessorCount,
        };
    }

    private CompressionResult CompressCore(
        string inputPath,
        CompressionOptions options,
        string? sourceRoot,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();

        if (!IsSupportedFormat(inputPath))
        {
            return CompressionResult.Failure(inputPath, Strings.Get("Str.Error.UnsupportedFormat"));
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var originalSize = new FileInfo(inputPath).Length;
            var inputExtension = Path.GetExtension(inputPath);
            var outputExtension = ResolveOutputExtension(inputExtension, options);
            var encoder = ResolveEncoder(outputExtension);
            var outputPath = BuildOutputPath(inputPath, outputExtension, options, sourceRoot);

            // Image.Load reads the file to completion and closes the handle, so the source is not
            // locked afterwards — which is what lets Overwrite write back over this same path.
            using var image = Image.Load<Rgba32>(inputPath);

            // Before anything measures or resizes the image: see ExifService.AutoOrient for why.
            _exifService.AutoOrient(image);

            var originalWidth = image.Width;
            var originalHeight = image.Height;

            ApplyScale(image, options);

            if (options.StripMetadata)
            {
                _exifService.Strip(image);
            }
            else
            {
                _exifService.Reduce(image, inputPath);
            }

            var surface = encoder.Prepare(image);
            byte[] payload;
            int qualityUsed;
            bool metTarget;

            try
            {
                ct.ThrowIfCancellationRequested();
                (payload, qualityUsed, metTarget) = Encode(encoder, surface, options, ct);
            }
            finally
            {
                // Prepare returns the same instance when it had nothing to convert; disposing it
                // here would tear down the image the using block above still owns.
                if (!ReferenceEquals(surface, image))
                {
                    surface.Dispose();
                }
            }

            if (ShouldSkip(payload.Length, originalSize, inputExtension, outputExtension, options))
            {
                return new CompressionResult
                {
                    OriginalPath = inputPath,
                    CompressedPath = inputPath,
                    OriginalSize = originalSize,
                    CompressedSize = originalSize,
                    OriginalSizeFormatted = FileSizeFormatter.Format(originalSize),
                    CompressedSizeFormatted = FileSizeFormatter.Format(originalSize),
                    CompressionRatio = 0,
                    State = ResultState.Skipped,
                    OriginalWidth = originalWidth,
                    OriginalHeight = originalHeight,
                    Width = originalWidth,
                    Height = originalHeight,
                    QualityUsed = qualityUsed,
                    Elapsed = Stopwatch.GetElapsedTime(started),
                };
            }

            WriteAtomic(outputPath, payload);
            encoder.PostProcess(outputPath, options.StripMetadata);

            var compressedSize = new FileInfo(outputPath).Length;
            var ratio = originalSize == 0 ? 0 : (1 - (double)compressedSize / originalSize) * 100;

            return new CompressionResult
            {
                OriginalPath = inputPath,
                CompressedPath = outputPath,
                OriginalSize = originalSize,
                CompressedSize = compressedSize,
                OriginalSizeFormatted = FileSizeFormatter.Format(originalSize),
                CompressedSizeFormatted = FileSizeFormatter.Format(compressedSize),
                CompressionRatio = ratio,
                State = ResultState.Compressed,
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                Width = image.Width,
                Height = image.Height,
                QualityUsed = qualityUsed,
                TargetMissed = options.TargetSizeBytes > 0 && (!metTarget || compressedSize > options.TargetSizeBytes),
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CompressionResult.Failure(inputPath, ex.Message) with
            {
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
    }

    /// <summary>
    /// Produces the bytes to write, searching for a quality that meets the target size when one was
    /// asked for.
    /// </summary>
    private static (byte[] Payload, int Quality, bool MetTarget) Encode(
        IFormatEncoder encoder,
        Image<Rgba32> surface,
        CompressionOptions options,
        CancellationToken ct)
    {
        if (options.TargetSizeBytes <= 0 || !encoder.SupportsQuality)
        {
            var payload = EncodeToArray(encoder, surface, new EncodeRequest(options.Quality, options.StripMetadata));
            return (payload, options.Quality, true);
        }

        return SearchForTargetSize(encoder, surface, options, ct);
    }

    /// <summary>
    /// Finds the highest quality whose output fits <see cref="CompressionOptions.TargetSizeBytes"/>.
    /// </summary>
    /// <remarks>
    /// Bisection bounds the work to seven probes. Encoder decisions are not strictly monotonic,
    /// so this is a best-effort search rather than proof of the highest achievable quality.
    /// The selected quality is re-encoded and its final size is checked against the target.
    /// </remarks>
    private static (byte[] Payload, int Quality, bool MetTarget) SearchForTargetSize(
        IFormatEncoder encoder,
        Image<Rgba32> surface,
        CompressionOptions options,
        CancellationToken ct)
    {
        var target = options.TargetSizeBytes;
        var low = MinimumSearchQuality;
        var high = 100;
        var bestQuality = 0;

        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = (low + high) / 2;
            var probe = EncodeToArray(encoder, surface, new EncodeRequest(mid, options.StripMetadata, Fast: true));

            if (probe.Length <= target)
            {
                bestQuality = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (bestQuality == 0)
        {
            // The target is unreachable at any quality this format offers. Give back the smallest
            // file it can make and let the result say the target was missed, rather than silently
            // returning something many times over budget.
            var floor = EncodeToArray(encoder, surface, new EncodeRequest(MinimumSearchQuality, options.StripMetadata));
            return (floor, MinimumSearchQuality, false);
        }

        var payload = EncodeToArray(encoder, surface, new EncodeRequest(bestQuality, options.StripMetadata));
        return (payload, bestQuality, payload.Length <= target);
    }

    private static byte[] EncodeToArray(IFormatEncoder encoder, Image<Rgba32> image, EncodeRequest request)
    {
        using var buffer = new MemoryStream();
        encoder.Encode(image, buffer, request);
        return buffer.ToArray();
    }

    /// <summary>
    /// Whether to keep the original instead of writing a result that came out bigger.
    /// </summary>
    /// <remarks>
    /// Only when the run was a straight recompression. A format conversion or a resize was asked for
    /// explicitly and has to be delivered even if the bytes went up — someone converting a folder to
    /// WebP wants WebP files, not a folder that silently kept half its PNGs.
    /// </remarks>
    private static bool ShouldSkip(
        long compressedSize,
        long originalSize,
        string inputExtension,
        string outputExtension,
        CompressionOptions options)
    {
        if (!options.SkipWhenLarger || options.StripMetadata || compressedSize < originalSize)
        {
            return false;
        }

        return options.ScaleMode == ScaleMode.None
            && string.Equals(inputExtension, outputExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyScale(Image<Rgba32> image, CompressionOptions options)
    {
        var (width, height) = options.ScaleMode switch
        {
            ScaleMode.LongestEdge => ScaleToLongestEdge(image.Width, image.Height, options.ScaleValue),
            ScaleMode.Percent => ScaleByPercent(image.Width, image.Height, options.ScaleValue),
            _ => (image.Width, image.Height),
        };

        if (width == image.Width && height == image.Height)
        {
            return;
        }

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch,

            // Lanczos3 over the default bicubic: downscaling a photo by more than about half is
            // where a cheaper filter starts dropping detail into aliasing, and the sharper result
            // also compresses better because the noise a soft resample leaves behind is exactly what
            // a lossy encoder spends its bits on.
            Sampler = KnownResamplers.Lanczos3,
        }));
    }

    /// <summary>
    /// Fits the image inside a square box, never enlarging it. An image already inside the box is
    /// returned unchanged — "longest edge 1920" is a ceiling, not a target.
    /// </summary>
    private static (int Width, int Height) ScaleToLongestEdge(int width, int height, int longestEdge)
    {
        if (longestEdge <= 0 || Math.Max(width, height) <= longestEdge)
        {
            return (width, height);
        }

        var scale = (double)longestEdge / Math.Max(width, height);
        return (AtLeastOne(width * scale), AtLeastOne(height * scale));
    }

    private static (int Width, int Height) ScaleByPercent(int width, int height, int percent)
    {
        if (percent is <= 0 or >= 100)
        {
            return (width, height);
        }

        var scale = percent / 100D;
        return (AtLeastOne(width * scale), AtLeastOne(height * scale));
    }

    /// <summary>A dimension of zero is not an image, so the last pixel is never rounded away.</summary>
    private static int AtLeastOne(double value) => Math.Max(1, (int)Math.Round(value));

    private IFormatEncoder ResolveEncoder(string outputExtension) => outputExtension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => _jpegCompressor,
        ".png" => _pngCompressor,
        ".webp" => _webpCompressor,
        _ => _bmpEncoder,
    };

    private static string ResolveOutputExtension(string inputExtension, CompressionOptions options)
    {
        // Overwrite means replace this file, which is only coherent if the format stays put. The
        // view keeps the two from being chosen together; this is the guarantee behind it, so that no
        // path through the engine can leave an original stranded beside a differently-named result.
        if (options.OutputMode == OutputMode.Overwrite)
        {
            return inputExtension;
        }

        return options.OutputFormat.ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => ".jpg",
            "png" => ".png",
            "webp" => ".webp",
            _ => inputExtension,
        };
    }

    private static string BuildOutputPath(
        string inputPath,
        string outputExtension,
        CompressionOptions options,
        string? sourceRoot)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory;
        var name = Path.GetFileNameWithoutExtension(inputPath);

        return options.OutputMode switch
        {
            OutputMode.Overwrite => inputPath,
            OutputMode.Subfolder => Path.Combine(directory, SubfolderName, name + outputExtension),
            OutputMode.CustomFolder when !string.IsNullOrWhiteSpace(options.OutputFolder) =>
                Path.Combine(MirrorDirectory(options.OutputFolder, directory, sourceRoot), name + outputExtension),
            _ => Path.Combine(directory, name + options.Suffix + outputExtension),
        };
    }

    /// <summary>
    /// Reproduces the source's sub-folder structure under the destination.
    /// </summary>
    /// <remarks>
    /// Without this, a recursive run over a photo library with a folder per year flattens into one
    /// directory — and every <c>DSC_0001.jpg</c> overwrites the last. When the file did not come
    /// from under the root, it lands at the top rather than escaping the destination through a
    /// relative path full of <c>..</c>.
    /// </remarks>
    private static string MirrorDirectory(string destinationRoot, string fileDirectory, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return destinationRoot;
        }

        try
        {
            var relative = Path.GetRelativePath(sourceRoot, fileDirectory);
            if (relative == "." || Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            {
                return destinationRoot;
            }

            return Path.Combine(destinationRoot, relative);
        }
        catch (ArgumentException)
        {
            // Paths on different volumes have no relative form between them.
            return destinationRoot;
        }
    }

    /// <summary>
    /// Writes through a temporary file in the destination folder, then moves it into place.
    /// </summary>
    /// <remarks>
    /// The move is what makes Overwrite safe: a crash, a full disk or a stop halfway through leaves
    /// the original intact, because the original is only replaced once a complete file exists beside
    /// it. Writing straight to the destination would have a window where the user's only copy is a
    /// truncated one.
    /// </remarks>
    private static void WriteAtomic(string outputPath, byte[] payload)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? AppContext.BaseDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.part");

        try
        {
            File.WriteAllBytes(tempPath, payload);
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // The write already failed; the caller is being told about that, not about this.
            }

            throw;
        }
    }

    /// <summary>
    /// Whether this file looks like something a previous run produced.
    /// </summary>
    /// <remarks>
    /// Without this, the second run over a folder compresses <c>photo_compressed.jpg</c> into
    /// <c>photo_compressed_compressed.jpg</c>, and the third does it again. Recompressing an
    /// already-lossy file also destroys quality for almost no saving, so skipping is the right
    /// answer twice over.
    /// </remarks>
    private static bool IsOwnOutput(string file, CompressionOptions options)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(file));
        if (string.Equals(directory, SubfolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(options.Suffix)
            && Path.GetFileNameWithoutExtension(file).EndsWith(options.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }
}
