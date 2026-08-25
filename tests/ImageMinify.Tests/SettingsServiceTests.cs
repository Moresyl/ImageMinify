using ImageMinify.Models;
using ImageMinify.Services;

namespace ImageMinify.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"imageminify-settings-{Guid.NewGuid():N}");

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsEveryOption()
    {
        var fileA = Path.Combine(_tempDirectory, "a.jpg");
        var fileB = Path.Combine(_tempDirectory, "b.png");
        File.WriteAllText(fileA, "a");
        File.WriteAllText(fileB, "b");

        var service = new SettingsService(new InMemorySettingsStore());
        var snapshot = new AppSettingsSnapshot
        {
            Options = new CompressionOptions
            {
                Quality = 72,
                OutputFormat = "webp",
                ScaleMode = ScaleMode.LongestEdge,
                ScaleValue = 2400,
                StripMetadata = true,
                TargetSizeBytes = 500 * 1024L,
                SkipWhenLarger = false,
                OutputMode = OutputMode.CustomFolder,
                OutputFolder = _tempDirectory,
                Suffix = "_small",
                Recursive = true,
                MaxParallelism = 4,
            },
            IsDarkTheme = true,
            LastPaths = [fileA, fileB, _tempDirectory],
        };

        await service.SaveAsync(snapshot);
        var loaded = await service.LoadAsync();

        Assert.Equal(snapshot.Options, loaded.Options);
        Assert.True(loaded.IsDarkTheme);
        Assert.Equal(snapshot.LastPaths, loaded.LastPaths);
    }

    /// <summary>
    /// The target size is entered in kilobytes and stored in kilobytes, but held in bytes. A restart
    /// must not shave anything off it.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadAsync_KeepsTargetSizeExact()
    {
        var service = new SettingsService(new InMemorySettingsStore());

        await service.SaveAsync(new AppSettingsSnapshot
        {
            Options = new CompressionOptions { TargetSizeBytes = 250 * 1024L },
        });

        var loaded = await service.LoadAsync();

        Assert.Equal(250 * 1024L, loaded.Options.TargetSizeBytes);
    }

    /// <summary>
    /// A file that has since been deleted is dropped from the restored list; a folder is kept even
    /// when it is not reachable right now, because an unmounted share is still the user's selection.
    /// </summary>
    [Fact]
    public async Task LoadAsync_DropsMissingFilesFromTheRestoredSelection()
    {
        var present = Path.Combine(_tempDirectory, "present.jpg");
        var missing = Path.Combine(_tempDirectory, "missing.jpg");
        File.WriteAllText(present, "a");

        var service = new SettingsService(new InMemorySettingsStore());
        await service.SaveAsync(new AppSettingsSnapshot { LastPaths = [present, missing, _tempDirectory] });

        var loaded = await service.LoadAsync();

        Assert.Equal([present, _tempDirectory], loaded.LastPaths);
    }

    [Fact]
    public async Task LoadAsync_FallsBackToDefaultsOnAnEmptyStore()
    {
        var loaded = await new SettingsService(new InMemorySettingsStore()).LoadAsync();

        Assert.Equal(new CompressionOptions(), loaded.Options);
        Assert.False(loaded.IsDarkTheme);
        Assert.Empty(loaded.LastPaths);
    }

    /// <summary>
    /// A settings file written by a version that still had the rename feature must not keep carrying
    /// those keys around forever.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PurgesKeysFromRemovedFeatures()
    {
        var store = new InMemorySettingsStore();
        store.SetValue("RenameEnabled", true);
        store.SetValue("RenamePrefix", "batch");
        store.SetValue("RenameSeparator", "_");
        store.SetValue("RenameStartNumber", 10);
        store.SetValue("SelectLastMode", "files");

        await new SettingsService(store).SaveAsync(new AppSettingsSnapshot());

        Assert.False(store.Contains("RenameEnabled"));
        Assert.False(store.Contains("RenamePrefix"));
        Assert.False(store.Contains("RenameSeparator"));
        Assert.False(store.Contains("RenameStartNumber"));
        Assert.False(store.Contains("SelectLastMode"));
    }

    /// <summary>
    /// Around fifteen keys, one write. A save happens on every slider movement, so a write per key
    /// would be hundreds of writes to disk during one drag.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WritesTheStoreOnce()
    {
        var store = new InMemorySettingsStore();

        await new SettingsService(store).SaveAsync(new AppSettingsSnapshot());

        Assert.Equal(1, store.WriteCount);
    }

    /// <summary>An out-of-range quality in the store is clamped rather than handed to the encoder.</summary>
    [Fact]
    public async Task LoadAsync_ClampsQualityIntoRange()
    {
        var store = new InMemorySettingsStore();
        store.SetValue("CompressQuality", 500);

        var loaded = await new SettingsService(store).LoadAsync();

        Assert.Equal(100, loaded.Options.Quality);
    }

    /// <summary>A corrupt path list is not a reason to fail to start.</summary>
    [Fact]
    public async Task LoadAsync_IgnoresAnUnparseablePathList()
    {
        var store = new InMemorySettingsStore();
        store.SetValue("SelectLastPaths", "{not json");

        var loaded = await new SettingsService(store).LoadAsync();

        Assert.Empty(loaded.LastPaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
