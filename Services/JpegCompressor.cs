using System.Diagnostics;
using ImageMinify.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageMinify.Services;

public sealed class JpegCompressor : IFormatEncoder
{
    private readonly string? _optimizerToolPath;

    public JpegCompressor()
    {
        _optimizerToolPath = NativeLibraryLoader.ResolveToolPath("jpegtran.exe", "jpegtran", "mozjpeg.exe", "mozjpeg");
    }

    public string Extension => ".jpg";

    public bool SupportsQuality => true;

    public bool HasMozJpeg => !string.IsNullOrWhiteSpace(_optimizerToolPath);

    public string OptimizerName => HasMozJpeg ? "MozJPEG" : "ImageSharp";

    /// <summary>
    /// Composites onto white if the image has any transparency, because JPEG has no alpha channel
    /// and an un-composited encode turns transparent pixels into whatever happened to be in the
    /// unused colour bytes — usually black.
    /// </summary>
    public Image<Rgba32> Prepare(Image<Rgba32> image)
    {
        if (!PixelInspector.HasTransparency(image))
        {
            return image;
        }

        var canvas = new Image<Rgba32>(image.Width, image.Height, Color.White);
        canvas.Mutate(context => context.DrawImage(image, 1F));

        // The composite is a new image and starts with no metadata. The pipeline has already decided
        // what this file should carry, so carry that decision across rather than re-deciding it here.
        canvas.Metadata.ExifProfile = image.Metadata.ExifProfile;
        canvas.Metadata.IccProfile = image.Metadata.IccProfile;
        return canvas;
    }

    public void Encode(Image<Rgba32> image, Stream output, EncodeRequest request)
    {
        var encoder = new JpegEncoder
        {
            Quality = Math.Clamp(request.Quality, 1, 100),
            Interleaved = true,
            SkipMetadata = false,
        };

        EncodingMetadata.Encode(image, output, encoder, request.StripMetadata, request.CancellationToken);
    }

    /// <summary>
    /// Rewrites the file's entropy coding with an optimised Huffman table and a progressive scan
    /// script. Both are lossless — not one pixel changes — and together they usually take another
    /// 5–15% off, which is free size the encoder above cannot reach on its own.
    /// </summary>
    public void PostProcess(string filePath, bool stripMetadata)
    {
        var toolPath = _optimizerToolPath;
        if (toolPath is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory;

        // A GUID rather than a fixed name: the batch runs one of these per core, and two workers in
        // the same folder would otherwise write over each other's intermediate file.
        var tempPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.mozopt");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            startInfo.ArgumentList.Add("-optimize");
            startInfo.ArgumentList.Add("-progressive");
            startInfo.ArgumentList.Add("-copy");
            // Encode has already removed personal profiles. Keep its display ICC profile.
            startInfo.ArgumentList.Add("all");
            startInfo.ArgumentList.Add("-outfile");
            startInfo.ArgumentList.Add(tempPath);
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(15000))
            {
                // The tool hung. Kill it and keep the encoder's own output, which is already a valid
                // JPEG — an optimiser that never returns must not hold up the rest of the batch.
                TryKill(process);
                return;
            }

            if (process.ExitCode != 0 || !File.Exists(tempPath))
            {
                return;
            }

            // Only take the result if it is genuinely smaller. jpegtran can produce a slightly larger
            // file from an image whose Huffman tables were already optimal, and quietly growing a
            // file inside a compression tool is worse than doing nothing.
            var optimisedSize = new FileInfo(tempPath).Length;
            if (optimisedSize > 0 && optimisedSize < new FileInfo(filePath).Length)
            {
                File.Copy(tempPath, filePath, overwrite: true);
            }
        }
        catch
        {
            // Lossless optimisation is a bonus pass. Keep the encoder's output when it is unavailable.
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone between the timeout and the kill.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A leftover temp file is not worth failing a compression over.
        }
    }
}
