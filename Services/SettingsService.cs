using System.Text.Json;
using System.Windows;
using ImageMinify.Models;

namespace ImageMinify.Services;

public sealed class SettingsService
{
    /// <summary>
    /// Keys written by earlier versions that nothing reads any more. Cleared on the first save so a
    /// long-lived settings file does not accumulate the debris of every feature the application has
    /// ever had — the four <c>Rename*</c> keys belonged to a feature that no longer exists, and the
    /// three <c>SelectLast*</c> keys to a selection model that was one-folder-or-some-files before
    /// the list became a single mixed set of sources.
    /// </summary>
    private static readonly string[] ObsoleteKeys =
    [
        "RenameEnabled",
        "RenamePrefix",
        "RenameSeparator",
        "RenameStartNumber",
        "SelectLastDirectory",
        "SelectLastFiles",
        "SelectLastMode",
    ];

    private readonly ISettingsStore _store;

    public SettingsService()
        : this(new WindowsSettingsStore())
    {
    }

    public SettingsService(ISettingsStore store)
    {
        _store = store;
    }

    public Task<AppSettingsSnapshot> LoadAsync()
    {
        var options = new CompressionOptions
        {
            Quality = Math.Clamp(ReadInt("CompressQuality", 82), 1, 100),
            OutputFormat = ReadString("CompressOutputFormat", "original"),
            ScaleMode = ReadEnum("ScaleMode", ScaleMode.None),
            ScaleValue = Math.Max(1, ReadInt("ScaleValue", 1920)),
            StripMetadata = ReadBool("StripMetadata"),

            // Stored in kilobytes because that is the unit the field is typed in; keeping the
            // persisted form the same as the entered form means no rounding drift across restarts.
            TargetSizeBytes = Math.Max(0, ReadInt("TargetSizeKb", 0)) * 1024L,
            SkipWhenLarger = ReadBool("SkipWhenLarger", defaultValue: true),
            OutputMode = ReadEnum("OutputMode", OutputMode.Suffix),
            OutputFolder = ReadString("OutputFolder", string.Empty),
            Suffix = ReadString("OutputSuffix", "_compressed"),
            Recursive = ReadBool("Recursive"),
            MaxParallelism = Math.Max(0, ReadInt("MaxParallelism", 0)),
        };

        return Task.FromResult(new AppSettingsSnapshot
        {
            Options = options,
            IsDarkTheme = ReadBool("IsDarkTheme"),

            // Folders are kept even when missing at load time — a network share or an external drive
            // that is not mounted right now is still the selection the user left behind. Only files
            // are pruned, because a file that is gone will only fail the run.
            LastPaths = ReadPaths("SelectLastPaths"),
        });
    }

    public Task SaveAsync(AppSettingsSnapshot snapshot)
    {
        var options = snapshot.Options;

        _store.SetValues(new Dictionary<string, object>
        {
            ["CompressQuality"] = options.Quality,
            ["CompressOutputFormat"] = options.OutputFormat,
            ["ScaleMode"] = options.ScaleMode.ToString(),
            ["ScaleValue"] = options.ScaleValue,
            ["StripMetadata"] = options.StripMetadata,
            ["TargetSizeKb"] = (int)(options.TargetSizeBytes / 1024),
            ["SkipWhenLarger"] = options.SkipWhenLarger,
            ["OutputMode"] = options.OutputMode.ToString(),
            ["OutputFolder"] = options.OutputFolder,
            ["OutputSuffix"] = options.Suffix,
            ["Recursive"] = options.Recursive,
            ["MaxParallelism"] = options.MaxParallelism,
            ["IsDarkTheme"] = snapshot.IsDarkTheme,
            ["SelectLastPaths"] = JsonSerializer.Serialize(snapshot.LastPaths),
        });

        _store.Remove(ObsoleteKeys);
        return Task.CompletedTask;
    }

    public void RestoreWindowPlacement(Window window)
    {
        var width = ReadDouble("WindowWidth", 1180);
        var height = ReadDouble("WindowHeight", 800);

        window.Width = Math.Max(window.MinWidth, width);
        window.Height = Math.Max(window.MinHeight, height);

        var left = ReadDouble("WindowX", double.NaN);
        var top = ReadDouble("WindowY", double.NaN);

        // No stored position, or a position that is no longer on any attached screen — a laptop
        // undocked from the monitor its window was on. Fall back to WPF's own centring rather than
        // restoring the window to coordinates the user cannot reach.
        if (double.IsNaN(left) || double.IsNaN(top) || !IsOnScreen(left, top, window.Width, window.Height))
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left;
        window.Top = top;
    }

    public void SaveWindowPlacement(Window window)
    {
        // RestoreBounds is the size the window would return to; Width and Height while maximized are
        // the screen's. Saving the latter means a window maximized at close reopens filling the
        // screen but not actually maximized, and can never be un-maximized back to a sensible size.
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsNaN(bounds.X) || double.IsNaN(bounds.Y))
        {
            return;
        }

        _store.SetValues(new Dictionary<string, object>
        {
            ["WindowWidth"] = bounds.Width,
            ["WindowHeight"] = bounds.Height,
            ["WindowX"] = bounds.X,
            ["WindowY"] = bounds.Y,
        });
    }

    /// <summary>
    /// Whether the window's title bar would land somewhere the user can grab it.
    /// </summary>
    /// <remarks>
    /// Checks the top strip rather than the whole rectangle: a window hanging off the bottom of a
    /// screen is a nuisance, but one whose caption is entirely off-screen cannot be moved back at
    /// all without the keyboard.
    /// </remarks>
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        _ = height;

        var caption = new Rect(left, top, width, 48);

        foreach (var screen in SystemScreens())
        {
            if (screen.IntersectsWith(caption))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The virtual desktop as one rectangle.
    /// </summary>
    /// <remarks>
    /// WPF exposes no per-monitor list without a WinForms reference, and the union is enough for the
    /// question being asked: a stored position from a monitor that is no longer attached falls
    /// outside it, and one from a monitor that is still there falls inside.
    /// </remarks>
    private static IEnumerable<Rect> SystemScreens()
    {
        yield return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private IReadOnlyList<string> ReadPaths(string key)
    {
        var json = _store.GetValue(key) as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var paths = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private TEnum ReadEnum<TEnum>(string key, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        return _store.GetValue(key) is string text && Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
    }

    private int ReadInt(string key, int defaultValue)
    {
        return _store.GetValue(key) switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)doubleValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private bool ReadBool(string key, bool defaultValue = false)
    {
        return _store.GetValue(key) switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            int intValue => intValue != 0,
            _ => defaultValue,
        };
    }

    private double ReadDouble(string key, double defaultValue)
    {
        return _store.GetValue(key) switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when double.TryParse(stringValue, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private string ReadString(string key, string defaultValue)
    {
        return _store.GetValue(key) as string ?? defaultValue;
    }
}
