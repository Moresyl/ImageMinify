using System.Diagnostics;
using ImageMinify.Helpers;
using ImageMinify.Models;
using ImageMinify.Services;
using ImageMinify.ViewModels;

namespace ImageMinify.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-vm-{Guid.NewGuid():N}");

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task InitializeAsync_RestoresSettingsAndSources()
    {
        var filePath = CreateFile("sample.jpg");

        var settingsService = new SettingsService(new InMemorySettingsStore());
        await settingsService.SaveAsync(new AppSettingsSnapshot
        {
            Options = new CompressionOptions
            {
                Quality = 64,
                OutputFormat = "png",
                ScaleMode = ScaleMode.LongestEdge,
                ScaleValue = 1280,
                StripMetadata = true,
                TargetSizeBytes = 300 * 1024L,
                OutputMode = OutputMode.Subfolder,
                Suffix = "_tiny",
                Recursive = true,
            },
            IsDarkTheme = true,
            LastPaths = [filePath],
        });

        var viewModel = Create(settingsService);
        await viewModel.InitializeAsync();

        Assert.Equal(64, viewModel.Quality);
        Assert.Equal("png", viewModel.OutputFormat);
        Assert.Equal(ScaleMode.LongestEdge, viewModel.ScaleMode);
        Assert.Equal(1280, viewModel.ScaleValue);
        Assert.True(viewModel.StripMetadata);
        Assert.Equal(300, viewModel.TargetSizeKb);
        Assert.Equal(OutputMode.Subfolder, viewModel.OutputMode);
        Assert.Equal("_tiny", viewModel.Suffix);
        Assert.True(viewModel.Recursive);
        Assert.True(viewModel.IsDarkTheme);
        Assert.Equal(filePath, Assert.Single(viewModel.Sources).Path);
        Assert.NotNull(viewModel.EngineCapabilities);
    }

    /// <summary>
    /// Restoring a saved scale must not trip the mode-change reset. That reset exists so that
    /// switching from percent to longest-edge does not carry "50" across as 50 pixels — but during
    /// a restore the mode and the value belong together and neither may be rewritten.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_DoesNotResetTheRestoredScaleValue()
    {
        var settingsService = new SettingsService(new InMemorySettingsStore());
        await settingsService.SaveAsync(new AppSettingsSnapshot
        {
            Options = new CompressionOptions { ScaleMode = ScaleMode.Percent, ScaleValue = 25 },
        });

        var viewModel = Create(settingsService);
        await viewModel.InitializeAsync();

        Assert.Equal(25, viewModel.ScaleValue);
    }

    [Fact]
    public async Task StartCommand_CompressesEverySourceAndBuildsTheSummary()
    {
        var fileA = CreateFile("a.jpg");
        var fileB = CreateFile("b.png");

        var viewModel = Create();
        await viewModel.InitializeAsync();
        viewModel.AddPaths([fileA, fileB]);

        await RunToCompletionAsync(viewModel);

        Assert.Equal(2, viewModel.Summary.TotalFiles);
        Assert.Equal(2, viewModel.Summary.ProcessedFiles);
        Assert.Equal(40d, viewModel.Summary.OverallCompressionRatio, 3);
        Assert.Equal(80, viewModel.Summary.BytesSaved);
        Assert.False(viewModel.IsCompressing);

        await WaitUntilAsync(() => viewModel.Results.Count == 2);
    }

    /// <summary>
    /// A folder and a file inside it are the same image. Adding both must not queue it twice.
    /// </summary>
    [Fact]
    public void AddPaths_DropsDuplicatesAndPathsCoveredByAFolder()
    {
        var filePath = CreateFile("a.jpg");

        var viewModel = Create();
        viewModel.AddPaths([_tempDirectory, filePath, _tempDirectory]);

        Assert.Equal(_tempDirectory, Assert.Single(viewModel.Sources).Path);
    }

    /// <summary>
    /// Overwriting is irreversible, so it is the one run that may not start without being confirmed.
    /// </summary>
    [Fact]
    public async Task StartCommand_DeclinedOverwriteConfirmationRunsNothing()
    {
        var filePath = CreateFile("a.jpg");

        var viewModel = Create();
        await viewModel.InitializeAsync();
        viewModel.AddPaths([filePath]);
        viewModel.OutputMode = OutputMode.Overwrite;

        var run = viewModel.StartCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.IsDialogOpen);

        Assert.True(viewModel.IsDialogDestructive);
        Assert.True(viewModel.IsDialogCancellable);

        viewModel.DialogCancelCommand.Execute(null);
        await run;

        Assert.Empty(viewModel.Results);
        Assert.False(viewModel.IsCompressing);
    }

    /// <summary>
    /// Overwrite writes back to the source path, so the output extension is fixed by the input. The
    /// view model has to say so too, or the rail offers a format the run will silently ignore.
    /// </summary>
    [Fact]
    public void OutputMode_OverwriteForcesTheOriginalFormat()
    {
        var viewModel = Create();
        viewModel.OutputFormat = "webp";

        viewModel.OutputMode = OutputMode.Overwrite;

        Assert.Equal("original", viewModel.OutputFormat);
        Assert.False(viewModel.IsFormatSelectable);
        Assert.True(viewModel.IsOverwriting);
    }

    /// <summary>
    /// Percent and pixels are different units. Carrying the number across a mode change would turn a
    /// 1920px cap into a 1920% enlargement.
    /// </summary>
    [Theory]
    [InlineData(ScaleMode.Percent, 50)]
    [InlineData(ScaleMode.LongestEdge, 1920)]
    public void ScaleMode_ResetsTheValueToTheNewModesDefault(ScaleMode mode, int expected)
    {
        var viewModel = Create();
        viewModel.ScaleValue = 7777;

        viewModel.ScaleMode = mode;

        Assert.Equal(expected, viewModel.ScaleValue);
        Assert.True(viewModel.IsScaleValueEnabled);
    }

    [Fact]
    public void TargetSize_TakesTheQualitySliderOutOfPlay()
    {
        var viewModel = Create();
        Assert.True(viewModel.IsQualityEnabled);

        viewModel.TargetSizeKb = 200;

        Assert.False(viewModel.IsQualityEnabled);
    }

    [Fact]
    public void ApplyPreset_SetsEveryOptionThePresetOwnsAndMarksItActive()
    {
        var viewModel = Create();
        var preset = viewModel.Presets.First(item => item.Key == "web");

        viewModel.ApplyPresetCommand.Execute(preset);

        Assert.Equal(preset.Quality, viewModel.Quality);
        Assert.Equal(preset.OutputFormat, viewModel.OutputFormat);
        Assert.Equal(preset.ScaleMode, viewModel.ScaleMode);
        Assert.Equal(preset.ScaleValue, viewModel.ScaleValue);
        Assert.Equal(preset.StripMetadata, viewModel.StripMetadata);
        Assert.Equal(0, viewModel.TargetSizeKb);
        Assert.Equal("web", viewModel.ActivePresetKey);
    }

    /// <summary>
    /// Editing a preset's settings by hand has to release the preset button, or the rail claims a
    /// preset that no longer describes what the run will do.
    /// </summary>
    [Fact]
    public void ActivePreset_ClearsWhenASettingIsChangedByHand()
    {
        var viewModel = Create();
        viewModel.ApplyPresetCommand.Execute(viewModel.Presets.First(item => item.Key == "web"));

        viewModel.Quality = 13;

        Assert.Equal(string.Empty, viewModel.ActivePresetKey);
    }

    [Fact]
    public void ClearSources_EmptiesTheListAndDisablesStart()
    {
        var viewModel = Create();
        viewModel.AddPaths([CreateFile("a.jpg")]);

        viewModel.ClearSourcesCommand.Execute(null);

        Assert.Empty(viewModel.Sources);
        Assert.False(viewModel.HasSources);
        Assert.False(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveSource_TakesOutOnlyTheOneAskedFor()
    {
        var fileA = CreateFile("a.jpg");
        var fileB = CreateFile("b.jpg");

        var viewModel = Create();
        viewModel.AddPaths([fileA, fileB]);

        viewModel.RemoveSourceCommand.Execute(viewModel.Sources[0]);

        Assert.Equal(fileB, Assert.Single(viewModel.Sources).Path);
    }

    /// <summary>
    /// A second dialog opening while one is up must not strand the first one's awaiter. It resolves
    /// as a decline, which is what the user sees: the question disappeared unanswered.
    /// </summary>
    [Fact]
    public async Task ShowMessageAsync_ASecondDialogReleasesTheFirst()
    {
        var viewModel = Create();

        var first = viewModel.ShowMessageAsync("一", "第一个", StatusSeverity.Informational);
        var second = viewModel.ShowMessageAsync("二", "第二个", StatusSeverity.Informational);

        await first;

        Assert.True(viewModel.IsDialogOpen);
        Assert.Equal("二", viewModel.DialogTitle);

        viewModel.DialogConfirmCommand.Execute(null);
        await second;

        Assert.False(viewModel.IsDialogOpen);
    }

    [Fact]
    public async Task ShutdownAsync_FlushesSettingsThatAreStillWaitingOnTheDebounce()
    {
        var settingsService = new SettingsService(new InMemorySettingsStore());
        var viewModel = Create(settingsService);
        await viewModel.InitializeAsync();

        viewModel.Quality = 37;
        await viewModel.ShutdownAsync();

        var loaded = await settingsService.LoadAsync();
        Assert.Equal(37, loaded.Options.Quality);
    }

    [Fact]
    public async Task InitializeAsync_RestoresLanguageAndUpdatePreference()
    {
        var settingsService = new SettingsService(new InMemorySettingsStore());
        await settingsService.SaveAsync(new AppSettingsSnapshot
        {
            Language = Language.English,
            AutoCheckUpdates = false,
        });

        var viewModel = Create(settingsService);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsEnglish);
        Assert.False(viewModel.AutoCheckUpdates);
        Assert.Equal(Language.English, Strings.Current);

        viewModel.IsEnglish = false;
        await viewModel.ShutdownAsync();

        var restored = await settingsService.LoadAsync();
        Assert.Equal(Language.Chinese, restored.Language);
    }

    [Fact]
    public async Task Settings_UpdateFlowChecksDownloadsAndReportsProgress()
    {
        var update = new FakeUpdateService
        {
            IsInstalled = true,
            Release = new UpdateRelease("3.1.0", "New release notes"),
        };
        var viewModel = Create(updateService: update);
        await viewModel.InitializeAsync();

        viewModel.OpenSettingsCommand.Execute(null);
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSettingsOpen);
        Assert.True(viewModel.HasAvailableUpdate);
        Assert.Equal("3.1.0", viewModel.AvailableUpdate?.Version);

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.UpdateProgress == 100);

        Assert.True(update.Applied);
        Assert.Equal(100, viewModel.UpdateProgress);

        viewModel.CloseSettingsCommand.Execute(null);
        Assert.False(viewModel.IsSettingsOpen);
    }

    [Fact]
    public async Task ExceptionDialog_CopiesRedactedDiagnostics()
    {
        var viewModel = Create();
        string? copied = null;
        viewModel.CopyTextHandler = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        var dialog = viewModel.ShowExceptionAsync(
            "Failure",
            "Something failed",
            new InvalidOperationException("token=should-not-leak"));

        Assert.True(viewModel.CanCopyDialog);
        await viewModel.DialogCopyCommand.ExecuteAsync(null);

        Assert.Contains("token=[REDACTED]", copied);
        Assert.DoesNotContain("should-not-leak", copied);

        viewModel.DialogConfirmCommand.Execute(null);
        await dialog;
    }

    /// <summary>
    /// The command and the row's eye have to agree about which results can be compared, or a row
    /// offers a button that opens an empty stage.
    /// </summary>
    [Fact]
    public void PreviewResultCommand_OpensOnlyWhatCanBeCompared()
    {
        var viewModel = Create();

        viewModel.PreviewResultCommand.Execute(new CompressionResult
        {
            OriginalPath = @"C:\a.jpg",
            CompressedPath = @"C:\a.jpg",
            State = ResultState.Skipped,
        });

        Assert.False(viewModel.IsPreviewOpen);
        Assert.Null(viewModel.PreviewResult);

        var comparable = new CompressionResult
        {
            OriginalPath = @"C:\a.jpg",
            CompressedPath = @"C:\a_compressed.jpg",
            State = ResultState.Compressed,
        };

        viewModel.PreviewResultCommand.Execute(comparable);

        Assert.True(viewModel.IsPreviewOpen);
        Assert.Same(comparable, viewModel.PreviewResult);
    }

    // ==== Helpers =============================================================================

    private MainViewModel Create(SettingsService? settingsService = null, IUpdateService? updateService = null) =>
        new(
            new FakeImageCompressor(),
            settingsService ?? new SettingsService(new InMemorySettingsStore()),
            updateService ?? new FakeUpdateService());

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, "data");
        return path;
    }

    /// <summary>
    /// Runs a compression to the end, answering the completion dialog it finishes with.
    /// </summary>
    /// <remarks>
    /// A run always reports itself and waits for that report to be dismissed, so awaiting the command
    /// alone would hang. This is the view model behaving correctly, not a test-only quirk — the same
    /// wait is what keeps the summary on screen until the user has read it.
    /// </remarks>
    private static async Task RunToCompletionAsync(MainViewModel viewModel)
    {
        var run = viewModel.StartCommand.ExecuteAsync(null);

        await WaitUntilAsync(() => viewModel.IsDialogOpen);
        viewModel.DialogConfirmCommand.Execute(null);

        await run;
    }

    /// <summary>
    /// Polls until a condition holds. Needed because results arrive through <see cref="Progress{T}"/>,
    /// which posts its callbacks rather than running them inline.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                Assert.Fail($"条件在 {timeoutMs} ms 内没有满足。");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// An engine that writes a stub file and reports a fixed 40% saving, so the view model's own
    /// behaviour is what the assertions are measuring.
    /// </summary>
    private sealed class FakeImageCompressor : IImageCompressor
    {
        public IReadOnlyList<string> SupportedExtensions { get; } = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

        public bool IsSupportedFormat(string path) =>
            SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Expand(IEnumerable<string> paths, CompressionOptions options)
        {
            var files = new List<string>();

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var search = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    files.AddRange(Directory.EnumerateFiles(path, "*", search).Where(IsSupportedFormat));
                }
                else if (File.Exists(path) && IsSupportedFormat(path))
                {
                    files.Add(path);
                }
            }

            return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<CompressionResult> CompressAsync(
            string inputPath,
            CompressionOptions options,
            string? sourceRoot = null,
            CancellationToken ct = default)
        {
            var targetPath = options.OutputMode == OutputMode.Overwrite
                ? inputPath
                : Path.Combine(
                    Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory,
                    Path.GetFileNameWithoutExtension(inputPath) + options.Suffix + Path.GetExtension(inputPath));

            File.WriteAllText(targetPath, "compressed");

            return Task.FromResult(new CompressionResult
            {
                OriginalPath = inputPath,
                CompressedPath = targetPath,
                OriginalSize = 100,
                CompressedSize = 60,
                OriginalSizeFormatted = "100.00 B",
                CompressedSizeFormatted = "60.00 B",
                CompressionRatio = 40,
                State = ResultState.Compressed,
                QualityUsed = options.Quality,
            });
        }

        public async Task<(IReadOnlyList<CompressionResult> Results, CompressionSummary Summary)> CompressBatchAsync(
            IReadOnlyList<string> files,
            CompressionOptions options,
            string? sourceRoot = null,
            IProgress<CompressionProgress>? progress = null,
            CancellationToken ct = default)
        {
            var results = new List<CompressionResult>();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var result = await CompressAsync(file, options, sourceRoot, ct);
                results.Add(result);
                progress?.Report(new CompressionProgress(results.Count, files.Count, result));
            }

            return (results, CompressionSummary.FromResults(files.Count, results));
        }

        public EngineCapabilities GetCapabilities() => new()
        {
            HasMozJpeg = true,
            HasImagequant = true,
            HasOxipng = true,
            JpegOptimizer = "MozJPEG",
            PngQuantization = "imagequant",
            PngLossless = "oxipng",
            ProcessorCount = 8,
        };
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsInstalled { get; init; }

        public UpdateRelease? Release { get; init; }

        public bool Applied { get; private set; }

        public Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Release);

        public Task DownloadAndApplyAsync(
            UpdateRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(25);
            progress?.Report(100);
            Applied = true;
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
