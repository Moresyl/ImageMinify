using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMinify.Helpers;
using ImageMinify.Models;
using ImageMinify.Services;

namespace ImageMinify.ViewModels;

public partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// How long to wait after the last settings change before writing to disk. Long enough that a
    /// slider drag is one write instead of two hundred, short enough that nothing is lost if the
    /// application is closed straight after a change.
    /// </summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Debounce for counting the images behind the source list, which costs a directory walk.</summary>
    private static readonly TimeSpan ScanDelay = TimeSpan.FromMilliseconds(250);

    private readonly IImageCompressor _compressor;
    private readonly SettingsService _settingsService;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _saveCts;
    private CancellationTokenSource? _updateCts;
    private TaskCompletionSource<bool>? _dialogCompletion;

    private bool _isInitializing;
    private IReadOnlyList<CompressionResult> _lastRunResults = [];
    private long _savedBytesThisRun;

    private string _updateStatusKey = "Str.Update.Ready";
    private object?[] _updateStatusArguments = [];

    public MainViewModel(IImageCompressor compressor, SettingsService settingsService, IUpdateService updateService)
    {
        _compressor = compressor;
        _settingsService = settingsService;
        _updateService = updateService;

        Summary = new CompressionSummary();
        EngineCapabilities = _compressor.GetCapabilities();

        Sources.CollectionChanged += OnSourcesChanged;

        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync, () => !IsCompressing);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, () => !IsCompressing);
        ClearSourcesCommand = new RelayCommand(ClearSources, () => !IsCompressing && Sources.Count > 0);
        RemoveSourceCommand = new RelayCommand<SourceItem>(RemoveSource, _ => !IsCompressing);

        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        CancelCommand = new RelayCommand(Cancel, () => IsCompressing);

        ChooseOutputFolderCommand = new AsyncRelayCommand(ChooseOutputFolderAsync, () => !IsCompressing);
        ApplyPresetCommand = new RelayCommand<CompressionPreset>(ApplyPreset, _ => !IsCompressing);

        OpenResultCommand = new RelayCommand<CompressionResult>(OpenResult);
        RevealResultCommand = new RelayCommand<CompressionResult>(RevealResult);
        PreviewResultCommand = new RelayCommand<CompressionResult>(OpenPreview);
        ClosePreviewCommand = new RelayCommand(ClosePreview);

        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => Results.Count > 0);
        ClearResultsCommand = new RelayCommand(ClearResults, () => !IsCompressing && Results.Count > 0);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        CheckForUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(quiet: false), CanManageUpdates);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => CanManageUpdates() && AvailableUpdate is not null);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync);
        OpenRepositoryCommand = new RelayCommand(() => Launch(AppInfo.RepositoryUrl));
        ReportIssueCommand = new RelayCommand(() => Launch(AppInfo.IssuesUrl));

        DialogConfirmCommand = new RelayCommand(() => CloseDialog(true));
        DialogCancelCommand = new RelayCommand(() => CloseDialog(false));
        DialogCopyCommand = new AsyncRelayCommand(CopyDialogAsync, () => CanCopyDialog);

        Results.CollectionChanged += (_, _) =>
        {
            ExportReportCommand.NotifyCanExecuteChanged();
            ClearResultsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasResults));
        };
    }

    // ==== Selection =========================================================================

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public ObservableCollection<CompressionResult> Results { get; } = [];

    /// <summary>
    /// The four preset chips. Observable so a language change can restate them; the presets
    /// themselves never change.
    /// </summary>
    public ObservableCollection<CompressionPreset> Presets { get; } = [.. CompressionPreset.All];

    /// <summary>
    /// What this build can read, for the open dialog's filter. Taken from the engine rather than
    /// spelled out again in the window, so the dialog cannot offer a format the run will reject.
    /// </summary>
    public IReadOnlyList<string> SupportedExtensions => _compressor.SupportedExtensions;

    /// <summary>Supplied by the view, which owns the platform's file dialogs.</summary>
    public Func<Task<IReadOnlyList<string>>>? SelectFilesHandler { get; set; }

    public Func<string, Task<string?>>? SelectFolderHandler { get; set; }

    public Func<string, Task<string?>>? SaveReportHandler { get; set; }

    /// <summary>Supplied by the window because the WPF clipboard requires the UI thread.</summary>
    public Func<string, Task>? CopyTextHandler { get; set; }

    [ObservableProperty]
    private string _sourceSummary = Strings.Get("Str.Source.Prompt");

    [ObservableProperty]
    private bool _isScanning;

    public bool HasSources => Sources.Count > 0;

    public bool HasResults => Results.Count > 0;

    // ==== Compression settings ==============================================================

    [ObservableProperty]
    private string _outputFormat = "original";

    [ObservableProperty]
    private int _quality = 82;

    [ObservableProperty]
    private ScaleMode _scaleMode = ScaleMode.None;

    [ObservableProperty]
    private int _scaleValue = 1920;

    [ObservableProperty]
    private bool _stripMetadata;

    /// <summary>
    /// The target size in kilobytes, as typed. Zero turns the search off, which is why the field is
    /// an int and not a nullable — "no target" and "a target of nothing" are the same instruction.
    /// </summary>
    [ObservableProperty]
    private int _targetSizeKb;

    [ObservableProperty]
    private bool _skipWhenLarger = true;

    [ObservableProperty]
    private OutputMode _outputMode = OutputMode.Suffix;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _suffix = "_compressed";

    [ObservableProperty]
    private bool _recursive;

    [ObservableProperty]
    private string _activePresetKey = string.Empty;

    /// <summary>
    /// True while the run would replace the user's originals. The view keeps this visible the whole
    /// time rather than only asking at the end, because by then the choice has already been made.
    /// </summary>
    public bool IsOverwriting => OutputMode == OutputMode.Overwrite;

    /// <summary>
    /// Overwrite has to keep the file's format — see <c>ImageCompressor.ResolveOutputExtension</c>.
    /// Rather than let the two be set to contradictory values and silently ignore one, the format
    /// picker goes dead while overwrite is on.
    /// </summary>
    public bool IsFormatSelectable => OutputMode != OutputMode.Overwrite;

    public bool IsScaleValueEnabled => ScaleMode != ScaleMode.None;

    /// <summary>The unit beside the scale box, which changes with the mode it belongs to.</summary>
    public string ScaleUnit => ScaleMode == ScaleMode.Percent ? "%" : "px";

    public bool IsQualityEnabled => TargetSizeKb <= 0;

    // ==== Run state =========================================================================

    [ObservableProperty]
    private bool _isCompressing;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private CompressionSummary _summary;

    [ObservableProperty]
    private EngineCapabilities _engineCapabilities;

    // ==== Dialog ============================================================================

    [ObservableProperty]
    private bool _isDialogOpen;

    [ObservableProperty]
    private string _dialogTitle = string.Empty;

    [ObservableProperty]
    private string _dialogMessage = string.Empty;

    [ObservableProperty]
    private string _dialogDetail = string.Empty;

    [ObservableProperty]
    private string _dialogCopyText = string.Empty;

    [ObservableProperty]
    private string _dialogCopyLabel = Strings.Get("Str.Dialog.CopyError");

    [ObservableProperty]
    private string _dialogConfirmText = Strings.Get("Str.Dialog.Ok");

    [ObservableProperty]
    private bool _isDialogDestructive;

    /// <summary>False for a message with one dismiss button, true when there is a choice to make.</summary>
    [ObservableProperty]
    private bool _isDialogCancellable;

    [ObservableProperty]
    private StatusSeverity _dialogSeverity = StatusSeverity.Informational;

    // ==== Preview ===========================================================================

    [ObservableProperty]
    private bool _isPreviewOpen;

    [ObservableProperty]
    private CompressionResult? _previewResult;

    /// <summary>
    /// Where the wipe between the two images sits, 0 to 1. Lives on the view model so it survives
    /// opening a different result, which keeps the comparison steady while stepping through a batch.
    /// </summary>
    [ObservableProperty]
    private double _previewSplit = 0.5;

    // ==== Appearance ========================================================================

    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>
    /// Which of the two languages the interface is in. A flag rather than the enum because the title
    /// bar offers one button, and a button that flips a two-state choice is the whole control.
    /// </summary>
    [ObservableProperty]
    private bool _isEnglish;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    private bool _isInstallingUpdate;

    [ObservableProperty]
    private int _updateProgress;

    [ObservableProperty]
    private UpdateRelease? _availableUpdate;

    [ObservableProperty]
    private string _updateStatus = Strings.Get("Str.Update.Ready");

    [ObservableProperty]
    private string _copyStatus = string.Empty;

    public string CurrentVersion => AppInfo.Version;

    public bool CanCopyDialog => !string.IsNullOrWhiteSpace(DialogCopyText) && CopyTextHandler is not null;

    public bool HasAvailableUpdate => AvailableUpdate is not null;

    public string UpdateNotes => ReleaseNotesFormatter.ForDisplay(AvailableUpdate?.Notes, Strings.Current);

    // ==== Commands ==========================================================================

    public AsyncRelayCommand AddFilesCommand { get; }

    public AsyncRelayCommand AddFolderCommand { get; }

    public RelayCommand ClearSourcesCommand { get; }

    public RelayCommand<SourceItem> RemoveSourceCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand ChooseOutputFolderCommand { get; }

    public RelayCommand<CompressionPreset> ApplyPresetCommand { get; }

    public RelayCommand<CompressionResult> OpenResultCommand { get; }

    public RelayCommand<CompressionResult> RevealResultCommand { get; }

    public RelayCommand<CompressionResult> PreviewResultCommand { get; }

    public RelayCommand ClosePreviewCommand { get; }

    public AsyncRelayCommand ExportReportCommand { get; }

    public RelayCommand ClearResultsCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand CloseSettingsCommand { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public AsyncRelayCommand InstallUpdateCommand { get; }

    public AsyncRelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand OpenRepositoryCommand { get; }

    public RelayCommand ReportIssueCommand { get; }

    public RelayCommand DialogConfirmCommand { get; }

    public RelayCommand DialogCancelCommand { get; }

    public AsyncRelayCommand DialogCopyCommand { get; }

    // ==== Lifecycle =========================================================================

    public async Task InitializeAsync()
    {
        _isInitializing = true;

        var snapshot = await _settingsService.LoadAsync();
        var options = snapshot.Options;

        OutputFormat = options.OutputFormat;
        Quality = options.Quality;
        ScaleMode = options.ScaleMode;
        ScaleValue = options.ScaleValue;
        StripMetadata = options.StripMetadata;
        TargetSizeKb = (int)(options.TargetSizeBytes / 1024);
        SkipWhenLarger = options.SkipWhenLarger;
        OutputMode = options.OutputMode;
        OutputFolder = options.OutputFolder;
        Suffix = options.Suffix;
        Recursive = options.Recursive;
        IsDarkTheme = snapshot.IsDarkTheme;
        IsEnglish = snapshot.Language == Language.English;
        AutoCheckUpdates = snapshot.AutoCheckUpdates;

        Sources.Clear();
        foreach (var item in snapshot.LastPaths.Select(SourceItem.TryCreate).OfType<SourceItem>())
        {
            Sources.Add(item);
        }

        EngineCapabilities = _compressor.GetCapabilities();
        RefreshActivePreset();

        _isInitializing = false;

        QueueScan();
        RefreshCommandStates();

        if (AutoCheckUpdates && _updateService.IsInstalled)
        {
            _updateCts = new CancellationTokenSource();
            _ = RunAutomaticUpdateCheckAsync(_updateCts.Token);
        }
    }

    /// <summary>Flushes any pending debounced save. Called as the window closes.</summary>
    public async Task ShutdownAsync()
    {
        _saveCts?.Cancel();
        _updateCts?.Cancel();
        await _settingsService.SaveAsync(BuildSnapshot());
    }

    // ==== Selection =========================================================================

    /// <summary>
    /// Adds dropped or picked paths. Duplicates are dropped, and so is any path already covered by
    /// a folder in the list — adding a folder and then a file inside it should not compress that
    /// file twice.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            var item = SourceItem.TryCreate(path);
            if (item is null || IsAlreadyCovered(item))
            {
                continue;
            }

            Sources.Add(item);
            added++;
        }

        if (added > 0)
        {
            QueueScan();
            QueueSave();
        }
    }

    private bool IsAlreadyCovered(SourceItem candidate)
    {
        foreach (var existing in Sources)
        {
            if (string.Equals(existing.Path, candidate.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (existing.IsDirectory && IsUnder(candidate.Path, existing.Path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnder(string path, string directory)
    {
        var normalised = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalised, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AddFilesAsync()
    {
        if (SelectFilesHandler is null)
        {
            return;
        }

        AddPaths(await SelectFilesHandler());
    }

    private async Task AddFolderAsync()
    {
        if (SelectFolderHandler is null)
        {
            return;
        }

        var folder = await SelectFolderHandler(Strings.Get("Str.Picker.SourceFolder"));
        if (!string.IsNullOrWhiteSpace(folder))
        {
            AddPaths([folder]);
        }
    }

    private void RemoveSource(SourceItem? item)
    {
        if (item is not null && Sources.Remove(item))
        {
            QueueScan();
            QueueSave();
        }
    }

    private void ClearSources()
    {
        Sources.Clear();
        QueueScan();
        QueueSave();
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSources));
        ClearSourcesCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Counts the images behind the current selection, off the UI thread and after a pause.
    /// </summary>
    /// <remarks>
    /// A folder on a network share can take seconds to walk, and the walk restarts on every edit to
    /// the list. Debouncing collapses a burst of drops into one scan; cancelling the previous one
    /// means an abandoned walk stops rather than racing the new one to publish its answer.
    /// </remarks>
    private void QueueScan()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        _ = ScanAsync(token);
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(ScanDelay, ct);

            if (Sources.Count == 0)
            {
                SourceSummary = Strings.Get("Str.Source.Prompt");
                IsScanning = false;
                return;
            }

            IsScanning = true;

            var paths = Sources.Select(source => source.Path).ToList();
            var options = BuildOptions();

            var (count, bytes) = await Task.Run(
                () =>
                {
                    var files = _compressor.Expand(paths, options);
                    var total = 0L;

                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            total += new FileInfo(file).Length;
                        }
                        catch (IOException)
                        {
                            // A file that vanished between the walk and the stat. It will be
                            // reported properly if the run reaches it.
                        }
                    }

                    return (files.Count, total);
                },
                ct);

            ct.ThrowIfCancellationRequested();

            SourceSummary = count == 0
                ? Strings.Get("Str.Source.None")
                : Strings.Format("Str.Source.Count", count, FileSizeFormatter.Format(bytes));
            IsScanning = false;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer scan, which will set the summary itself.
        }
        catch (Exception)
        {
            IsScanning = false;
            SourceSummary = Strings.Get("Str.Source.Unreadable");
        }
    }

    // ==== Running ===========================================================================

    private bool CanStart() => !IsCompressing && Sources.Count > 0;

    private async Task StartAsync()
    {
        if (!CanStart())
        {
            return;
        }

        if (OutputMode == OutputMode.Overwrite && !await ConfirmOverwriteAsync())
        {
            return;
        }

        if (OutputMode == OutputMode.CustomFolder && string.IsNullOrWhiteSpace(OutputFolder))
        {
            await ShowMessageAsync(
                Strings.Get("Str.Warn.NeedFolder.Title"),
                Strings.Get("Str.Warn.NeedFolder.Body"),
                StatusSeverity.Warning);
            return;
        }

        var options = BuildOptions();
        var paths = Sources.Select(source => source.Path).ToList();

        Results.Clear();
        Summary = new CompressionSummary();
        _lastRunResults = [];
        _savedBytesThisRun = 0;
        ProgressValue = 0;
        ProgressText = Strings.Get("Str.Progress.Scanning");
        IsCompressing = true;
        RefreshCommandStates();

        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        var cancelled = false;

        try
        {
            var files = await Task.Run(() => _compressor.Expand(paths, options), token);

            if (files.Count == 0)
            {
                await ShowMessageAsync(
                    Strings.Get("Str.Warn.NoImages.Title"),
                    Strings.Get("Str.Warn.NoImages.Body"),
                    StatusSeverity.Warning);
                return;
            }

            ProgressText = $"0 / {files.Count}";

            // Progress<T> captures the UI SynchronizationContext at construction, so the workers can
            // report from any thread and the handler still runs where the collections live.
            var progress = new Progress<CompressionProgress>(OnProgress);

            var (results, summary) = await _compressor.CompressBatchAsync(
                files,
                options,
                ResolveSourceRoot(),
                progress,
                token);

            _lastRunResults = results;
            Summary = summary;
            cancelled = token.IsCancellationRequested;

            await ReportRunAsync(summary, cancelled);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            await ShowExceptionAsync(
                Strings.Get("Str.Error.Run.Title"),
                Strings.Get("Str.Error.Run.Body"),
                ex);
        }
        finally
        {
            IsCompressing = false;
            ProgressValue = 0;
            ProgressText = cancelled ? Strings.Get("Str.Progress.Cancelled") : string.Empty;

            _runCts?.Dispose();
            _runCts = null;

            RefreshCommandStates();
            QueueSave();
        }
    }

    private void OnProgress(CompressionProgress progress)
    {
        ProgressValue = progress.Percent;
        _savedBytesThisRun += progress.Result.BytesSaved;

        ProgressText = _savedBytesThisRun > 0
            ? Strings.Format(
                "Str.Progress.Running",
                progress.Completed,
                progress.Total,
                FileSizeFormatter.Format(_savedBytesThisRun))
            : $"{progress.Completed} / {progress.Total}";

        Results.Add(progress.Result);
    }

    private void Cancel()
    {
        _runCts?.Cancel();
        ProgressText = Strings.Get("Str.Progress.Stopping");
    }

    /// <summary>
    /// The folder that sub-folder structure is measured against when writing to a custom folder.
    /// </summary>
    /// <remarks>
    /// Only meaningful when the whole selection sits under one folder. With several unrelated
    /// sources there is no shared root to mirror, so the destination stays flat rather than
    /// inventing a hierarchy from whichever source happened to be first.
    /// </remarks>
    private string? ResolveSourceRoot()
    {
        if (Sources.Count != 1)
        {
            return null;
        }

        var only = Sources[0];
        return only.IsDirectory ? only.Path : null;
    }

    private async Task ReportRunAsync(CompressionSummary summary, bool cancelled)
    {
        if (cancelled)
        {
            return;
        }

        var severity = summary.FailedFiles > 0 ? StatusSeverity.Warning : StatusSeverity.Success;
        var title = Strings.Get(summary.FailedFiles > 0 ? "Str.Summary.Title.Partial" : "Str.Summary.Title.Done");

        var message = summary.ProcessedFiles == 0
            ? Strings.Get("Str.Summary.NothingSmaller")
            : Strings.Format(
                "Str.Summary.Body",
                summary.ProcessedFiles,
                summary.TotalOriginalSizeFormatted,
                summary.TotalCompressedSizeFormatted,
                summary.BytesSavedFormatted,
                summary.OverallCompressionRatioDisplay);

        var detail = BuildRunDetail(summary);
        await ShowMessageAsync(title, message, severity, detail);
    }

    private static string BuildRunDetail(CompressionSummary summary)
    {
        var parts = new List<string> { Strings.Format("Str.Summary.Elapsed", summary.ElapsedDisplay) };

        if (summary.SkippedFiles > 0)
        {
            parts.Add(Strings.Format("Str.Summary.Skipped", summary.SkippedFiles));
        }

        if (summary.FailedFiles > 0)
        {
            parts.Add(Strings.Format("Str.Summary.Failed", summary.FailedFiles));
        }

        return string.Join(" · ", parts);
    }

    // ==== Settings ==========================================================================

    private CompressionOptions BuildOptions() => new()
    {
        OutputFormat = OutputFormat,
        Quality = Quality,
        ScaleMode = ScaleMode,
        ScaleValue = ScaleValue,
        StripMetadata = StripMetadata,
        TargetSizeBytes = Math.Max(0, TargetSizeKb) * 1024L,
        SkipWhenLarger = SkipWhenLarger,
        OutputMode = OutputMode,
        OutputFolder = OutputFolder,
        Suffix = string.IsNullOrWhiteSpace(Suffix) ? "_compressed" : Suffix,
        Recursive = Recursive,
    };

    private AppSettingsSnapshot BuildSnapshot() => new()
    {
        Options = BuildOptions(),
        IsDarkTheme = IsDarkTheme,
        Language = IsEnglish ? Language.English : Language.Chinese,
        AutoCheckUpdates = AutoCheckUpdates,
        LastPaths = Sources.Select(source => source.Path).ToList(),
    };

    private async Task ChooseOutputFolderAsync()
    {
        if (SelectFolderHandler is null)
        {
            return;
        }

        var folder = await SelectFolderHandler(Strings.Get("Str.Picker.OutputFolder"));
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        OutputFolder = folder;
        OutputMode = OutputMode.CustomFolder;
    }

    private void ApplyPreset(CompressionPreset? preset)
    {
        if (preset is null)
        {
            return;
        }

        // Set through the properties rather than swapping an options object, so each change raises
        // its own notification and the sliders and segments move to match.
        OutputFormat = preset.OutputFormat;
        Quality = preset.Quality;
        ScaleMode = preset.ScaleMode;
        StripMetadata = preset.StripMetadata;
        TargetSizeKb = 0;

        if (preset.ScaleMode != ScaleMode.None)
        {
            ScaleValue = preset.ScaleValue;
        }

        RefreshActivePreset();
    }

    private void RefreshActivePreset()
    {
        var options = BuildOptions();
        ActivePresetKey = Presets.FirstOrDefault(preset => preset.Matches(options))?.Key ?? string.Empty;
    }

    /// <summary>
    /// Schedules a settings save, replacing any save already pending.
    /// </summary>
    private void QueueSave()
    {
        if (_isInitializing)
        {
            return;
        }

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = SaveAfterDelayAsync(token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SaveDelay, ct);
            await _settingsService.SaveAsync(BuildSnapshot());
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later change, which carries this one's values too.
        }
        catch (Exception)
        {
            // A settings file that cannot be written is not worth interrupting the user over.
        }
    }

    // ==== Results ===========================================================================

    private void OpenResult(CompressionResult? result)
    {
        if (result is not null && File.Exists(result.CompressedPath))
        {
            Launch(result.CompressedPath);
        }
    }

    private void RevealResult(CompressionResult? result)
    {
        if (result is null)
        {
            return;
        }

        var path = File.Exists(result.CompressedPath) ? result.CompressedPath : result.OriginalPath;
        if (File.Exists(path))
        {
            // /select, opens the folder with the file already highlighted, which is what people mean
            // by "show me where it went".
            Launch("explorer.exe", $"/select,\"{path}\"");
        }
    }

    private void OpenPreview(CompressionResult? result)
    {
        if (result is null || !result.CanPreview)
        {
            return;
        }

        PreviewResult = result;
        IsPreviewOpen = true;
    }

    private void ClosePreview()
    {
        IsPreviewOpen = false;
        PreviewResult = null;
    }

    private void ClearResults()
    {
        Results.Clear();
        Summary = new CompressionSummary();
        _lastRunResults = [];
    }

    private async Task ExportReportAsync()
    {
        if (SaveReportHandler is null || Results.Count == 0)
        {
            return;
        }

        var suggested = $"ImageMinify-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var path = await SaveReportHandler(suggested);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var results = _lastRunResults.Count > 0 ? _lastRunResults : Results.ToList();
            await ReportExporter.WriteCsvAsync(path, results, Summary);
            await ShowMessageAsync(
                Strings.Get("Str.Report.Exported"),
                Path.GetFileName(path),
                StatusSeverity.Success,
                Path.GetDirectoryName(path) ?? string.Empty);
        }
        catch (Exception ex)
        {
            await ShowExceptionAsync(
                Strings.Get("Str.Report.Failed"),
                Strings.Get("Str.Error.Report.Body"),
                ex);
        }
    }

    private static void Launch(string fileName, string? arguments = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
            };

            if (arguments is not null)
            {
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
            }

            Process.Start(startInfo);
        }
        catch (Exception)
        {
            // No handler registered for the type, or the shell refused. Nothing useful to say.
        }
    }

    // ==== Appearance ========================================================================

    private void OpenSettings()
    {
        ClosePreview();
        IsSettingsOpen = true;
    }

    private void CloseSettings() => IsSettingsOpen = false;

    private bool CanManageUpdates() => !IsCheckingForUpdate && !IsInstallingUpdate;

    private async Task RunAutomaticUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            await CheckForUpdatesAsync(quiet: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Closing the window before the delayed check is expected.
        }
    }

    private async Task CheckForUpdatesAsync(bool quiet, CancellationToken cancellationToken = default)
    {
        if (!CanManageUpdates())
        {
            return;
        }

        IsCheckingForUpdate = true;
        SetUpdateStatus("Str.Update.Checking");
        RefreshUpdateCommands();

        try
        {
            if (!_updateService.IsInstalled)
            {
                AvailableUpdate = null;
                SetUpdateStatus("Str.Update.NotInstalled");
                return;
            }

            AvailableUpdate = await _updateService.CheckAsync(cancellationToken);
            SetUpdateStatus(
                AvailableUpdate is null ? "Str.Update.UpToDate" : "Str.Update.Available",
                AvailableUpdate?.Version ?? CurrentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Window shutdown or an explicitly cancelled check has no UI consequence.
        }
        catch (Exception ex)
        {
            AvailableUpdate = null;
            SetUpdateStatus("Str.Update.CheckFailed");
            if (!quiet)
            {
                await ShowExceptionAsync(
                    Strings.Get("Str.Update.CheckFailed.Title"),
                    Strings.Get("Str.Update.CheckFailed.Body"),
                    ex);
            }
        }
        finally
        {
            IsCheckingForUpdate = false;
            RefreshUpdateCommands();
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (!CanManageUpdates() || AvailableUpdate is not { } release)
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateProgress = 0;
        SetUpdateStatus("Str.Update.Downloading", 0);
        RefreshUpdateCommands();

        try
        {
            var progress = new Progress<int>(value =>
            {
                UpdateProgress = Math.Clamp(value, 0, 100);
                SetUpdateStatus("Str.Update.Downloading", UpdateProgress);
            });

            await _updateService.DownloadAndApplyAsync(release, progress);
            UpdateProgress = 100;
            SetUpdateStatus("Str.Update.Restarting");
        }
        catch (Exception ex)
        {
            SetUpdateStatus("Str.Update.InstallFailed");
            await ShowExceptionAsync(
                Strings.Get("Str.Update.InstallFailed.Title"),
                Strings.Get("Str.Update.InstallFailed.Body"),
                ex);
        }
        finally
        {
            IsInstallingUpdate = false;
            RefreshUpdateCommands();
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        if (CopyTextHandler is null)
        {
            return;
        }

        try
        {
            await CopyTextHandler(DiagnosticReport.Build());
            CopyStatus = Strings.Get("Str.Settings.DiagnosticsCopied");
        }
        catch (Exception ex)
        {
            await ShowExceptionAsync(
                Strings.Get("Str.Error.Copy.Title"),
                Strings.Get("Str.Error.Copy.Body"),
                ex);
        }
    }

    private async Task CopyDialogAsync()
    {
        if (!CanCopyDialog || CopyTextHandler is null)
        {
            return;
        }

        try
        {
            await CopyTextHandler(DialogCopyText);
            DialogCopyLabel = Strings.Get("Str.Dialog.Copied");
        }
        catch
        {
            DialogCopyLabel = Strings.Get("Str.Error.Copy.Title");
        }
    }

    private void SetUpdateStatus(string key, params object?[] arguments)
    {
        _updateStatusKey = key;
        _updateStatusArguments = arguments;
        UpdateStatus = arguments.Length == 0 ? Strings.Get(key) : Strings.Format(key, arguments);
    }

    private void RefreshUpdateCommands()
    {
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Puts the text this class owns into the language that has just been chosen.
    /// </summary>
    /// <remarks>
    /// Everything the markup says re-reads itself, because it asks for its strings by key. What is
    /// left is the text that was assembled in code and stored — a summary line, a status message —
    /// and the display strings on records, which resolve when read but have nothing to announce that
    /// they have changed. Both are handled here rather than through an event, so the one thing that
    /// changes the language is also the one thing that repairs what the change left behind.
    /// </remarks>
    private void RefreshLocalizedText()
    {
        // Re-walks the sources rather than reformatting a remembered count: this is the code that
        // knows all four things the line can say, and the walk is debounced and already paid for
        // every time the list changes.
        QueueScan();

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EngineCapabilities));
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(UpdateNotes));

        UpdateStatus = _updateStatusArguments.Length == 0
            ? Strings.Get(_updateStatusKey)
            : Strings.Format(_updateStatusKey, _updateStatusArguments);
        CopyStatus = string.Empty;
        if (CanCopyDialog)
        {
            DialogCopyLabel = Strings.Get("Str.Dialog.CopyError");
        }

        Restate(Presets);
        Restate(Results);
    }

    /// <summary>Nudges every row of <paramref name="items"/> so the bindings read it again.</summary>
    /// <remarks>
    /// Assigning an item over itself is what raises Replace for that index. It reads like a no-op and
    /// deliberately is not one: these records resolve their display strings when read, so the rows
    /// already hold the right words and only the notification is missing. Clearing and refilling
    /// would also work, and would take the count to zero on the way — long enough for the empty state
    /// to flash and for the selection to be lost.
    /// </remarks>
    private static void Restate<T>(ObservableCollection<T> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i] = items[i];
        }
    }

    // ==== Dialog ============================================================================

    /// <summary>Shows a message with a single dismiss button and waits for it to be dismissed.</summary>
    public Task ShowMessageAsync(string title, string message, StatusSeverity severity, string detail = "")
    {
        return ShowDialogAsync(
            title,
            message,
            detail,
            Strings.Get("Str.Dialog.Acknowledge"),
            severity,
            cancellable: false,
            destructive: false);
    }

    /// <summary>Shows a failure with a redacted, copyable diagnostic report.</summary>
    public Task ShowExceptionAsync(string title, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return ShowDialogAsync(
            title,
            message,
            DiagnosticReport.Redact(exception.Message),
            Strings.Get("Str.Dialog.Acknowledge"),
            StatusSeverity.Error,
            cancellable: false,
            destructive: false,
            copyText: DiagnosticReport.Build(exception));
    }

    private Task<bool> ConfirmOverwriteAsync()
    {
        var where = Sources.Count == 1
            ? Sources[0].DisplayName
            : Strings.Format("Str.Source.Locations", Sources.Count);

        return ShowDialogAsync(
            Strings.Get("Str.Confirm.Overwrite.Title"),
            Strings.Format("Str.Confirm.Overwrite.Body", where),
            Strings.Get("Str.Confirm.Overwrite.Detail"),
            Strings.Get("Str.Confirm.Overwrite.Confirm"),
            StatusSeverity.Warning,
            cancellable: true,
            destructive: true);
    }

    private Task<bool> ShowDialogAsync(
        string title,
        string message,
        string detail,
        string confirmText,
        StatusSeverity severity,
        bool cancellable,
        bool destructive,
        string copyText = "")
    {
        // A dialog opening while one is already up would strand the first one's awaiter forever.
        // Resolving it as a decline keeps that call site unblocked and matches what the user sees:
        // the question they were asked is gone without having been answered.
        _dialogCompletion?.TrySetResult(false);

        DialogTitle = title;
        DialogMessage = message;
        DialogDetail = detail;
        DialogCopyText = copyText;
        DialogCopyLabel = Strings.Get("Str.Dialog.CopyError");
        DialogConfirmText = confirmText;
        DialogSeverity = severity;
        IsDialogCancellable = cancellable;
        IsDialogDestructive = destructive;
        IsDialogOpen = true;

        _dialogCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _dialogCompletion.Task;
    }

    private void CloseDialog(bool confirmed)
    {
        IsDialogOpen = false;
        DialogCopyText = string.Empty;
        DialogCopyLabel = Strings.Get("Str.Dialog.CopyError");

        var completion = _dialogCompletion;
        _dialogCompletion = null;
        completion?.TrySetResult(confirmed);
    }

    // ==== Change handling ===================================================================

    private void RefreshCommandStates()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        ClearSourcesCommand.NotifyCanExecuteChanged();
        RemoveSourceCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ChooseOutputFolderCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
        ClearResultsCommand.NotifyCanExecuteChanged();
        RefreshUpdateCommands();
    }

    partial void OnOutputFormatChanged(string value)
    {
        RefreshActivePreset();
        QueueSave();
    }

    partial void OnQualityChanged(int value)
    {
        RefreshActivePreset();
        QueueSave();
    }

    partial void OnScaleModeChanged(ScaleMode value)
    {
        OnPropertyChanged(nameof(IsScaleValueEnabled));
        OnPropertyChanged(nameof(ScaleUnit));

        // The two modes measure completely different things, so carrying the number across gives
        // either a 1920% enlargement or an 80-pixel thumbnail. Reset to each mode's own default.
        if (!_isInitializing)
        {
            ScaleValue = value switch
            {
                ScaleMode.Percent => 50,
                ScaleMode.LongestEdge => 1920,
                _ => ScaleValue,
            };
        }

        RefreshActivePreset();
        QueueScan();
        QueueSave();
    }

    partial void OnScaleValueChanged(int value)
    {
        RefreshActivePreset();
        QueueSave();
    }

    partial void OnStripMetadataChanged(bool value)
    {
        RefreshActivePreset();
        QueueSave();
    }

    partial void OnTargetSizeKbChanged(int value)
    {
        OnPropertyChanged(nameof(IsQualityEnabled));
        RefreshActivePreset();
        QueueSave();
    }

    partial void OnSkipWhenLargerChanged(bool value) => QueueSave();

    partial void OnOutputModeChanged(OutputMode value)
    {
        OnPropertyChanged(nameof(IsOverwriting));
        OnPropertyChanged(nameof(IsFormatSelectable));

        // Overwrite cannot change the format, so leaving a conversion selected would show a setting
        // the run is going to ignore.
        if (value == OutputMode.Overwrite)
        {
            OutputFormat = "original";
        }

        QueueSave();
    }

    partial void OnOutputFolderChanged(string value) => QueueSave();

    partial void OnSuffixChanged(string value)
    {
        QueueScan();
        QueueSave();
    }

    partial void OnRecursiveChanged(bool value)
    {
        QueueScan();
        QueueSave();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.Apply(value);
        QueueSave();
    }

    partial void OnIsEnglishChanged(bool value)
    {
        Strings.Apply(value ? Language.English : Language.Chinese);
        RefreshLocalizedText();
        QueueSave();
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        QueueSave();

        if (_isInitializing)
        {
            return;
        }

        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;

        if (value && _updateService.IsInstalled)
        {
            _updateCts = new CancellationTokenSource();
            _ = RunAutomaticUpdateCheckAsync(_updateCts.Token);
        }
    }

    partial void OnDialogCopyTextChanged(string value)
    {
        OnPropertyChanged(nameof(CanCopyDialog));
        DialogCopyCommand.NotifyCanExecuteChanged();
    }

    partial void OnAvailableUpdateChanged(UpdateRelease? value)
    {
        OnPropertyChanged(nameof(HasAvailableUpdate));
        OnPropertyChanged(nameof(UpdateNotes));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCompressingChanged(bool value) => RefreshCommandStates();
}
